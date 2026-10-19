using _855Media.Core.Licensing;
using Xunit;

namespace _855Media.Core.Tests.Licensing;

public class LicenseServiceTests
{
    private static readonly (string PublicKeyBase64, string PrivateKeyBase64) Keys =
        LicenseCrypto.GenerateKeyPair();

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static LicensePayload Payload(Clock clock) =>
        new()
        {
            LicenseKey = "test-subscription",
            MachineFingerprint = "TEST-PC",
            Type = LicenseType.Monthly,
            IssuedAt = clock.Now.UtcDateTime,
            ExpiresAt = clock.Now.AddDays(30).UtcDateTime,
            AllowedFeatures = ["All"],
        };

    private static LicenseService Service(Clock clock) =>
        new(
            clock,
            () => "TEST-PC",
            token => LicenseCrypto.VerifyToken(token, Keys.PublicKeyBase64)
        );

    private static string Sign(LicensePayload payload) =>
        LicenseCrypto.CreateSignedToken(payload, Keys.PrivateKeyBase64);

    [Fact]
    public void FreshInstallHasNoFreeTrialOrFreeModules()
    {
        var clock = new Clock();
        var service = Service(clock);
        service.Initialize(null, clock.Now.UtcDateTime, null);
        Assert.False(service.IsTrialActive);
        foreach (var feature in Enum.GetValues<LicenseFeature>())
            Assert.False(service.IsFeatureAllowed(feature));
    }

    [Fact]
    public void OldMinimumLastRunDateDoesNotCrashActivation()
    {
        var clock = new Clock();
        var service = Service(clock);
        service.Initialize(Sign(Payload(clock)), null, DateTime.MinValue);
        Assert.True(service.IsProActive);
        Assert.False(service.IsFeatureAllowed((LicenseFeature)999));
    }

    [Fact]
    public void SignedMachineBoundSubscriptionUnlocksEveryModuleAndPersistsToken()
    {
        var clock = new Clock();
        var service = Service(clock);
        var token = Sign(Payload(clock));
        var result = service.ActivateWithToken(token);
        Assert.True(result.Success);
        Assert.Equal(token, result.SignedToken);
        foreach (var feature in Enum.GetValues<LicenseFeature>())
            Assert.True(service.IsFeatureAllowed(feature));
        service.Initialize(result.SignedToken, null, null);
        Assert.True(service.IsProActive);
    }

    [Fact]
    public void ExpiryLocksModulesWithoutRestartAndRaisesOneChange()
    {
        var clock = new Clock();
        var service = Service(clock);
        Assert.True(service.ActivateWithToken(Sign(Payload(clock))).Success);
        var events = 0;
        service.LicenseChanged += () => events++;
        clock.Now = clock.Now.AddDays(30);
        Assert.False(service.IsFeatureAllowed(LicenseFeature.BasicDownloads));
        Assert.Equal(LicenseStatus.Expired, service.Status);
        service.Refresh();
        Assert.Equal(1, events);
    }

    [Theory]
    [InlineData("wrong-device")]
    [InlineData("unbound")]
    [InlineData("expired")]
    [InlineData("missing-expiry")]
    [InlineData("trial")]
    [InlineData("future-issue")]
    [InlineData("no-features")]
    [InlineData("bad-dates")]
    public void SignedButInvalidSubscriptionsCannotActivate(string scenario)
    {
        var clock = new Clock();
        var payload = Payload(clock);
        switch (scenario)
        {
            case "wrong-device":
                payload.MachineFingerprint = "OTHER-PC";
                break;
            case "unbound":
                payload.MachineFingerprint = "";
                break;
            case "expired":
                payload.IssuedAt = clock.Now.AddDays(-40).UtcDateTime;
                payload.ExpiresAt = clock.Now.AddDays(-1).UtcDateTime;
                break;
            case "missing-expiry":
                payload.ExpiresAt = null;
                break;
            case "trial":
                payload.Type = LicenseType.Trial;
                break;
            case "future-issue":
                payload.IssuedAt = clock.Now.AddDays(1).UtcDateTime;
                break;
            case "no-features":
                payload.AllowedFeatures = [];
                break;
            case "bad-dates":
                payload.ExpiresAt = payload.IssuedAt;
                break;
        }
        var service = Service(clock);
        Assert.False(service.ActivateWithToken(Sign(payload)).Success);
        Assert.False(service.IsFeatureAllowed(LicenseFeature.All));
    }

    [Fact]
    public void PayloadCopiesCannotExtendOrEscalateAccess()
    {
        var clock = new Clock();
        var payload = Payload(clock);
        payload.AllowedFeatures = ["AutoPost"];
        var service = Service(clock);
        var result = service.ActivateWithToken(Sign(payload));
        result.Payload!.AllowedFeatures.Add("All");
        service.CurrentLicense!.AllowedFeatures.Add("All");
        service.CurrentLicense!.ExpiresAt = null;
        Assert.True(service.IsFeatureAllowed(LicenseFeature.AutoPost));
        Assert.False(service.IsFeatureAllowed(LicenseFeature.All));
        clock.Now = clock.Now.AddDays(30);
        Assert.False(service.IsFeatureAllowed(LicenseFeature.AutoPost));
    }

    [Fact]
    public void ClockRollbackCannotReactivateExpiredAccess()
    {
        var clock = new Clock();
        var service = Service(clock);
        var token = Sign(Payload(clock));
        service.Initialize(token, null, clock.Now.AddDays(1).UtcDateTime);
        Assert.Equal(LicenseStatus.Expired, service.Status);
        Assert.False(service.ActivateWithToken(token).Success);
        service.Initialize(token, null, null);
        Assert.True(service.IsProActive);
        clock.Now = clock.Now.AddHours(-2);
        Assert.False(service.IsFeatureAllowed(LicenseFeature.All));
    }

    [Fact]
    public void LegacySerialAndTamperedTokenCannotGrantAccess()
    {
        var clock = new Clock();
        var service = Service(clock);
        service.Initialize(LicenseCrypto.GenerateShortSerialKey(), null, null);
        Assert.False(service.IsProActive);
        var token = Sign(Payload(clock));
        var invalid = token[..^4] + "AAAA";
        Assert.False(service.ActivateWithToken(invalid).Success);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://example.test/activate")]
    [InlineData("https://user:password@example.test/activate")]
    public async Task InsecureActivationEndpointsAreRejectedWithoutSendingKey(string? endpoint)
    {
        var service = Service(new Clock());
        var result = await service.ActivateWithKeyOnlineAsync("test-key", endpoint);
        Assert.False(result.Success);
        Assert.Contains("HTTPS", result.Message);
    }

    [Fact]
    public void SignedLifetimeAndDeactivationWorkWithoutTrialFallback()
    {
        var clock = new Clock();
        var payload = Payload(clock);
        payload.Type = LicenseType.Lifetime;
        payload.ExpiresAt = null;
        var service = Service(clock);
        Assert.True(service.ActivateWithToken(Sign(payload)).Success);
        clock.Now = clock.Now.AddYears(10);
        Assert.True(service.IsProActive);
        service.Deactivate();
        Assert.False(service.IsFeatureAllowed(LicenseFeature.All));
        Assert.False(service.IsTrialActive);
    }
}
