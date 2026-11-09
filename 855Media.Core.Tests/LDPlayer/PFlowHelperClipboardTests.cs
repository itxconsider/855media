using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowHelperClipboardTests
{
    private static (PFlowHelperClipboard Bridge, Device Fake) Create()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        return (new(ld, 43, "ld14", fake.Command, _ => Task.FromResult(fake.Installed)), fake);
    }

    [Fact]
    public async Task EmptyUpdateAcknowledgmentUsesFreshProviderProofAndRepeatedLinkReads()
    {
        var (bridge, fake) = Create();
        Assert.Null(await bridge.ReadAsync(default));
        Assert.Equal(0, fake.Starts);
        Assert.True(await bridge.PrepareAsync(default));
        Assert.Equal(1, fake.Starts);
        Assert.Equal(fake.Link, await bridge.ReadAsync(default));
        Assert.Equal(fake.Link, await bridge.ReadAsync(default));
        Assert.Equal(3, fake.Starts);
        Assert.Equal(0, fake.OtherActions);
    }

    [Theory]
    [InlineData("not-installed")]
    [InlineData("foreign")]
    [InlineData("input")]
    [InlineData("forged-arm")]
    public async Task UnsafeOrUnsupportedPreparationCannotStartClipboardActivity(string failure)
    {
        var (bridge, fake) = Create();
        fake.Installed = failure != "not-installed";
        fake.Failure = failure;
        Assert.False(await bridge.PrepareAsync(default));
        Assert.Equal(0, fake.Starts);
        Assert.Null(await bridge.ReadAsync(default));
    }

    [Theory]
    [InlineData("changed-pid")]
    [InlineData("foreign")]
    [InlineData("input")]
    public async Task ChangedFacebookContextCannotReadOrLaunchHelper(string failure)
    {
        var (bridge, fake) = Create();
        Assert.True(await bridge.PrepareAsync(default));
        fake.Failure = failure;
        Assert.Null(await bridge.ReadAsync(default));
        Assert.Equal(1, fake.Starts);
        fake.Failure = "";
        Assert.Null(await bridge.ReadAsync(default));
        Assert.Equal(1, fake.Starts);
    }

    [Fact]
    public async Task CancelledRequestPerformsNoCommands()
    {
        var (bridge, fake) = Create();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bridge.PrepareAsync(stop.Token)
        );
        Assert.Equal(0, fake.Starts);
        Assert.Equal(0, fake.Commands);
    }

    [Theory]
    [InlineData("wrong-request")]
    [InlineData("wrong-operation")]
    [InlineData("not-ready")]
    [InlineData("failed")]
    [InlineData("bad-link")]
    [InlineData("unsupported-protocol")]
    public void ResultsRequireFreshRequestSuccessProtocolAndSupportedFacebookUrl(string failure)
    {
        var raw =
            "Row: 0 json="
            + JsonSerializer.Serialize(
                new
                {
                    clipboardProtocolVersion = failure == "unsupported-protocol" ? 2 : 1,
                    request = failure == "wrong-request" ? "other" : "request",
                    operation = failure == "wrong-operation" ? "prepare" : "read",
                    ready = failure != "not-ready",
                    success = failure != "failed",
                    value = failure == "bad-link"
                        ? "secret text"
                        : "https://www.facebook.com/profile.php?id=123456789",
                }
            );
        Assert.Null(PFlowHelperClipboard.Parse(raw, "request", "read"));
    }

    public class Device : DispatchProxy
    {
        public bool Installed = true;
        public string Failure = "";
        public string Link = "https://www.facebook.com/profile.php?id=123456789";
        public int Starts,
            Commands,
            OtherActions;
        private bool Ready;
        private string Request = "",
            Operation = "";

        public Task<string> Command(string command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Commands++;
            if (command == "shell pidof com.facebook.katana")
                return Task.FromResult(Failure == "changed-pid" ? "456" : "123");
            if (command.StartsWith("shell content update"))
            {
                Request = Regex.Match(command, @"request:s:([a-f0-9]+)").Groups[1].Value;
                Operation = Regex.Match(command, @"operation:s:(prepare|read)").Groups[1].Value;
                Ready = false;
                return Task.FromResult(""); // Actual Android14 content update prints no count.
            }
            if (command.StartsWith("shell am start -W -n com.media885.helper/.ClipboardActivity"))
            {
                Starts++;
                Ready = true;
                return Task.FromResult("Status: ok");
            }
            if (command.StartsWith("shell content query"))
                return Task.FromResult(
                    "Row: 0 json="
                        + JsonSerializer.Serialize(
                            new
                            {
                                clipboardProtocolVersion = 1,
                                request = Failure == "forged-arm" ? "other" : Request,
                                operation = Operation,
                                ready = Ready,
                                success = Ready,
                                value = Operation == "read" ? Link : "",
                            }
                        )
                );
            OtherActions++;
            throw new Exception("Unexpected command");
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(
                        Failure == "foreign" ? "other.app/Main" : "com.facebook.katana/Main"
                    );
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(
                        "<hierarchy><node package='com.facebook.katana' text='Profile settings'/>"
                            + (
                                Failure == "input"
                                    ? "<node package='com.facebook.katana' class='android.widget.EditText'/>"
                                    : ""
                            )
                            + "</hierarchy>"
                    );
                default:
                    OtherActions++;
                    throw new Exception("Unexpected UI action: " + method.Name);
            }
        }
    }
}
