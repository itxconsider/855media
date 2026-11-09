using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowZeroGroupTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ThreeStandaloneFilesExcludePartsAndReservations(int mode)
    {
        var root = Path.GetTempPath();
        var standalone = Enumerable
            .Range(1, 4)
            .Select(i => Path.Combine(root, $"single-{i}.mp4"))
            .ToArray();
        var candidates = standalone.Concat([
            Path.Combine(root, "Story Part 1.mp4"),
            Path.Combine(root, "Story Part 2.mp4"),
            Path.Combine(root, "Part 0 ambiguous.mp4"),
        ]);
        var eligible = PFlowPartGroups.StandaloneFiles(candidates, [standalone[3]]).ToArray();
        var result = PFlowFileSelection.Select(eligible, 3, mode, false);
        Assert.Equal(3, result.Count);
        Assert.All(result, path => Assert.Contains(path, standalone.Take(3)));
    }
}
