using _855Media.Core.Licensing;
using Xunit;

namespace _855Media.Core.Tests.Licensing;

public class LicenseDurationTests
{
    [Theory]
    [InlineData(2026, 10, 18)]
    [InlineData(2028, 2, 1)]
    [InlineData(2026, 1, 31)]
    public void MonthlyIsThirtyDaysAcrossCalendarBoundaries(int year, int month, int day)
    {
        var issued = new DateTime(year, month, day, 14, 52, 52, DateTimeKind.Utc);
        Assert.Equal(
            issued.AddDays(30),
            LicenseDuration.ExpiryFromIssue(LicenseType.Monthly, issued)
        );
    }

    [Theory]
    [InlineData(2028)]
    [InlineData(2026)]
    public void AnnualIsExactly365DaysIncludingLeapYears(int year)
    {
        var issued = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(
            issued.AddDays(365),
            LicenseDuration.ExpiryFromIssue(LicenseType.Annual, issued)
        );
    }

    [Theory]
    [InlineData("monthly", LicenseType.Monthly)]
    [InlineData("30", LicenseType.Monthly)]
    [InlineData("annual", LicenseType.Annual)]
    [InlineData("365", LicenseType.Annual)]
    [InlineData("lifetime", LicenseType.Lifetime)]
    [InlineData("0", LicenseType.Lifetime)]
    public void NamedAndNumericPlansAgree(string input, LicenseType expected)
    {
        Assert.True(LicenseDuration.TryParsePlan(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("31")]
    [InlineData("366")]
    [InlineData("-1")]
    [InlineData("invalid")]
    public void InvalidTermsDoNotSilentlyCreateLifetimeLicense(string input) =>
        Assert.False(LicenseDuration.TryParsePlan(input, out _));

    [Fact]
    public void LifetimeHasNoExpiry() =>
        Assert.Null(LicenseDuration.ExpiryFromIssue(LicenseType.Lifetime, DateTime.UtcNow));
}
