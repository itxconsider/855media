using _855Media.Core.Licensing;
using _855Media.Framework;
using Xunit;

namespace _855Media.Core.Tests.Licensing;

public class ModuleAccessTests
{
    private sealed class Module : ViewModelBase
    {
        public bool CanStart() => CanUseSubscription();

        public void StartQueuedWork() => RequireSubscription();
    }

    [Fact]
    public void MissingOrUnactivatedLicenseCannotStartModuleWork()
    {
        using var module = new Module();
        Assert.False(module.CanStart());
        Assert.Throws<InvalidOperationException>(module.StartQueuedWork);
        module.ModuleLicense = new LicenseService();
        module.ModuleLicense.Initialize(null, DateTime.UtcNow, null);
        Assert.False(module.CanStart());
        Assert.Throws<InvalidOperationException>(module.StartQueuedWork);
    }

    [Fact]
    public void ModuleRechecksLicenseAfterDeactivation()
    {
        var keys = LicenseCrypto.GenerateKeyPair();
        var license = new LicenseService(
            TimeProvider.System,
            () => "TEST-PC",
            token => LicenseCrypto.VerifyToken(token, keys.PublicKeyBase64)
        );
        var token = LicenseCrypto.CreateSignedToken(
            new LicensePayload
            {
                LicenseKey = "module-access-test",
                MachineFingerprint = "TEST-PC",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                Type = LicenseType.Monthly,
                AllowedFeatures = ["All"],
            },
            keys.PrivateKeyBase64
        );
        Assert.True(license.ActivateWithToken(token).Success);
        using var module = new Module { ModuleLicense = license };
        Assert.True(module.CanStart());
        module.StartQueuedWork();
        license.Deactivate();
        Assert.False(module.CanStart());
        Assert.Throws<InvalidOperationException>(module.StartQueuedWork);
    }
}
