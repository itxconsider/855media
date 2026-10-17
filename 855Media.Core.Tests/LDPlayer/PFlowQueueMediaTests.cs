using System.IO;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowQueueMediaTests
{
    [Fact]
    public void RepeatedPreparationChoosesNextFileEvenWithoutGlobalUniqueness()
    {
        var first = Path.GetFullPath("queued-one.mp4");
        var next = Path.GetFullPath("queued-two.mp4");
        Assert.Equal(new[] { next }, PFlowQueueMedia.ExcludeReserved([first, next], [first]));
        Assert.Empty(PFlowQueueMedia.ExcludeReserved([first, next], [first, next]));
    }

    [Fact]
    public void NormalizedCaseInsensitivePathsAndDuplicateCandidatesAreReserved()
    {
        var first = Path.GetFullPath("queued-one.mp4");
        var alias = Path.Combine(Path.GetDirectoryName(first)!, ".", Path.GetFileName(first));
        Assert.Empty(PFlowQueueMedia.ExcludeReserved([alias], [first.ToUpperInvariant()]));
        Assert.Equal(new[] { first }, PFlowQueueMedia.ExcludeReserved([first, alias], []));
    }
}
