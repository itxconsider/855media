using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace _855Media.Core.Licensing;

public class LicenseService : ILicenseService
{
    private static readonly HttpClient HttpClient = new(
        new HttpClientHandler { AllowAutoRedirect = false }
    )
    {
        Timeout = TimeSpan.FromSeconds(15),
    };
    private readonly TimeProvider _clock;
    private readonly Func<string> _fingerprint;
    private readonly Func<string, (bool IsValid, LicensePayload? Payload, string? Error)> _verify;
    private LicensePayload? _payload;
    private LicenseStatus _status = LicenseStatus.Unlicensed;
    private DateTime _lastSeenUtc;

    public LicenseService()
        : this(
            TimeProvider.System,
            HardwareIdProvider.GetMachineFingerprint,
            token => LicenseCrypto.VerifyToken(token)
        ) { }

    internal LicenseService(
        TimeProvider clock,
        Func<string> fingerprint,
        Func<string, (bool IsValid, LicensePayload? Payload, string? Error)> verify
    )
    {
        _clock = clock;
        _fingerprint = fingerprint;
        _verify = verify;
        _lastSeenUtc = UtcNow;
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    public LicenseStatus Status
    {
        get
        {
            Refresh();
            return _status;
        }
    }
    public LicensePayload? CurrentLicense => _payload is { } payload ? Copy(payload) : null;
    public string MachineFingerprint => _fingerprint();
    public int TrialDaysRemaining => 0;
    public bool IsTrialActive => false;
    public bool IsProActive => Status == LicenseStatus.Active;
    public event Action? LicenseChanged;

    public void Initialize(string? savedToken, DateTime? firstRunDate, DateTime? lastRunDate)
    {
        _payload = null;
        _lastSeenUtc = lastRunDate?.ToUniversalTime() ?? UtcNow;
        _status = LicenseStatus.Unlicensed;
        if (_lastSeenUtc - UtcNow > TimeSpan.FromHours(1))
            _status = LicenseStatus.Expired;
        else if (!string.IsNullOrWhiteSpace(savedToken))
        {
            var result = ValidateTokenInternal(savedToken);
            _status = result.Status;
            if (result.Success)
                _payload = Copy(result.Payload!);
        }
        LicenseChanged?.Invoke();
    }

    public void Refresh()
    {
        var now = UtcNow;
        var previous = _status;
        if (_lastSeenUtc - now > TimeSpan.FromHours(1))
            _status = LicenseStatus.Expired;
        else if (
            _status == LicenseStatus.Active
            && _payload?.ExpiresAt is { } end
            && now >= end.ToUniversalTime()
        )
            _status = LicenseStatus.Expired;
        if (now > _lastSeenUtc)
            _lastSeenUtc = now;
        if (previous != _status)
            LicenseChanged?.Invoke();
    }

    public bool IsFeatureAllowed(LicenseFeature feature)
    {
        Refresh();
        return Enum.IsDefined(feature)
            && _status == LicenseStatus.Active
            && _payload != null
            && (
                _payload.AllowedFeatures.Contains("All")
                || _payload.AllowedFeatures.Contains(feature.ToString())
            );
    }

    public LicenseActivationResult ActivateWithToken(string signedToken)
    {
        Refresh();
        if (_lastSeenUtc - UtcNow > TimeSpan.FromHours(1))
            return LicenseActivationResult.Failed(
                "Correct the computer clock before activation.",
                LicenseStatus.Expired
            );
        var result = ValidateTokenInternal(signedToken);
        if (result.Success)
        {
            _payload = Copy(result.Payload!);
            _status = LicenseStatus.Active;
            result.SignedToken = signedToken.Trim();
            LicenseChanged?.Invoke();
        }
        return result;
    }

    public async Task<LicenseActivationResult> ActivateWithKeyOnlineAsync(
        string licenseKey,
        string? apiUrl = null
    )
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            return LicenseActivationResult.Failed(
                "Please enter an activation key or signed license token."
            );
        var key = licenseKey.Trim();
        if (key.Contains('.'))
            return ActivateWithToken(key);
        if (
            !Uri.TryCreate(apiUrl, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
        )
            return LicenseActivationResult.Failed(
                "Online activation requires a configured HTTPS activation service. You can also import a signed .lic file."
            );
        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        license_key = key,
                        machine_id = MachineFingerprint,
                        product = "855Media",
                        version = "1.0",
                    }
                ),
                Encoding.UTF8,
                "application/json"
            );
            using var response = await HttpClient.PostAsync(endpoint, content);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (
                    doc.RootElement.TryGetProperty("token", out var element)
                    && element.GetString() is { Length: > 0 } token
                )
                    return ActivateWithToken(token);
            }
            return LicenseActivationResult.Failed(
                "Activation service did not return a valid signed license."
            );
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or TaskCanceledException
                        or JsonException
                        or InvalidOperationException
            )
        {
            return LicenseActivationResult.Failed(
                "Activation service could not be reached or returned an invalid response. Try again or import a signed .lic file."
            );
        }
    }

    public void Deactivate()
    {
        _payload = null;
        _status = LicenseStatus.Unlicensed;
        LicenseChanged?.Invoke();
    }

    private LicenseActivationResult ValidateTokenInternal(string token)
    {
        var (valid, payload, error) = _verify(token.Trim());
        if (!valid || payload == null)
            return LicenseActivationResult.Failed(error ?? "Invalid digital signature.");
        var now = UtcNow;
        if (
            !Enum.IsDefined(payload.Type)
            || payload.Type == LicenseType.Trial
            || string.IsNullOrWhiteSpace(payload.LicenseKey)
            || payload.AllowedFeatures == null
            || payload.AllowedFeatures.Count == 0
            || payload.IssuedAt.ToUniversalTime() > now.AddMinutes(5)
            || (payload.Type != LicenseType.Lifetime && !payload.ExpiresAt.HasValue)
            || (payload.ExpiresAt.HasValue && payload.ExpiresAt.Value <= payload.IssuedAt)
        )
            return LicenseActivationResult.Failed(
                "Invalid subscription details. A paid, signed license with valid dates and module access is required."
            );
        if (payload.ExpiresAt.HasValue && now >= payload.ExpiresAt.Value.ToUniversalTime())
            return LicenseActivationResult.Failed(
                "This subscription has expired.",
                LicenseStatus.Expired
            );
        if (
            string.IsNullOrWhiteSpace(payload.MachineFingerprint)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(payload.MachineFingerprint.ToUpperInvariant()),
                Encoding.UTF8.GetBytes(MachineFingerprint.ToUpperInvariant())
            )
        )
            return LicenseActivationResult.Failed(
                "This subscription is not bound to this computer.",
                LicenseStatus.HardwareMismatch
            );
        return LicenseActivationResult.Succeeded(Copy(payload));
    }

    private static LicensePayload Copy(LicensePayload payload) =>
        JsonSerializer.Deserialize<LicensePayload>(JsonSerializer.Serialize(payload))!;
}
