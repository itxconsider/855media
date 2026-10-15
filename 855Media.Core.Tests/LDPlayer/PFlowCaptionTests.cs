using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowCaptionTests
{
    [Fact]
    public void PreferEditableCaptionOverPlaceholder()
    {
        var node = PFlowAutoPostService.FindPFlowCaptionField(
            "<hierarchy><node text='Describe your reel'/><node class='android.widget.EditText' focused='true' bounds='[10,100][200,300]'/></hierarchy>"
        );
        Assert.Equal("android.widget.EditText", node?.Attribute("class")?.Value);
    }

    [Fact]
    public void DoNotGuessUnrelatedFocusableButton()
    {
        Assert.Null(
            PFlowAutoPostService.FindPFlowCaptionField(
                "<hierarchy><node text='Share now' focusable='true' bounds='[10,100][200,300]'/></hierarchy>"
            )
        );
    }

    [Theory]
    [InlineData("hello&#160;world", "hello world", true)]
    [InlineData("hello&#10;world", "hello world", true)]
    [InlineData("hello…", "hello world", false)]
    [InlineData("hello world extra", "hello world", false)]
    [InlineData("hello world", "different caption", false)]
    public void FullCaptionRequiredWithWhitespaceNormalization(
        string actual,
        string expected,
        bool matches
    )
    {
        Assert.Equal(
            matches,
            PFlowAutoPostService.CaptionTextMatches(
                $"<hierarchy><node text='{actual}'/></hierarchy>",
                expected
            )
        );
    }

    [Fact]
    public void NamedSwitcherDoesNotRequireFollowerCounter()
    {
        Assert.NotNull(
            PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(
                "<hierarchy><node text='Switch accounts' enabled='true' clickable='true' bounds='[600,100][690,190]'/></hierarchy>"
            )
        );
    }

    [Fact]
    public async System.Threading.Tasks.Task CaptionDryRunUsesBase64AfterKeyboardAndNeverShares()
    {
        var ld = System.Reflection.DispatchProxy.Create<ILDPlayerService, CaptionLd>();
        var fake = (CaptionLd)(object)ld;

        var job = new PFlowJob
        {
            State = PFlowState.IdentityVerified,
            TargetName = "Target",
            TargetId = "123",
            Caption = "Full caption #tag with emoji \U0001F3AC",
            Size = 1,
            Sha256 = "HASH",
            SourcePath = "test.mp4",
        };
        await new PFlowAutoPostService(ld).PublishAsync(
            job,
            _ => System.Threading.Tasks.Task.CompletedTask,
            null,
            System.Threading.CancellationToken.None,
            dryRun: true
        );
        Assert.Equal(PFlowState.CancelledBeforeSubmission, job.State);
        Assert.True(fake.KeyboardBeforeTap);
        Assert.True(fake.ClearBeforeInput);
        Assert.Equal(job.Caption, fake.Caption);
        Assert.Equal(1, fake.InputBroadcasts);
        Assert.Equal(0, fake.ShareTaps);
    }

    [Fact]
    public void MultiPageSwitcherWithoutCreatePageIsRecognized()
    {
        var xml =
            "<hierarchy><node content-desc='Dismiss'/><node content-desc='Page A' selected='true' clickable='true'/><node text='Owner'/><node text='Page'/><node text='Go to Accounts Center'/></hierarchy>";
        Assert.True(PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(xml));
        Assert.False(
            PFlowPersonaSwitcherService.IsNativeSwitcherSheetOpen(
                "<hierarchy><node text='Go to Accounts Center'/></hierarchy>"
            )
        );
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void DisabledPublishButtonIsNotReady(string enabled, bool ready)
    {
        Assert.Equal(
            ready,
            PFlowAutoPostService.HasEnabledPublishControl(
                $"<hierarchy><node text='Share now' clickable='true' enabled='{enabled}'/></hierarchy>",
                ["Share now"]
            )
        );
    }

    [Fact]
    public void StatusBarWindowDoesNotHideProfileDropdown()
    {
        var xml =
            "<hierarchy><node bounds='[0,0][720,48]'/><node bounds='[0,48][720,1280]'><node text='Carey Northcutt' bounds='[186,569][530,624]'/><node text='44 friends · 6 posts' bounds='[200,630][520,680]'/><node clickable='true' bounds='[632,564][696,628]'/></node></hierarchy>";
        Assert.Equal((720, 1280), PFlowPersonaSwitcherService.ParseScreenDimensions(xml));
        Assert.NotNull(PFlowPersonaSwitcherService.FindProfileHeaderSwitcherButton(xml));
    }

    [Theory]
    [InlineData("am broadcast -a ADB_INPUT_B64 --es msg ABC", true)]
    [InlineData("am broadcast -a ADB_CLEAR_TEXT", true)]
    [InlineData("dumpsys activity activities", false)]
    public void KeyboardBroadcastUsesRealAdbShell(string command, bool expected)
    {
        Assert.Equal(expected, LDPlayerService.IsKeyboardBroadcastCommand(command));
    }

    public class CaptionLd : System.Reflection.DispatchProxy
    {
        public bool KeyboardBeforeTap,
            ClearBeforeInput;
        public int InputBroadcasts;
        public string Caption = "";
        public int ShareTaps;
        private bool _chooserAccepted;
        private bool _keyboard,
            _composer;

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    if (_composer && !_chooserAccepted)
                        return Task.FromResult<string?>(
                            PFlowShareDestinationTests.SelectedReelsXml
                        );
                    return System.Threading.Tasks.Task.FromResult<string?>(
                        !_composer
                            ? "<hierarchy><node text='Target'/><node text='Professional dashboard'/></hierarchy>"
                            : "<hierarchy><node text='Target'/><node text='Describe your reel'/><node class='android.widget.EditText' focused='true' text='"
                                + System.Security.SecurityElement.Escape(Caption)
                                + "' bounds='[10,100][190,200]'/><node text='Share now' clickable='true' enabled='true' bounds='[0,0][100,100]'/></hierarchy>"
                    );
                case "EnableAdbKeyboardAsync":
                    _keyboard = true;
                    return System.Threading.Tasks.Task.FromResult(true);
                case "InputTextAdbKeyboardAsync":
                    throw new System.InvalidOperationException(
                        "P-Flow must use the direct ADB broadcast exactly once."
                    );
                case "TapAsync":
                    if (_composer && !_chooserAccepted)
                    {
                        _chooserAccepted = true;
                        return Task.FromResult(true);
                    }
                    if ((int)args![2]! == 50)
                        ShareTaps++;
                    else
                        KeyboardBeforeTap = _keyboard;
                    return System.Threading.Tasks.Task.FromResult(true);
                case "ShareVideoToFacebookAsync":
                    _composer = true;
                    return System.Threading.Tasks.Task.FromResult(true);
                case "ExecuteAdbAsync":
                    var cmd = (string)args![1]!;
                    if (cmd.Contains("ADB_CLEAR_TEXT"))
                    {
                        ClearBeforeInput = true;
                        return System.Threading.Tasks.Task.FromResult(
                            "Broadcast completed: result=0"
                        );
                    }
                    if (cmd.Contains("ADB_INPUT_B64"))
                    {
                        InputBroadcasts++;
                        Caption = System.Text.Encoding.UTF8.GetString(
                            System.Convert.FromBase64String(
                                cmd[
                                    (
                                        cmd.IndexOf("--es msg ", System.StringComparison.Ordinal)
                                        + 9
                                    )..
                                ]
                                    .Trim()
                                    .Trim('\'')
                            )
                        );
                        return System.Threading.Tasks.Task.FromResult(
                            "Broadcast completed: result=0"
                        );
                    }
                    return System.Threading.Tasks.Task.FromResult(
                        cmd.Contains("stat -c") ? "1"
                        : cmd.Contains("sha256sum") ? "HASH file"
                        : ""
                    );
            }
            if (method.ReturnType == typeof(System.Threading.Tasks.Task<bool>))
                return System.Threading.Tasks.Task.FromResult(true);
            if (method.ReturnType == typeof(System.Threading.Tasks.Task))
                return System.Threading.Tasks.Task.CompletedTask;
            throw new System.NotSupportedException(method.Name);
        }
    }
}
