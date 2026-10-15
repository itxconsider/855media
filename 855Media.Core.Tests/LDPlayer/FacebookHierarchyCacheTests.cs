using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class FacebookHierarchyCacheTests
{
    [Fact]
    public void NestedReadersShareOneTreeWithinRecognitionButNotAcrossCaptures()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("analytics-summary");
        XDocument first;
        using (FacebookHierarchyCache.Enter())
        {
            first = FacebookHierarchyCache.ParseStrict(xml);
            Assert.Same(first, PFlowAutoPostService.SafeParseXmlDocuments(xml).Single());
            Assert.Same(ScreenTimeHierarchy.Nodes(xml), ScreenTimeHierarchy.Nodes(xml));
            Assert.Same(
                AtxAgentClient.ParseBloksHierarchy(xml),
                AtxAgentClient.ParseBloksHierarchy(xml)
            );
            using (FacebookHierarchyCache.Enter())
                Assert.Same(first, FacebookHierarchyCache.ParseStrict(xml));
            Assert.True(FacebookHierarchyCache.Active);
        }
        Assert.False(FacebookHierarchyCache.Active);
        using (FacebookHierarchyCache.Enter())
            Assert.NotSame(first, FacebookHierarchyCache.ParseStrict(xml));
    }

    [Theory]
    [InlineData("<hierarchy><node text='a'/></hierarchy><hierarchy><node text='b'/></hierarchy>")]
    [InlineData("adb prefix\n<hierarchy><node text='a'/></hierarchy>\ntrailer")]
    [InlineData("<?xml version='1.0'?>\n<hierarchy><node text='a'/></hierarchy>\n")]
    [InlineData("<hierarchy><hierarchy><node text='nested'/></hierarchy></hierarchy>")]
    [InlineData("<hierarchy><!-- </hierarchy> --><node text='comment'/></hierarchy>")]
    [InlineData("<!-- prefix --><hierarchy><node text='a'/></hierarchy>")]
    [InlineData("<broken")]
    public void PermissiveExtractionKeepsTheUncachedBehavior(string xml)
    {
        var expected = PFlowAutoPostService
            .SafeParseXmlDocuments(xml)
            .SelectMany(d => d.Descendants("node"))
            .Select(n => n.ToString())
            .ToArray();
        using (FacebookHierarchyCache.Enter())
        {
            Assert.Equal(
                expected,
                ScreenTimeHierarchy.Nodes(xml).Select(n => n.ToString()).ToArray()
            );
            Assert.Equal(
                expected,
                ScreenTimeHierarchy.Nodes(xml).Select(n => n.ToString()).ToArray()
            );
        }
    }

    [Fact]
    public void ParsedCaptureCanBeReusedButChangedRawXmlCannot()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(
            FacebookLayoutRegistryTests.Fixture("analytics-summary")
        );
        Assert.True(FacebookHierarchyCache.CanReuse(screen));
        Assert.Same(screen, FacebookLayoutObservation.FromScreen(screen).Screen);
        Assert.True(
            FacebookLayoutRegistry.MatchScreen(screen, FacebookScreenKind.Analytics).IsMatch
        );
        screen.RawXml = FacebookLayoutRegistryTests.Fixture("feed-home");
        Assert.False(FacebookHierarchyCache.CanReuse(screen));
        Assert.NotSame(screen, FacebookLayoutObservation.FromScreen(screen).Screen);
        Assert.False(
            FacebookLayoutRegistry.MatchScreen(screen, FacebookScreenKind.Analytics).IsMatch
        );
    }

    [Fact]
    public void ContextAndObservationIdentityAreNotReusedAcrossCaptures()
    {
        var xml = FacebookLayoutRegistryTests.Fixture("own-profile");
        FacebookLayoutMatch first;
        using (FacebookHierarchyCache.Enter())
        {
            first = FacebookLayoutRegistry.Match(
                xml,
                FacebookScreenKind.OwnProfile,
                new("W500 PH")
            );
            Assert.True(first.IsMatch);
            Assert.Same(
                first,
                FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile, new("W500 PH"))
            );
            Assert.False(
                FacebookLayoutRegistry
                    .Match(xml, FacebookScreenKind.OwnProfile, new("Other Page"))
                    .IsMatch
            );
        }
        var next = FacebookLayoutRegistry.Match(xml, FacebookScreenKind.OwnProfile, new("W500 PH"));
        Assert.True(next.IsMatch);
        Assert.NotEqual(first.ObservationId, next.ObservationId);
    }

    [Fact]
    public void DiagnosticRedactionDoesNotMutateSharedInputNodes()
    {
        const string xml =
            "<hierarchy><node class='android.widget.EditText' text='sample draft' content-desc='Password'/></hierarchy>";
        using (FacebookHierarchyCache.Enter())
        {
            var node = ScreenTimeHierarchy.Nodes(xml).Single();
            var redacted = PFlowUiPrivacy.RedactForDiagnostics(xml);
            Assert.Equal(
                "",
                XDocument.Parse(redacted).Descendants("node").Single().Attribute("text")!.Value
            );
            Assert.Equal("sample draft", node.Attribute("text")!.Value);
            Assert.Same(node, ScreenTimeHierarchy.Nodes(xml).Single());
        }
    }

    [Fact]
    public void ParallelRecognitionScopesCannotReadEachOthersMemoizedResults()
    {
        Parallel.For(
            0,
            24,
            index =>
            {
                using var scope = FacebookHierarchyCache.Enter();
                var calls = 0;
                var first = FacebookHierarchyCache.Memo(
                    "same XML",
                    "same key",
                    () =>
                    {
                        calls++;
                        return index;
                    }
                );
                var second = FacebookHierarchyCache.Memo("same XML", "same key", () => -1);
                Assert.Equal(index, first);
                Assert.Equal(index, second);
                Assert.Equal(1, calls);
            }
        );
    }

    [Fact]
    public void ExceptionDisposesTheRecognitionScope()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var scope = FacebookHierarchyCache.Enter();
            FacebookHierarchyCache.Memo<int>(
                "XML",
                "failed",
                () => throw new InvalidOperationException()
            );
        });
        Assert.False(FacebookHierarchyCache.Active);
    }

    [Fact]
    public void ConcurrentReadersOfOneObservationKeepTheSameLayoutAndControls()
    {
        var observation = new FacebookLayoutObservation(
            FacebookLayoutRegistryTests.Fixture("own-profile"),
            new("W500 PH")
        );
        Parallel.For(
            0,
            16,
            _ =>
            {
                var match = FacebookLayoutRegistry.Match(
                    observation,
                    FacebookScreenKind.OwnProfile
                );
                Assert.True(match.IsMatch, match.Diagnostic);
                Assert.Equal(observation.Id, match.ObservationId);
                Assert.NotNull(match.Control(FacebookControlRole.Dashboard));
            }
        );
        Assert.False(FacebookHierarchyCache.Active);
    }
}
