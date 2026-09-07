using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace _855Media.Core.Licensing;

public class LicenseService : ILicenseService
{
    private const int TrialDaysDuration = 7;
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public LicenseStatus Status { get; private set; } = LicenseStatus.Unlicensed;
    public LicensePayload? CurrentLicense { get; private set; }
    public string MachineFingerprint => HardwareIdProvider.GetMachineFingerprint();
    public int TrialDaysRemaining { get; private set; }
    public bool IsTrialActive => Status == LicenseStatus.Trial;
    public bool IsProActive => Status == LicenseStatus.Active;

    public event Action? LicenseChanged;

    public void Initialize(string? savedToken, DateTime? firstRunDate, DateTime? lastRunDate)
    {
        // 1. First priority: Check if an activated cryptographic token or serial key is saved
        if (!string.IsNullOrWhiteSpace(savedToken))
        {
            if (LicenseCrypto.VerifyShortSerialKey(savedToken))
            {
                Status = LicenseStatus.Active;
                CurrentLicense = new LicensePayload
                {
                    LicenseKey = savedToken,
                    CustomerName = "Licensed User",
                    CustomerEmail = "licensed@855media.com",
                    MachineFingerprint = MachineFingerprint,
                    Type = LicenseType.Lifetime,
                    IssuedAt = DateTime.UtcNow,
                    ExpiresAt = null,
                    AllowedFeatures = ["All"],
                };
                LicenseChanged?.Invoke();
                return;
            }

            var result = ValidateTokenInternal(savedToken);
            if (result.Success && result.Payload is not null)
            {
                Status = LicenseStatus.Active;
                CurrentLicense = result.Payload;
                LicenseChanged?.Invoke();
                return;
            }
            if (result.Status == LicenseStatus.HardwareMismatch)
            {
                Status = LicenseStatus.HardwareMismatch;
                LicenseChanged?.Invoke();
                return;
            }
        }

        // 2. Second priority: Free Trial Period
        var now = DateTime.UtcNow;

        // Anti-tampering clock detection: if current time is before the last known execution time
        if (lastRunDate.HasValue && now < lastRunDate.Value.AddHours(-1))
        {
            Status = LicenseStatus.Expired;
            TrialDaysRemaining = 0;
            LicenseChanged?.Invoke();
            return;
        }

        var trialStart = firstRunDate ?? now;
        var trialEnd = trialStart.AddDays(TrialDaysDuration);

        if (now < trialEnd)
        {
            Status = LicenseStatus.Trial;
            TrialDaysRemaining = Math.Max(1, (int)Math.Ceiling((trialEnd - now).TotalDays));
        }
        else
        {
            Status = LicenseStatus.Expired;
            TrialDaysRemaining = 0;
        }

        LicenseChanged?.Invoke();
    }

    public bool IsFeatureAllowed(LicenseFeature feature)
    {
        // During active trial or full license, all features are enabled
        if (Status == LicenseStatus.Active || Status == LicenseStatus.Trial)
            return true;

        // Basic downloading is always free even if trial expires (freemium model)
        if (feature == LicenseFeature.BasicDownloads)
            return true;

        // Pro features require an active license or trial
        return false;
    }

    public LicenseActivationResult ActivateWithToken(string signedToken)
    {
        var result = ValidateTokenInternal(signedToken);
        if (result.Success && result.Payload is not null)
        {
            Status = LicenseStatus.Active;
            CurrentLicense = result.Payload;
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
            return LicenseActivationResult.Failed("Please enter a license key.");

        var key = licenseKey.Trim();

        // 1. If user pasted the full signed token directly, activate immediately
        if (key.Contains('.'))
        {
            return ActivateWithToken(key);
        }

        // 2. Offline short serial key verification (e.g. 855M-XXXX-XXXX-XXXX)
        if (LicenseCrypto.VerifyShortSerialKey(key))
        {
            var payload = new LicensePayload
            {
                LicenseKey = key,
                CustomerName = "Licensed User",
                CustomerEmail = "licensed@855media.com",
                MachineFingerprint = MachineFingerprint,
                Type = LicenseType.Lifetime,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = null,
                AllowedFeatures = ["All"],
            };

            Status = LicenseStatus.Active;
            CurrentLicense = payload;
            LicenseChanged?.Invoke();
            return LicenseActivationResult.Succeeded(payload, "Serial key activated successfully!");
        }

        // 3. Fallback online check if API URL is specifically configured
        if (!string.IsNullOrWhiteSpace(apiUrl))
        {
            try
            {
                var requestBody = JsonSerializer.Serialize(
                    new
                    {
                        license_key = key,
                        machine_id = MachineFingerprint,
                        product = "855Media",
                        version = "1.0",
                    }
                );

                using var content = new StringContent(
                    requestBody,
                    Encoding.UTF8,
                    "application/json"
                );
                using var response = await HttpClient.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    if (doc.RootElement.TryGetProperty("token", out var tokenElement))
                    {
                        var token = tokenElement.GetString();
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            return ActivateWithToken(token);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return LicenseActivationResult.Failed(
                    $"Connection error: {ex.Message}. Please check your key or activate offline."
                );
            }
        }

        return LicenseActivationResult.Failed(
            "Invalid license key or token. Please verify the code or select your .lic file."
        );
    }

    public void Deactivate()
    {
        CurrentLicense = null;
        Status = LicenseStatus.Unlicensed;
        LicenseChanged?.Invoke();
    }

    private LicenseActivationResult ValidateTokenInternal(string token)
    {
        var (isValid, payload, error) = LicenseCrypto.VerifyToken(token);

        if (!isValid || payload is null)
            return LicenseActivationResult.Failed(error ?? "Invalid digital signature.");

        if (payload.IsExpired)
            return LicenseActivationResult.Failed(
                "This license has expired.",
                LicenseStatus.Expired
            );

        // Check machine fingerprint binding (if license was locked to a specific device)
        if (
            !string.IsNullOrWhiteSpace(payload.MachineFingerprint)
            && !string.Equals(
                payload.MachineFingerprint,
                MachineFingerprint,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return LicenseActivationResult.Failed(
                $"This license is locked to another computer (Device ID mismatch). Your Device ID: {MachineFingerprint}",
                LicenseStatus.HardwareMismatch
            );
        }

        return LicenseActivationResult.Succeeded(payload);
    }
}
