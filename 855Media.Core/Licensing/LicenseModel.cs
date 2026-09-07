using System;
using System.Collections.Generic;

namespace _855Media.Core.Licensing;

public enum LicenseStatus
{
    Unlicensed,
    Trial,
    Active,
    Expired,
    Revoked,
    HardwareMismatch,
}

public enum LicenseType
{
    Trial,
    Lifetime,
    Annual,
    Monthly,
}

public enum LicenseFeature
{
    All,
    BasicDownloads,
    BulkFacebookScraper,
    BatchDownloads,
    AudioProcessing,
    HighParallelDownloads,
}

public class LicensePayload
{
    public string LicenseKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public LicenseType Type { get; set; } = LicenseType.Lifetime;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public List<string> AllowedFeatures { get; set; } = [];

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
}

public class LicenseActivationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public LicenseStatus Status { get; set; }
    public LicensePayload? Payload { get; set; }

    public static LicenseActivationResult Succeeded(
        LicensePayload payload,
        string message = "Activation successful"
    ) =>
        new()
        {
            Success = true,
            Status = LicenseStatus.Active,
            Payload = payload,
            Message = message,
        };

    public static LicenseActivationResult Failed(
        string message,
        LicenseStatus status = LicenseStatus.Unlicensed
    ) =>
        new()
        {
            Success = false,
            Status = status,
            Message = message,
        };
}
