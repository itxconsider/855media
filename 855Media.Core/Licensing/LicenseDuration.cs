using System;

namespace _855Media.Core.Licensing;

public static class LicenseDuration
{
    public const int MonthlyDays = 30;
    public const int AnnualDays = 365;

    public static DateTime? ExpiryFromIssue(LicenseType type, DateTime issuedAt) =>
        type switch
        {
            LicenseType.Monthly => issuedAt.AddDays(MonthlyDays),
            LicenseType.Annual => issuedAt.AddDays(AnnualDays),
            LicenseType.Lifetime => null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                "A paid subscription type is required."
            ),
        };

    public static bool TryParsePlan(string value, out LicenseType type)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "monthly":
            case "30":
                type = LicenseType.Monthly;
                return true;
            case "annual":
            case "yearly":
            case "365":
                type = LicenseType.Annual;
                return true;
            case "lifetime":
            case "0":
                type = LicenseType.Lifetime;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
