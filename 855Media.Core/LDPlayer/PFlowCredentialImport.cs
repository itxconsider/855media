using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace _855Media.Core.LDPlayer;

public enum PFlowTwoFactorKind
{
    None,
    TotpSecret,
    OneTimeCode,
}

public sealed class PFlowCredential : IDisposable
{
    [JsonIgnore]
    public bool IsDisposed { get; private set; }

    [JsonIgnore]
    public byte[] LoginIdentifier { get; }

    [JsonIgnore]
    public byte[] Password { get; }

    [JsonIgnore]
    public byte[] TwoFactor { get; }

    [JsonIgnore]
    public PFlowTwoFactorKind TwoFactorKind { get; }

    [JsonIgnore]
    public DateTimeOffset? OneTimeExpiresUtc { get; }

    internal PFlowCredential(
        byte[] login,
        byte[] password,
        byte[] twoFactor,
        PFlowTwoFactorKind kind
    )
    {
        LoginIdentifier = login;
        Password = password;
        TwoFactor = twoFactor;
        TwoFactorKind = kind;
        if (kind == PFlowTwoFactorKind.OneTimeCode)
            OneTimeExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(60);
    }

    public void Dispose()
    {
        IsDisposed = true;
        CryptographicOperations.ZeroMemory(LoginIdentifier);
        CryptographicOperations.ZeroMemory(Password);
        CryptographicOperations.ZeroMemory(TwoFactor);
    }

    public override string ToString() => "Protected credential (values hidden)";
}

public static class PFlowCredentialImport
{
    public static string FormatForEditor(PFlowAccountBinding binding, PFlowCredential credential)
    {
        binding.Validate();
        if (credential.IsDisposed || credential.TwoFactorKind == PFlowTwoFactorKind.OneTimeCode)
            throw new InvalidOperationException("Reusable stored credentials are unavailable.");
        return binding.OwnerUid
            + "|"
            + Encoding.UTF8.GetString(credential.Password)
            + "|"
            + Encoding.UTF8.GetString(credential.TwoFactor)
            + "|";
    }

    public static PFlowCredential Parse(
        string line,
        PFlowAccountBinding binding,
        string loginIdentifier,
        PFlowTwoFactorKind kind
    )
    {
        binding.Validate();
        if (
            !Enum.IsDefined(kind)
            || string.IsNullOrWhiteSpace(loginIdentifier)
            || Encoding.UTF8.GetByteCount(loginIdentifier) > 1024
            || loginIdentifier.Contains('\n')
            || loginIdentifier.Contains('\r')
            || line.Length > 65536
        )
            throw new FormatException(
                "Choose a supported login identifier and explicit 2FA field type."
            );
        // Strict four-field format. No guessing/shifted cookies when a password contains '|'.
        var fields = line.Split('|');
        if (fields.Length != 4)
            throw new FormatException(
                "Expected UID|Password|2FA|Cookie with exactly four fields. Embedded pipes are unsupported."
            );
        if (fields[0].Trim() != binding.OwnerUid)
            throw new FormatException(
                "Imported UID does not match the explicitly assigned account."
            );
        if (
            fields[1].Length == 0
            || Encoding.UTF8.GetByteCount(fields[1]) > 65536
            || fields[1].Contains('\n')
            || fields[1].Contains('\r')
        )
            throw new FormatException(
                "Password is missing, too long, or contains an unsupported line break."
            );
        if (!string.IsNullOrWhiteSpace(fields[3]))
            throw new NotSupportedException(
                "Cookie import is unavailable until a supported installed-app session route is verified. Leave Cookie empty."
            );
        var factor = fields[2].Trim();
        if (kind == PFlowTwoFactorKind.None && factor.Length != 0)
            throw new FormatException(
                "Select TOTP secret or one-time code explicitly for the 2FA field."
            );
        if (
            kind == PFlowTwoFactorKind.OneTimeCode
            && (factor.Length is not (6 or 8) || !factor.All(char.IsAsciiDigit))
        )
            throw new FormatException("A one-time code must contain 6 or 8 digits.");
        if (kind == PFlowTwoFactorKind.TotpSecret)
        {
            var key = PFlowTotp.DecodeBase32(factor);
            CryptographicOperations.ZeroMemory(key);
        }
        return new(
            Encoding.UTF8.GetBytes(loginIdentifier.Trim()),
            Encoding.UTF8.GetBytes(fields[1]),
            Encoding.UTF8.GetBytes(factor),
            kind
        );
    }
}

public static class PFlowTotp
{
    public static string Generate(string secret, DateTimeOffset utc, int digits = 6)
    {
        if (digits is not (6 or 8) || utc.ToUnixTimeSeconds() < 0)
            throw new ArgumentOutOfRangeException(nameof(digits));
        var key = DecodeBase32(secret);
        byte[]? hash = null;
        try
        {
            Span<byte> counter = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(
                counter,
                utc.ToUnixTimeSeconds() / 30
            );
            hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 15;
            var binary =
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4))
                & 0x7fffffff;
            return (binary % (digits == 6 ? 1000000U : 100000000U)).ToString(
                digits == 6 ? "D6" : "D8",
                System.Globalization.CultureInfo.InvariantCulture
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (hash != null)
                CryptographicOperations.ZeroMemory(hash);
        }
    }

    internal static byte[] DecodeBase32(string secret)
    {
        var normalized = new string(secret.Where(c => !char.IsWhiteSpace(c)).ToArray())
            .TrimEnd('=')
            .ToUpperInvariant();
        if (normalized.Length < 16 || normalized.Length > 256)
            throw new FormatException(
                "TOTP secret must be a valid Base32 key, not a one-time code."
            );
        var bytes = new byte[normalized.Length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var count = 0;
        foreach (var c in normalized)
        {
            var value = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            if (value < 0)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new FormatException("TOTP secret contains invalid Base32 characters.");
            }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[count++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }
        if (buffer != 0 || bits >= 5)
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw new FormatException("TOTP Base32 key has invalid trailing bits.");
        }
        return bytes;
    }
}
