using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowFileSelectionTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "855-file-selection-" + Guid.NewGuid().ToString("N")
    );

    private string Media(string name, int day)
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, name);
        File.WriteAllText(file, "fixture");
        File.SetLastWriteTimeUtc(file, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
        return file;
    }

    [Theory]
    [InlineData(1, false, "alpha.mp4,beta.mp4")]
    [InlineData(1, true, "zeta.mp4,beta.mp4")]
    [InlineData(2, false, "beta.mp4,zeta.mp4")]
    [InlineData(2, true, "alpha.mp4,zeta.mp4")]
    public void ExactCountUsesRequestedNameOrModifiedDateOrder(
        int mode,
        bool descending,
        string expected
    )
    {
        var files = new[] { Media("zeta.mp4", 2), Media("alpha.mp4", 3), Media("beta.mp4", 1) };
        Assert.Equal(
            expected.Split(','),
            PFlowFileSelection.Select(files, 2, mode, descending).Select(Path.GetFileName)
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomUsesExactCountInsteadOfDrawingACountRange(int count)
    {
        var files = Enumerable.Range(0, 10).Select(i => "file" + i + ".mp4").ToArray();
        for (var seed = 0; seed < 10; seed++)
        {
            var selected = PFlowFileSelection.Select(files, count, 0, false, new Random(seed));
            Assert.Equal(count, selected.Count);
            Assert.Equal(count, selected.Distinct().Count());
            Assert.All(selected, p => Assert.Contains(p, files));
        }
    }

    [Fact]
    public void AvailabilityAndCountBoundsDoNotCreateExtraRows()
    {
        Assert.Empty(PFlowFileSelection.Select([], 3, 1, false));
        Assert.Single(PFlowFileSelection.Select(["only.mp4"], 5, 1, false));
        Assert.Single(PFlowFileSelection.Select(["a", "b"], 0, 1, false));
        Assert.Equal(
            5,
            PFlowFileSelection
                .Select(Enumerable.Range(0, 10).Select(i => i.ToString()), 20, 1, false)
                .Count
        );
    }

    [Fact]
    public void EqualDatesHaveStableNameTiesInEitherDirection()
    {
        var files = new[] { Media("b.mp4", 2), Media("A.mp4", 2) };
        Assert.Equal(files.Reverse(), PFlowFileSelection.Order(files, 2, false));
        Assert.Equal(files.Reverse(), PFlowFileSelection.Order(files, 2, true));
    }

    [Theory]
    [InlineData(1, false, "Alpha")]
    [InlineData(1, true, "Zulu")]
    [InlineData(2, false, "Zulu")]
    [InlineData(2, true, "Alpha")]
    public void SeriesOrderNeverReversesNumericPartOrder(int mode, bool descending, string first)
    {
        var files = new[]
        {
            Media("Alpha Part 2.mp4", 1),
            Media("Zulu Part 2.mp4", 3),
            Media("Zulu Part 1.mp4", 1),
            Media("Alpha Part 1.mp4", 3),
        };
        var plan = PFlowPartGroups.Plan(
            files,
            files,
            [],
            [],
            [],
            false,
            1,
            2,
            10,
            orderBy: mode,
            descending: descending
        );
        Assert.Equal(4, plan.Files.Count);
        Assert.StartsWith(first, Path.GetFileName(plan.Files[0].Path));
        Assert.Equal(new[] { 1, 2, 1, 2 }, plan.Files.Select(p => p.Number));
    }

    [Fact]
    public void PhotoGroupingStillCountsGroupsInsteadOfIndividualImages()
    {
        var a = Media("a.jpg", 2);
        var b = Media("b.jpg", 1);
        var c = Media("c.jpg", 3);
        File.WriteAllText(Path.ChangeExtension(a, ".txt"), "Same caption");
        File.WriteAllText(Path.ChangeExtension(b, ".txt"), "Same caption");
        File.WriteAllText(Path.ChangeExtension(c, ".txt"), "Other caption");
        var groups = PFlowPhotoGroups.Group([a, b, c], true).ToDictionary(g => g[0]);
        var chosen = PFlowFileSelection.Select(groups.Keys, 1, 1, false);
        Assert.Equal(new[] { a, b }, groups[Assert.Single(chosen)]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }
}
