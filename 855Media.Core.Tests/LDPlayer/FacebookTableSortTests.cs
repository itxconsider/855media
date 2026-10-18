using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookTableSortTests
{
    [Theory]
    [InlineData("29/08/26", "02/10/26")]
    [InlineData("September 29, 2026", "October 2, 2026")]
    [InlineData("2025-12-31", "2026-01-01")]
    [InlineData("Created August 2026", "September 2026")]
    public void DatesSortByYearMonthThenDay(string older, string newer) =>
        Assert.True(
            FacebookTableSort.CreationDateKey(older) < FacebookTableSort.CreationDateKey(newer)
        );

    [Fact]
    public void FileCountsSortNumericallyWithTotalAsTieBreaker()
    {
        Assert.True(
            FacebookTableSort.MediaCountKey(52, 54) < FacebookTableSort.MediaCountKey(500, 500)
        );
        Assert.True(
            FacebookTableSort.MediaCountKey(55, 55) < FacebookTableSort.MediaCountKey(55, 57)
        );
        Assert.True(
            FacebookTableSort.MediaCountKey(2, 500) < FacebookTableSort.MediaCountKey(10, 10)
        );
        Assert.Equal(-1, FacebookTableSort.CreationDateKey("—"));
        Assert.Equal(-1, FacebookTableSort.MediaCountKey(-1, 0));
    }
}
