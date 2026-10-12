using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowCredentialTests : IDisposable
{
    static readonly PFlowAccountBinding Binding = new(
        "fixture",
        1,
        "LD-1",
        "com.facebook.katana",
        "123"
    );
    readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "pflow-secrets-fixture-" + Guid.NewGuid().ToString("N")
    );
    const string Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    public void Dispose()
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void TotpMatchesRfc6238Sha1Vectors(long time, string expected) =>
        Assert.Equal(
            expected,
            PFlowTotp.Generate(Secret, DateTimeOffset.FromUnixTimeSeconds(time), 8)
        );

    [Fact]
    public void DefaultTotpUsesSixDigits() =>
        Assert.Equal("287082", PFlowTotp.Generate(Secret, DateTimeOffset.FromUnixTimeSeconds(59)));

    [Theory]
    [InlineData("123456")]
    [InlineData("THIS!ISNOTBASE32")]
    [InlineData("AAAAAAAAAAAAAAAAA")]
    public void RejectsInvalidSecretWithoutExposingIt(string secret)
    {
        var ex = Assert.Throws<FormatException>(() =>
            PFlowTotp.Generate(secret, DateTimeOffset.UtcNow)
        );
        Assert.DoesNotContain(secret, ex.Message);
    }

    [Fact]
    public void SecretsCannotLeakThroughDefaultJsonOrToStringAndAreClearedOnDispose()
    {
        var credential = PFlowCredentialImport.Parse(
            "123|fixture-password||",
            Binding,
            "fixture@example.invalid",
            PFlowTwoFactorKind.None
        );
        Assert.Equal("{}", JsonSerializer.Serialize(credential));
        Assert.DoesNotContain("fixture-password", credential.ToString());
        credential.Dispose();
        Assert.All(credential.Password, b => Assert.Equal(0, b));
        Assert.All(credential.LoginIdentifier, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("123|password", PFlowTwoFactorKind.None)]
    [InlineData("123|pass|word||", PFlowTwoFactorKind.None)]
    [InlineData("456|password||", PFlowTwoFactorKind.None)]
    [InlineData("123||123456|", PFlowTwoFactorKind.OneTimeCode)]
    [InlineData("123|password|123456|", PFlowTwoFactorKind.None)]
    [InlineData("123|password|123456|", PFlowTwoFactorKind.TotpSecret)]
    public void AmbiguousOrWrongMappingInputIsRejected(string line, PFlowTwoFactorKind kind) =>
        Assert.Throws<FormatException>(() =>
            PFlowCredentialImport.Parse(line, Binding, "explicit-login", kind)
        );

    [Fact]
    public void CookieRouteIsExplicitlyUnsupported() =>
        Assert.Throws<NotSupportedException>(() =>
            PFlowCredentialImport.Parse(
                "123|fixture-password||c_user=123;",
                Binding,
                "explicit-login",
                PFlowTwoFactorKind.None
            )
        );

    [Fact]
    public void LoginIdentifierIsRequiredRatherThanInventedFromUid() =>
        Assert.Throws<FormatException>(() =>
            PFlowCredentialImport.Parse(
                "123|fixture-password||",
                Binding,
                "",
                PFlowTwoFactorKind.None
            )
        );

    [Fact]
    public async Task OneTimeCodeExpiresAndCannotBePersisted()
    {
        using var credential = PFlowCredentialImport.Parse(
            "123|fixture-password|123456|",
            Binding,
            "explicit-login",
            PFlowTwoFactorKind.OneTimeCode
        );
        Assert.InRange(
            credential.OneTimeExpiresUtc!.Value,
            DateTimeOffset.UtcNow.AddSeconds(50),
            DateTimeOffset.UtcNow.AddSeconds(61)
        );
        if (OperatingSystem.IsWindows())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PFlowCredentialStore(directory).SaveAsync(Binding, credential)
            );
    }

    [Fact]
    public async Task DpapiRoundTripIsBoundToAccountAndNoPlaintextIsStored()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var store = new PFlowCredentialStore(directory);
        using var credential = PFlowCredentialImport.Parse(
            "123|fixture-password|" + Secret + "|",
            Binding,
            "fixture@example.invalid",
            PFlowTwoFactorKind.TotpSecret
        );
        var reference = await store.SaveAsync(Binding, credential);
        Assert.True(Guid.TryParseExact(reference, "N", out _));
        var stored = await File.ReadAllBytesAsync(Path.Combine(directory, reference + ".bin"));
        Assert.DoesNotContain("fixture-password", Encoding.UTF8.GetString(stored));
        Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(stored));
        using var restored = await new PFlowCredentialStore(directory).LoadAsync(
            Binding,
            reference
        );
        Assert.Equal(credential.Password, restored.Password);
        Assert.Equal(credential.TwoFactor, restored.TwoFactor);
        Assert.Equal(PFlowTwoFactorKind.TotpSecret, restored.TwoFactorKind);
        await Assert.ThrowsAsync<CryptographicException>(() =>
            store.LoadAsync(Binding with { DeviceIndex = 2, DeviceName = "LD-2" }, reference)
        );
        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(Binding, "../outside"));
        store.Delete(reference);
        Assert.False(File.Exists(Path.Combine(directory, reference + ".bin")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1048577)]
    public async Task EmptyOrOversizedEncryptedFileIsRejectedBeforeDpapi(int size)
    {
        if (!OperatingSystem.IsWindows())
            return;
        Directory.CreateDirectory(directory);
        var reference = Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(Path.Combine(directory, reference + ".bin"), new byte[size]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PFlowCredentialStore(directory).LoadAsync(Binding, reference)
        );
    }

    [Theory]
    [InlineData(PFlowTwoFactorKind.None, "unexpected-factor")]
    [InlineData(PFlowTwoFactorKind.TotpSecret, "bad")]
    public async Task InvalidProtectedFieldsCannotBeSaved(PFlowTwoFactorKind kind, string factor)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var credential = new PFlowCredential(
            Encoding.UTF8.GetBytes("login"),
            Encoding.UTF8.GetBytes("password"),
            Encoding.UTF8.GetBytes(factor),
            kind
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowCredentialStore(directory).SaveAsync(Binding, credential)
        );
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task DisposedCredentialCannotBeSaved()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var credential = PFlowCredentialImport.Parse(
            "123|fixture-password||",
            Binding,
            "explicit-login",
            PFlowTwoFactorKind.None
        );
        credential.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PFlowCredentialStore(directory).SaveAsync(Binding, credential)
        );
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void OversizedUtf8PasswordIsRejectedBeforeProtection()
    {
        Assert.Throws<FormatException>(() =>
            PFlowCredentialImport.Parse(
                "123|" + new string('界', 30000) + "||",
                Binding,
                "explicit-login",
                PFlowTwoFactorKind.None
            )
        );
    }

    [Fact]
    public async Task CancelledImportDoesNotWriteCredentialFile()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var credential = PFlowCredentialImport.Parse(
            "123|fixture-password||",
            Binding,
            "explicit-login",
            PFlowTwoFactorKind.None
        );
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PFlowCredentialStore(directory).SaveAsync(Binding, credential, ct.Token)
        );
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData(PFlowTwoFactorKind.None)]
    [InlineData(PFlowTwoFactorKind.TotpSecret)]
    public void EditorRestoresFullReusableValuesAndCanSaveThemAgain(PFlowTwoFactorKind kind)
    {
        var factor = kind == PFlowTwoFactorKind.TotpSecret ? Secret : "";
        using var original = PFlowCredentialImport.Parse(
            "123| fixture-密碼 |" + factor + "|",
            Binding,
            "fixture@example.invalid",
            kind
        );
        var line = PFlowCredentialImport.FormatForEditor(Binding, original);
        Assert.Equal("123| fixture-密碼 |" + factor + "|", line);
        using var restored = PFlowCredentialImport.Parse(
            line,
            Binding,
            Encoding.UTF8.GetString(original.LoginIdentifier),
            kind
        );
        Assert.Equal(original.Password, restored.Password);
        Assert.Equal(original.LoginIdentifier, restored.LoginIdentifier);
        Assert.Equal(original.TwoFactor, restored.TwoFactor);
    }

    [Fact]
    public void ExpiringOrDisposedCredentialsCannotBeReloadedIntoEditor()
    {
        using var expiring = PFlowCredentialImport.Parse(
            "123|fixture-password|123456|",
            Binding,
            "fixture-login",
            PFlowTwoFactorKind.OneTimeCode
        );
        Assert.Throws<InvalidOperationException>(() =>
            PFlowCredentialImport.FormatForEditor(Binding, expiring)
        );
        using var disposed = PFlowCredentialImport.Parse(
            "123|fixture-password||",
            Binding,
            "fixture-login",
            PFlowTwoFactorKind.None
        );
        disposed.Dispose();
        Assert.Throws<InvalidOperationException>(() =>
            PFlowCredentialImport.FormatForEditor(Binding, disposed)
        );
    }

    [Fact]
    public async Task RecreatedInstanceRemapRetainsRealDpapiCredentialsWithoutReentry()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var credentials = new PFlowCredentialStore(directory);
        var accounts = new PFlowAccountStore(Path.Combine(directory, "accounts.json"), credentials);
        using var original = PFlowCredentialImport.Parse(
            "123|fixture-password|" + Secret + "|",
            Binding,
            "fixture@example.invalid",
            PFlowTwoFactorKind.TotpSecret
        );
        var reference = await credentials.SaveAsync(Binding, original);
        await accounts.UpdateAsync(a =>
            a.Add(
                new PFlowPersonalAccount
                {
                    Binding = Binding,
                    CredentialReference = reference,
                    AllowAutomaticLogin = true,
                }
            )
        );
        var replacement = Binding with
        {
            InstanceIdentity = "recreated-instance",
            DeviceName = "Recreated LD-1",
        };
        Assert.Equal(reference, await accounts.RemapAsync(Binding, replacement));
        var account = (await accounts.LoadAsync()).Single();
        Assert.NotEqual(reference, account.CredentialReference);
        Assert.True(account.AllowAutomaticLogin);
        Assert.Null(account.LastVerifiedUtc);
        using var restored = await credentials.LoadAsync(replacement, account.CredentialReference);
        Assert.Equal(original.Password, restored.Password);
        Assert.Equal(original.TwoFactor, restored.TwoFactor);
        await Assert.ThrowsAsync<CryptographicException>(() =>
            credentials.LoadAsync(
                replacement with
                {
                    OwnerUid = "456",
                },
                account.CredentialReference
            )
        );
        credentials.Delete(reference);
        using var afterCleanup = await credentials.LoadAsync(
            replacement,
            account.CredentialReference
        );
        Assert.Equal(original.LoginIdentifier, afterCleanup.LoginIdentifier);
    }
}
