using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowStartupIdentityTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "instance",
        21,
        "LD-21",
        "com.facebook.katana",
        "123"
    );
    private const string Xml =
        "<hierarchy><node package='com.facebook.katana' text='Home'/></hierarchy>";
    private static readonly PFlowGraphIdentity Identity = new("456", "Page", "123", null);

    [Fact]
    public void MatchingImmediateStartupHandoffIsSingleUse()
    {
        var now = DateTime.UtcNow;
        var proof = new PFlowStartupIdentity(Binding, "700", Xml, Identity, now);
        Assert.Equal(Identity, proof.Consume(Binding, "700", Xml, now.AddSeconds(1)));
        Assert.Null(proof.Consume(Binding, "700", Xml, now.AddSeconds(2)));
    }

    [Theory]
    [InlineData("process")]
    [InlineData("binding")]
    [InlineData("xml")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("missing")]
    public void ChangedOrOldEvidenceCannotSupplyStartupIdentity(string change)
    {
        var now = DateTime.UtcNow;
        var proof = new PFlowStartupIdentity(Binding, "700", Xml, Identity, now);
        Assert.Null(
            proof.Consume(
                change == "binding" ? Binding with { OwnerUid = "999" } : Binding,
                change == "process" ? "701" : "700",
                change == "missing" ? null
                    : change == "xml" ? Xml.Replace("Home", "Menu")
                    : Xml,
                change == "expired" ? now.AddSeconds(16)
                    : change == "future" ? now.AddSeconds(-1)
                    : now
            )
        );
        Assert.Null(proof.Consume(Binding, "700", Xml, now));
    }

    [Theory]
    [InlineData("", "<hierarchy/>")]
    [InlineData("error", Xml)]
    [InlineData("700", "<hierarchy><node class='android.widget.EditText'/></hierarchy>")]
    public void InvalidCaptureOrProcessDoesNotCreateReusableEvidence(string process, string xml)
    {
        var now = DateTime.UtcNow;
        var proof = new PFlowStartupIdentity(Binding, process, xml, Identity, now);
        Assert.Null(proof.Consume(Binding, process, xml, now));
    }
}
