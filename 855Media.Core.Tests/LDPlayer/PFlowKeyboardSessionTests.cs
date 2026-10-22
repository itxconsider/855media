using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public sealed class PFlowKeyboardSessionTests
{
    private const string Normal = "com.android.inputmethod.latin/.LatinIME";
    private const string Other = "com.example.keyboard/.InputService";
    private const string Adb = AdbKeyboardConstants.ImeComponent;

    private static (ILDPlayerService Ld, Device Device) Fake()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishAndStopRestorePreviousImeBeforeStoppingOnlyAdbKeyboard(bool cancelled)
    {
        var (ld, device) = Fake();
        await using var keyboard = new PFlowKeyboardSession(ld, 4, "fixture-path");
        using var run = new CancellationTokenSource();
        await keyboard.CaptureAsync(run.Token);
        device.Current = Adb;
        if (cancelled)
            run.Cancel();
        Assert.True(await keyboard.RestoreAsync());
        Assert.Equal(Normal, device.Current);
        var set = device.Commands.IndexOf("shell ime set " + Normal);
        var stop = device.Commands.IndexOf(
            "shell am force-stop " + AdbKeyboardConstants.PackageName
        );
        Assert.True(set >= 0 && stop > set);
        Assert.Contains(
            "shell settings get secure default_input_method",
            device.Commands.Skip(set + 1).Take(stop - set - 1)
        );
        Assert.DoesNotContain(
            device.Commands,
            s => s.Contains("clear") || s.Contains("uninstall") || s.Contains("kill-server")
        );
        Assert.True(await keyboard.RestoreAsync());
        Assert.Single(device.Commands, s => s.StartsWith("shell am force-stop"));
    }

    [Fact]
    public async Task DisposalOnExceptionAlsoRestoresKeyboard()
    {
        var (ld, device) = Fake();
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
            await keyboard.CaptureAsync(default);
            device.Current = Adb;
            throw new IOException("fixture operation failed");
        });
        Assert.Equal(Normal, device.Current);
        Assert.Contains("shell am force-stop " + AdbKeyboardConstants.PackageName, device.Commands);
    }

    [Fact]
    public async Task UserKeyboardChangeIsPreserved()
    {
        var (ld, device) = Fake();
        await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
        await keyboard.CaptureAsync(default);
        device.Current = Other;
        Assert.True(await keyboard.RestoreAsync());
        Assert.Equal(Other, device.Current);
        Assert.DoesNotContain(
            device.Commands,
            s => s.StartsWith("shell ime set") || s.Contains("force-stop")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlreadyAdbAtStartUsesInstalledNormalFallback(bool disabled)
    {
        var (ld, device) = Fake();
        device.Current = Adb;
        device.Enabled = disabled ? Adb : Adb + "\n" + Normal;
        await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
        await keyboard.CaptureAsync(default);
        Assert.True(await keyboard.RestoreAsync());
        Assert.Equal(Normal, device.Current);
        Assert.Equal(disabled, device.Commands.Contains("shell ime enable " + Normal));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("rejected")]
    [InlineData("offline")]
    public async Task FailedReplacementNeverStopsTheOnlyActiveKeyboard(string failure)
    {
        var (ld, device) = Fake();
        await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
        await keyboard.CaptureAsync(default);
        device.Current = Adb;
        if (failure == "missing")
            device.Enabled = device.Installed = Adb;
        if (failure == "rejected")
            device.IgnoreSet = true;
        if (failure == "offline")
            device.Offline = true;
        Assert.False(await keyboard.RestoreAsync());
        Assert.DoesNotContain(device.Commands, s => s.Contains("force-stop"));
    }

    [Fact]
    public async Task UnstartedWorkDoesNotTouchDevice()
    {
        var (ld, device) = Fake();
        await using (var keyboard = new PFlowKeyboardSession(ld, 4, null)) { }
        Assert.Empty(device.Commands);
    }

    [Fact]
    public async Task UntrustedImeOutputIsNeverExecuted()
    {
        var (ld, device) = Fake();
        device.Current = Adb;
        device.Enabled = device.Installed =
            "evil.package/.Ime;touch /tmp/file\ncom.bad/$SHELL\n" + Adb;
        await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
        await keyboard.CaptureAsync(default);
        Assert.False(await keyboard.RestoreAsync());
        Assert.DoesNotContain(
            device.Commands,
            s => s.StartsWith("shell ime set") || s.StartsWith("shell ime enable")
        );
    }

    [Fact]
    public async Task ExpandedComponentNamesRestoreTheSamePreviousKeyboard()
    {
        var (ld, device) = Fake();
        device.ExpandComponent = true;
        device.Enabled = Other + "\n" + Normal + "\n" + Adb;
        await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
        await keyboard.CaptureAsync(default);
        device.Current = Adb;
        Assert.True(await keyboard.RestoreAsync());
        Assert.Equal(Normal, device.Current);
        Assert.Contains("shell am force-stop " + AdbKeyboardConstants.PackageName, device.Commands);
    }

    [Fact]
    public async Task UserChangeDuringCleanupIsNotOverwritten()
    {
        var (ld, device) = Fake();
        await using var keyboard = new PFlowKeyboardSession(ld, 4, null);
        await keyboard.CaptureAsync(default);
        device.Current = Adb;
        device.SwitchDuringList = true;
        Assert.True(await keyboard.RestoreAsync());
        Assert.Equal(Other, device.Current);
        Assert.DoesNotContain(
            device.Commands,
            s => s.StartsWith("shell ime set") || s.Contains("force-stop")
        );
    }

    public class Device : DispatchProxy
    {
        public string Current = Normal,
            Enabled = Normal + "\n" + Adb,
            Installed = Normal + "\n" + Adb;
        public bool IgnoreSet,
            Offline,
            ExpandComponent,
            SwitchDuringList;
        public List<string> Commands = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name != nameof(ILDPlayerService.ExecuteAdbAsync))
                throw new NotSupportedException(method.Name);
            ((CancellationToken)args![3]!).ThrowIfCancellationRequested();
            var command = (string)args[1]!;
            Commands.Add(command);
            if (Offline)
                throw new IOException("device unavailable");
            if (command == "shell settings get secure default_input_method")
                return Task.FromResult(
                    ExpandComponent
                        ? Current.Replace("/.", "/" + Current.Split('/')[0] + ".")
                        : Current
                );
            if (command == "shell ime list -s")
                return Task.FromResult(Enabled);
            if (command == "shell ime list -a -s")
            {
                if (SwitchDuringList)
                    Current = Other;
                return Task.FromResult(Installed);
            }
            if (command.StartsWith("shell ime set ") && !IgnoreSet)
                Current = command[14..];
            return Task.FromResult("Success");
        }
    }
}
