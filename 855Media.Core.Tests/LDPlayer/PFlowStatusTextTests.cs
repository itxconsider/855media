using System.Text.Json;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowStatusTextTests
{
    [Theory]
    [InlineData("Queued", "Queued")]
    [InlineData("Confirmed", "Published")]
    [InlineData("Confirmed (UI proof)", "Published")]
    [InlineData("Unknown: publication timed out", "Needs review · check publication")]
    [InlineData("Cancelled before submission (batch stopped)", "Cancelled")]
    [InlineData("Blocked: Page phone verification required", "Blocked · verify phone")]
    [InlineData("Skipped (No selected account tasks)", "Skipped · no account tasks")]
    [InlineData("Skipped (No verified target/file job)", "Skipped · no ready posts")]
    [InlineData("Skipped (Daily capacity)", "Skipped · daily limit")]
    [InlineData(
        "Error: Could not switch to saved account Tani Charada",
        "Failed · cannot switch account"
    )]
    [InlineData(
        "Failed: Selected Facebook name/type does not match",
        "Failed · account or Page not matched"
    )]
    [InlineData("P-Flow [Tail Talks+] Facebook copyright scan", "copyright check")]
    [InlineData(
        "P-Flow [Tail Talks+] Refreshing Home feed after upload / processing",
        "returning to Feed"
    )]
    [InlineData("Failed: caption entry not ready", "Failed · caption not ready")]
    [InlineData("Failed: upload not confirmed", "Failed · upload needs attention")]
    [InlineData("All done", "Finished")]
    public void DisplayDoesNotChangeMeaning(string raw, string expected) =>
        Assert.Equal(expected, PFlowStatusText.Summary(raw));

    [Fact]
    public void NewMessagesAreBoundedAndRawEvidenceSurvives()
    {
        var row = new AutoPostTaskItem
        {
            DeviceName = "LD-20",
            Page = "Fixture Page",
            Title = "Caption",
            FilePath = "media.mp4",
            Status = new string('x', 400),
        };
        Assert.True(row.StatusSummary.Length <= 76);
        Assert.Contains(row.Status, row.StatusDetails);
        Assert.Contains("Device: LD-20", row.StatusDetails);
        Assert.Contains("Target: Fixture Page", row.StatusDetails);
        Assert.Contains("Media: media.mp4", row.StatusDetails);
        Assert.Contains("Caption: Caption", row.StatusDetails);
        Assert.Equal(new string('x', 400), row.Status);
        Assert.DoesNotContain("StatusSummary", JsonSerializer.Serialize(row));
        Assert.DoesNotContain("StatusDetails", JsonSerializer.Serialize(row));
    }

    [Fact]
    public void TrailIsBoundedNewestFirstAndNotifiedWithRawStatus()
    {
        var row = new AutoPostTaskItem();
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        for (var i = 0; i < 25; i++)
            row.Status = "Fixture step " + i + "|";
        Assert.DoesNotContain("Fixture step 4|", row.StatusDetails);
        Assert.Contains("Fixture step 5|", row.StatusDetails);
        Assert.True(
            row.StatusDetails.IndexOf("Fixture step 24|", StringComparison.Ordinal)
                < row.StatusDetails.IndexOf("Fixture step 5|", StringComparison.Ordinal)
        );
        Assert.Equal(25, changes.Count(n => n == nameof(row.StatusDetails)));
        var count = changes.Count;
        row.Status = row.Status;
        Assert.Equal(count, changes.Count);
    }
}
