using _855Media.Core.Editing;
using Xunit;

namespace _855Media.Core.Tests.Editing;

public class EditorThreadPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(20, 4)]
    [InlineData(64, 4)]
    public void BoundsBothSimpleAndComplexFilterPools(int cores, int expected)
    {
        Assert.Equal(expected, EditorThreadPolicy.FilterThreads(cores));
        Assert.Equal(
            new[]
            {
                "-filter_threads",
                expected.ToString(),
                "-filter_complex_threads",
                expected.ToString(),
            },
            EditorThreadPolicy.FilterArguments(cores)
        );
    }
}
