using System;
using System.Threading.Tasks;

namespace _855Media.Core.Licensing;

public interface ILicenseService
{
    LicenseStatus Status { get; }
    LicensePayload? CurrentLicense { get; }
    string MachineFingerprint { get; }
    int TrialDaysRemaining { get; }
    bool IsTrialActive { get; }
    bool IsProActive { get; }

    event Action? LicenseChanged;

    void Initialize(string? savedToken, DateTime? firstRunDate, DateTime? lastRunDate);
    bool IsFeatureAllowed(LicenseFeature feature);
    LicenseActivationResult ActivateWithToken(string signedToken);
    Task<LicenseActivationResult> ActivateWithKeyOnlineAsync(
        string licenseKey,
        string? apiUrl = null
    );
    void Deactivate();
}
