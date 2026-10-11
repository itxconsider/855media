using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public interface IPFlowCredentialStore
{
    Task<string> SaveAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        CancellationToken ct = default
    );
    Task<PFlowCredential> LoadAsync(
        PFlowAccountBinding binding,
        string reference,
        CancellationToken ct = default
    );
    void Delete(string reference);
}

// DPAPI CurrentUser storage. Only opaque references belong in normal account configuration.
public sealed class PFlowCredentialStore(string? directory = null) : IPFlowCredentialStore
{
    private const int MaximumEncryptedBytes = 1024 * 1024;

    private static bool ValidFields(
        byte[] login,
        byte[] password,
        byte[] factor,
        PFlowTwoFactorKind kind
    )
    {
        if (
            login.Length is 0 or > 1024
            || password.Length is 0 or > 65536
            || factor.Length > 1024
            || login.AsSpan().Contains((byte)'\n')
            || login.AsSpan().Contains((byte)'\r')
            || password.AsSpan().Contains((byte)'\n')
            || password.AsSpan().Contains((byte)'\r')
        )
            return false;
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetCharCount(login);
            utf8.GetCharCount(password);
            if (kind == PFlowTwoFactorKind.None)
                return factor.Length == 0;
            if (kind != PFlowTwoFactorKind.TotpSecret || factor.Length == 0)
                return false;
            PFlowTotp.Generate(utf8.GetString(factor), DateTimeOffset.UnixEpoch);
            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or FormatException)
        {
            return false;
        }
    }

    private readonly string _directory = Path.GetFullPath(
        directory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "855Media",
                "credentials"
            )
    );

    public async Task<string> SaveAsync(
        PFlowAccountBinding binding,
        PFlowCredential credential,
        CancellationToken ct = default
    )
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Protected PFlow credentials require Windows DPAPI."
            );
        binding.Validate();
        ct.ThrowIfCancellationRequested();
        if (credential.TwoFactorKind == PFlowTwoFactorKind.OneTimeCode)
            throw new InvalidOperationException(
                "Expiring one-time codes cannot be saved as reusable credentials."
            );
        if (
            credential.IsDisposed
            || !ValidFields(
                credential.LoginIdentifier,
                credential.Password,
                credential.TwoFactor,
                credential.TwoFactorKind
            )
        )
            throw new InvalidOperationException("Credentials are incomplete.");
        var reference = Guid.NewGuid().ToString("N");
        var path = Resolve(reference);
        Directory.CreateDirectory(_directory);
        using var plain = new MemoryStream();
        try
        {
            using (var writer = new BinaryWriter(plain, Encoding.UTF8, true))
            {
                writer.Write(1);
                writer.Write((int)credential.TwoFactorKind);
                foreach (
                    var field in new[]
                    {
                        credential.LoginIdentifier,
                        credential.Password,
                        credential.TwoFactor,
                    }
                )
                {
                    writer.Write(field.Length);
                    writer.Write(field);
                }
            }
            var bytes = plain.ToArray();
            byte[] encrypted;
            try
            {
                encrypted = ProtectedData.Protect(
                    bytes,
                    Entropy(binding),
                    DataProtectionScope.CurrentUser
                );
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
            try
            {
                await using var output = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                );
                await output.WriteAsync(encrypted, ct);
                output.Flush(true);
                ct.ThrowIfCancellationRequested();
            }
            catch
            {
                if (File.Exists(path))
                    File.Delete(path);
                throw;
            }
            return reference;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain.GetBuffer());
        }
    }

    public async Task<PFlowCredential> LoadAsync(
        PFlowAccountBinding binding,
        string reference,
        CancellationToken ct = default
    )
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Protected PFlow credentials require Windows DPAPI."
            );
        binding.Validate();
        ct.ThrowIfCancellationRequested();
        byte[] encrypted;
        await using (
            var input = new FileStream(
                Resolve(reference),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            )
        )
        {
            if (input.Length is <= 0 or > MaximumEncryptedBytes)
                throw new InvalidDataException("Protected credential file has an invalid size.");
            encrypted = new byte[(int)input.Length];
            await input.ReadExactlyAsync(encrypted, ct);
        }
        ct.ThrowIfCancellationRequested();
        var bytes = ProtectedData.Unprotect(
            encrypted,
            Entropy(binding),
            DataProtectionScope.CurrentUser
        );
        byte[]? login = null;
        byte[]? password = null;
        byte[]? factor = null;
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadInt32() != 1)
                throw new InvalidDataException("Unsupported protected credential version.");
            var kind = (PFlowTwoFactorKind)reader.ReadInt32();
            if (kind is not (PFlowTwoFactorKind.None or PFlowTwoFactorKind.TotpSecret))
                throw new InvalidDataException("Invalid protected 2FA kind.");
            byte[] Field()
            {
                var length = reader.ReadInt32();
                if (length < 0 || length > 65536 || length > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid protected credential field.");
                return reader.ReadBytes(length);
            }
            login = Field();
            password = Field();
            factor = Field();
            if (stream.Position != stream.Length || !ValidFields(login, password, factor, kind))
                throw new InvalidDataException("Protected credentials are incomplete.");
            ct.ThrowIfCancellationRequested();
            return new(login, password, factor, kind);
        }
        catch
        {
            if (login != null)
                CryptographicOperations.ZeroMemory(login);
            if (password != null)
                CryptographicOperations.ZeroMemory(password);
            if (factor != null)
                CryptographicOperations.ZeroMemory(factor);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Delete(string reference)
    {
        var path = Resolve(reference);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string Resolve(string reference)
    {
        if (
            !Guid.TryParseExact(reference, "N", out var parsed)
            || parsed.ToString("N") != reference
        )
            throw new ArgumentException("Invalid credential reference.", nameof(reference));
        return Path.Combine(_directory, reference + ".bin");
    }

    private static byte[] Entropy(PFlowAccountBinding binding) =>
        SHA256.HashData(
            Encoding.UTF8.GetBytes("855Media.PFlow.credentials.v1:" + binding.AccountKey)
        );
}
