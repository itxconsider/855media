using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowOwnerMenuRecoveryTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        61,
        "LD-61",
        "com.facebook.katana",
        "123"
    );
    private static string Profile =>
        FacebookLayoutRegistryTests.Fixture("ld61-tail-talks-profile-title", "PFlowScreenTime");

    private static string Menu()
    {
        var doc = XDocument.Parse(FacebookLayoutRegistryTests.Fixture("ld3-owner-shortcut"));
        foreach (var node in doc.Descendants("node"))
            node.SetAttributeValue("enabled", "true");
        return doc.ToString().Replace("Harch Jing", "Sue Li");
    }

    private static FacebookPageTarget Target() =>
        new()
        {
            AccountBinding = Binding,
            UserUid = "123",
            PageName = "Tail Talks+",
            Type = "Page",
        };

    [Theory]
    [InlineData("valid", true)]
    [InlineData("foreign", false)]
    [InlineData("duplicate", false)]
    [InlineData("pending", false)]
    [InlineData("profile", false)]
    public void CapturedPageOnlyPermitsRecoveryForUniqueBoundSavedPage(string mode, bool expected)
    {
        var row = Target();
        if (mode == "foreign")
            row.AccountBinding = Binding with { InstanceIdentity = "other" };
        if (mode == "pending")
            row.FetchDetailsPending = true;
        if (mode == "profile")
            row.Type = "Profile";
        Assert.Equal(
            expected,
            PFlowPostingNameVerification.CanRecoverOwnerFromPage(
                Profile,
                Binding,
                mode == "duplicate" ? [row, Target()] : [row]
            )
        );
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("disabled", false)]
    [InlineData("wrong-owner", false)]
    [InlineData("unchanged", false)]
    [InlineData("wrong-final", false)]
    public async Task MenuShortcutRequiresFreshExactOwnerAndFinalProof(string mode, bool expected)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var device = (Device)(object)ld;
        var menu = Menu();
        Assert.True(PFlowNativeAccountNavigator.MenuSurface(menu));
        Assert.NotNull(PFlowPostingNameVerification.PersonalShortcut(menu, "Sue Li"));
        if (mode == "disabled")
            menu = menu.Replace("enabled=\"true\"", "enabled=\"false\"");
        if (mode == "wrong-owner")
            menu = menu.Replace("Sue Li", "Other owner");
        var polls = 0;
        var result = await PFlowPostingNameVerification.RecoverOwnerThroughMenuAsync(
            ld,
            Binding,
            "Sue Li",
            null,
            _ =>
            {
                polls++;
                if (device.Taps == 0)
                    return Task.FromResult(Profile);
                var final = mode == "wrong-final" ? "Other owner" : "Sue Li";
                return Task.FromResult(
                    device.Taps < 2 || mode == "unchanged" || polls < 5
                        ? menu
                        : Menu().Replace("KC Waterheaters", final)
                );
            },
            (_, _) => Task.CompletedTask,
            default
        );
        Assert.Equal(expected, result);
        Assert.Equal(mode is "disabled" or "wrong-owner" ? 1 : 2, device.Taps);
        Assert.InRange(polls, 1, 123);
    }

    [Fact]
    public async Task CancellationStopsRecovery()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowPostingNameVerification.RecoverOwnerThroughMenuAsync(
                ld,
                Binding,
                "Sue Li",
                null,
                _ => Task.FromResult(((Device)(object)ld).Taps == 0 ? Profile : Menu()),
                (_, ct) =>
                {
                    cancel.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                cancel.Token
            )
        );
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("wrong-owner")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    public async Task ShortcutIsReboundAfterIdleAndCannotUseStaleControl(string mode)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var latest = Menu();
        var result = await PFlowPostingNameVerification.SelectOwnerShortcutAsync(
            ld,
            Binding,
            "Sue Li",
            null,
            _ => Task.FromResult(latest),
            (_, _) => Task.CompletedTask,
            default,
            _ =>
            {
                if (mode == "disabled")
                    latest = latest.Replace("enabled=\"true\"", "enabled=\"false\"");
                if (mode == "wrong-owner")
                    latest = latest.Replace("Sue Li", "Other owner");
                if (mode == "foreign")
                    latest = latest.Replace("com.facebook.katana", "other.app");
                if (mode == "duplicate")
                {
                    var doc = XDocument.Parse(latest);
                    var shortcut = PFlowPostingNameVerification.PersonalShortcut(latest, "Sue Li")!;
                    doc.Root!.Add(new XElement(shortcut));
                    latest = doc.ToString();
                }
                return Task.CompletedTask;
            }
        );
        Assert.False(result);
        Assert.Equal(0, ((Device)(object)ld).Taps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnerConfirmationRequiresTwoConsecutiveFreshFrames(bool transientOnly)
    {
        var reads = 0;
        var owner = Menu().Replace("KC Waterheaters", "Sue Li");
        var result = await PFlowPostingNameVerification.WaitForOwnerHeaderAsync(
            Binding,
            "Sue Li",
            _ =>
            {
                reads++;
                // One early owner frame must not complete recovery. A delayed stable
                // response appears after more than the old 500ms handoff.
                return Task.FromResult(reads == 1 || !transientOnly && reads >= 8 ? owner : Menu());
            },
            (_, _) => Task.CompletedTask,
            default
        );
        Assert.Equal(!transientOnly, result);
        Assert.Equal(transientOnly ? 60 : 9, reads);
    }

    [Fact]
    public async Task PageMembershipIsRecheckedAfterIdleBeforeMenuTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var latest = Profile;
        var result = await PFlowPostingNameVerification.RecoverOwnerThroughMenuAsync(
            ld,
            Binding,
            "Sue Li",
            null,
            _ => Task.FromResult(latest),
            (_, _) => Task.CompletedTask,
            default,
            _ =>
            {
                latest = Profile.Replace("Tail Talks+", "Other Page");
                return Task.CompletedTask;
            },
            fresh =>
                PFlowPostingNameVerification.CanRecoverOwnerFromPage(fresh, Binding, [Target()])
        );
        Assert.False(result);
        Assert.Equal(0, ((Device)(object)ld).Taps);
    }

    [Fact]
    public async Task OwnerAlreadyActiveAfterIdleIsVerifiedWithoutAnotherTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var reads = 0;
        Assert.True(
            await PFlowPostingNameVerification.SelectOwnerShortcutAsync(
                ld,
                Binding,
                "Sue Li",
                null,
                _ =>
                {
                    reads++;
                    return Task.FromResult(Menu().Replace("KC Waterheaters", "Sue Li"));
                },
                (_, _) => Task.CompletedTask,
                default
            )
        );
        Assert.Equal(3, reads);
        Assert.Equal(0, ((Device)(object)ld).Taps);
    }

    [Fact]
    public async Task FreshWorkerOrForegroundGuardFailureStopsBeforeTap()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PFlowPostingNameVerification.SelectOwnerShortcutAsync(
                ld,
                Binding,
                "Sue Li",
                null,
                _ => throw new InvalidOperationException("Device process changed"),
                (_, _) => Task.CompletedTask,
                default
            )
        );
        Assert.Equal(0, ((Device)(object)ld).Taps);
    }

    public class Device : DispatchProxy
    {
        public int Taps;

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method!.Name switch
            {
                "DumpBloksScreenAsync" => Task.FromResult<BloksScreenInfo?>(
                    AtxAgentClient.ParseBloksHierarchy(Profile)
                ),
                "TapAsync" => Tap(),
                _ => throw new Exception("Unexpected device call: " + method.Name),
            };

        private Task<bool> Tap()
        {
            Taps++;
            return Task.FromResult(true);
        }
    }
}
