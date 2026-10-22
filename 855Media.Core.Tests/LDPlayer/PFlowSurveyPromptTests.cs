using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowSurveyPromptTests
{
    // Reconstructed from the screenshot and observed LD-56 close bounds.
    private const string Banner =
        """<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'><node package='com.facebook.katana' bounds='[0,144][720,576]'><node package='com.facebook.katana' text='' clickable='true' enabled='true' bounds='[656,168][688,216]'/><node package='com.facebook.katana' text="Marc, we'd like to improve Facebook for everyone" bounds='[32,260][688,360]'/><node package='com.facebook.katana' text='Could you take a few minutes to answer a short survey?' bounds='[32,376][688,450]'/><node package='com.facebook.katana' content-desc='Start Survey' clickable='true' enabled='true' bounds='[32,474][688,546]'/></node></node></hierarchy>""";

    [Fact]
    public void OnlyChoosesClose() =>
        Assert.Equal((672, 192), PFlowSurveyPrompt.DismissControl(Banner));

    [Theory]
    [InlineData("Start Survey", "Something else")]
    [InlineData("short survey?", "different question?")]
    [InlineData("improve Facebook for everyone", "do something else")]
    [InlineData("enabled='true'", "enabled='false'")]
    [InlineData("[656,168][688,216]", "[32,474][688,546]")]
    [InlineData("com.facebook.katana", "other.app")]
    public void RejectsUnsafeOrUnrelated(string from, string to) =>
        Assert.Null(PFlowSurveyPrompt.DismissControl(Banner.Replace(from, to)));

    [Fact]
    public void RejectsInput() =>
        Assert.Null(
            PFlowSurveyPrompt.DismissControl(
                Banner.Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' class='android.widget.EditText' text='draft'/></hierarchy>"
                )
            )
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneCloseTapAndBoundedConfirmation(bool stalled)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        device.Xml = stalled
            ? Banner
            : "<hierarchy><node package='com.facebook.katana' text='Home'/></hierarchy>";
        if (stalled)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PFlowNavigationPrompts.DismissAsync(
                    ld,
                    56,
                    Banner,
                    null,
                    default,
                    (_, _) => Task.CompletedTask
                )
            );
        else
            Assert.Equal(
                device.Xml,
                await PFlowNavigationPrompts.DismissAsync(
                    ld,
                    56,
                    Banner,
                    null,
                    default,
                    (_, _) => Task.CompletedTask
                )
            );
        Assert.Equal(1, device.Taps);
    }

    [Fact]
    public async Task CancellationDoesNotTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowSurveyPrompt.DismissAsync(
                ld,
                56,
                Banner,
                null,
                cts.Token,
                (_, _) => Task.CompletedTask
            )
        );
        Assert.Equal(0, ((Device)(object)ld).Taps);
    }

    public class Device : DispatchProxy
    {
        public string Xml = "";
        public int Taps;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TapAsync")
            {
                Assert.Equal(672, args![1]);
                Assert.Equal(192, args[2]);
                Taps++;
                return Task.FromResult(true);
            }
            if (method.Name == "DumpUiHierarchyAsync")
                return Task.FromResult<string?>(Xml);
            throw new NotSupportedException(method.Name);
        }
    }
}
