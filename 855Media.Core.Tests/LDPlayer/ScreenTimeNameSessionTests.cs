using System.Reflection;
using System.Security;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class ScreenTimeNameSessionTests
{
    private static readonly PFlowAccountBinding Binding = new(
        "fixture",
        900756,
        "fixture-LD",
        "com.facebook.katana",
        "100000000000001"
    );

    private static FacebookPageTarget Personal() =>
        new()
        {
            PageName = "Personal Owner",
            Type = "Profile",
            PageId = Binding.OwnerUid,
            UserUid = Binding.OwnerUid,
            AccountBinding = Binding,
        };

    private static (ILDPlayerService Ld, Device Fake) Harness()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        return (ld, (Device)(object)ld);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public async Task PersonalAccountWithAnyPageCountReusesNameSessionWithoutGraph(int pageCount)
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        var saved = new[] { personal }
            .Concat(
                Enumerable
                    .Range(0, pageCount)
                    .Select(i => new FacebookPageTarget
                    {
                        PageName = "Page " + i,
                        PageId = "20000000000000" + i,
                        Type = "Page",
                        UserUid = Binding.OwnerUid,
                        AccountBinding = Binding,
                    })
            )
            .ToArray();
        var session = new ScreenTimeNameSession(ld, Binding, "PersonalOwner", personal, null);
        for (var i = 0; i < 10; i++)
            Assert.True(await session.VerifyAsync(personal, saved, default));
        Assert.Equal(0, fake.Switches);
        Assert.Equal(0, fake.ForbiddenReads);
    }

    [Fact]
    public async Task ActivePageIsSwitchedToSavedPersonalActorWithoutGraph()
    {
        var (ld, fake) = Harness();
        fake.Xml = File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld55-switcher-selected-page-captured.xml"
                )
            )
            .Replace("Scarlet Marcello", "Personal Owner");
        var personal = Personal();
        Assert.True(
            await new ScreenTimeNameSession(
                ld,
                Binding,
                personal.PageName,
                personal,
                null
            ).VerifyAsync(personal, [personal], default)
        );
        Assert.Equal(1, fake.Switches);
        Assert.Equal(0, fake.ForbiddenReads);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("persona")]
    [InlineData("name")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("duplicate-personal")]
    public async Task WrongOrAmbiguousPersonalMappingStopsBeforeDeviceReads(string change)
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        var session = new ScreenTimeNameSession(ld, Binding, personal.PageName, personal, null);
        var saved = new[] { personal };
        if (change == "page")
            personal.Type = "Page";
        if (change == "owner")
            personal.PageId = "999";
        if (change == "binding")
            personal.AccountBinding = Binding with { DeviceIndex = 99 };
        if (change == "persona")
            personal.PersonaUid = "999";
        if (change == "name")
            personal.PageName = "Different Owner";
        if (change == "missing")
            saved = [];
        if (change == "duplicate-personal")
            saved = [personal, personal.CreateSnapshot()];
        if (change == "duplicate")
        {
            var duplicate = personal.CreateSnapshot();
            duplicate.PageId = "999";
            duplicate.Type = "Page";
            duplicate.PageName = "PersonalOwner";
            saved = [personal, duplicate];
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.VerifyAsync(personal, saved, default)
        );
        Assert.Equal(0, fake.Reads);
        Assert.Equal(0, fake.ForbiddenReads);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("name")]
    [InlineData("foreground")]
    [InlineData("input")]
    public async Task ChangedSessionStopsWithoutGraphFallbackAndNeedsFreshInvocation(string change)
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        var session = new ScreenTimeNameSession(ld, Binding, personal.PageName, personal, null);
        Assert.True(await session.VerifyAsync(personal, [personal], default));
        if (change == "process")
            fake.Pid = "999";
        if (change == "name")
            fake.Xml = Device.Header("Other Actor");
        if (change == "foreground")
            fake.Activity = "another.app/Main";
        if (change == "input")
            fake.Xml = Device
                .Header(personal.PageName)
                .Replace(
                    "</hierarchy>",
                    "<node package='com.facebook.katana' class='android.widget.EditText'/></hierarchy>"
                );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.VerifyAsync(personal, [personal], default)
        );
        fake.Pid = "123";
        fake.Activity = "com.facebook.katana/Main";
        fake.Xml = Device.Header(personal.PageName);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.VerifyAsync(personal, [personal], default)
        );
        Assert.True(
            await new ScreenTimeNameSession(
                ld,
                Binding,
                personal.PageName,
                personal,
                null
            ).VerifyAsync(personal, [personal], default)
        );
        Assert.Equal(0, fake.ForbiddenReads);
    }

    [Fact]
    public async Task ClearFeedReusesProofWithoutReopeningSwitcher()
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        var session = new ScreenTimeNameSession(ld, Binding, personal.PageName, personal, null);
        Assert.True(await session.VerifyAsync(personal, [personal], default));
        fake.Xml = FacebookLayoutRegistryTests.Fixture("ld55-feed", "PFlowScreenTime");
        Assert.True(await session.VerifyAsync(personal, [personal], default));
        Assert.Equal(0, fake.Switches + fake.ForbiddenReads);
    }

    [Fact]
    public async Task PendingPublicationCannotTriggerPersonalSwitch()
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        fake.Xml = Device
            .Header("Other Page")
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' text='Uploading your reel'/></hierarchy>"
            );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ScreenTimeNameSession(ld, Binding, personal.PageName, personal, null).VerifyAsync(
                personal,
                [personal],
                default
            )
        );
        Assert.Equal(0, fake.Switches + fake.ForbiddenReads);
    }

    [Fact]
    public async Task ContradictoryOwnProfileTitleCannotAuthorizeTheFirstActivity()
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        fake.Xml = File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "PFlowScreenTime",
                    "ld56-personal-dashboard-captured.xml"
                )
            )
            .Replace(
                "</hierarchy>",
                "<node package='com.facebook.katana' content-desc='Personal Owner, see your profile' bounds='[48,108][405,188]'/></hierarchy>"
            );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ScreenTimeNameSession(ld, Binding, personal.PageName, personal, null).VerifyAsync(
                personal,
                [personal],
                default
            )
        );
        Assert.Equal(0, fake.Switches + fake.ForbiddenReads);
    }

    [Fact]
    public async Task CancelledVerificationDoesNotReadOrAct()
    {
        var (ld, fake) = Harness();
        var personal = Personal();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ScreenTimeNameSession(ld, Binding, personal.PageName, personal, null).VerifyAsync(
                personal,
                [personal],
                cancel.Token
            )
        );
        Assert.Equal(0, fake.Reads);
    }

    public class Device : DispatchProxy
    {
        public string Xml = Header("Personal Owner"),
            Pid = "123",
            Activity = "com.facebook.katana/Main";
        public int Reads,
            Switches,
            ForbiddenReads;

        public static string Header(string name) =>
            "<hierarchy><node package='com.facebook.katana' bounds='[0,0][720,1280]'/>"
            + "<node package='com.facebook.katana' content-desc='"
            + SecurityElement.Escape(name)
            + ", see your profile' enabled='true' bounds='[48,108][405,188]'/></hierarchy>";

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Reads++;
            switch (method!.Name)
            {
                case "DumpUiHierarchyAsync":
                    return Task.FromResult<string?>(Xml);
                case "DumpBloksScreenAsync":
                    return Task.FromResult<BloksScreenInfo?>(
                        AtxAgentClient.ParseBloksHierarchy(Xml)
                    );
                case "GetCurrentFocusedActivityAsync":
                    return Task.FromResult<string?>(Activity);
                case "ExecuteAdbAsync":
                    if ((string)args![1]! == "shell pidof com.facebook.katana")
                        return Task.FromResult(Pid);
                    ForbiddenReads++;
                    throw new InvalidOperationException(
                        "No Graph, authentication or UID lookup permitted"
                    );
                case "TapAsync":
                    Switches++;
                    Xml = Header("Personal Owner");
                    return Task.FromResult(true);
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
