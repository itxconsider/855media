using System.Reflection;
using System.Xml.Linq;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowProfileDatesTests
{
    [Theory]
    [InlineData("Created - Marcel Luft\nCamping")]
    [InlineData("Created - Marcel Luft\r\nCamping")]
    [InlineData("Created - Marcel Luft  Camping")]
    public void WrappedCreationEventReadsItsFullDate(string creationEvent)
    {
        var screen = Screen(
            Node("Page transparency", 20),
            Node("Marcel Luft\nCamping", 70),
            Node(creationEvent, 240),
            Node("September 22, 2026", 275)
        );
        Assert.Equal(
            "September 22, 2026",
            PFlowProfileDates.CreationDate(screen, false, "Marcel Luft Camping")
        );
        Assert.Empty(PFlowProfileDates.CreationDate(screen, false, "Another Page"));
    }

    [Theory]
    [InlineData("Marcel Luft  Camping")]
    [InlineData("Marcel Luft\nCamping")]
    [InlineData("Marcel Luft\u00a0\u00a0Camping")]
    [InlineData("  Marcel\tLuft\r\nCamping  ")]
    public void TransparencyMatchesFullNamesWithPresentationWhitespace(string visibleName)
    {
        foreach (var attribute in new[] { "text", "content-desc" })
        {
            var xml = new XElement(
                "hierarchy",
                new XElement("node", new XAttribute(attribute, visibleName))
            ).ToString();
            var screen = AtxAgentClient.ParseBloksHierarchy(xml);
            Assert.True(PFlowProfileDates.MatchesName(screen, "Marcel Luft Camping"));
            Assert.False(PFlowProfileDates.MatchesName(screen, "Marcel Luft"));
            Assert.False(PFlowProfileDates.MatchesName(screen, "Marcel Luft Outdoors"));
        }
    }

    [Theory]
    [InlineData("Marcel Luft…")]
    [InlineData("Marcel Luft Camping Extra")]
    [InlineData("")]
    public void TransparencyStillRejectsTruncatedOrDifferentFullNames(string visibleName)
    {
        var xml = new XElement(
            "hierarchy",
            new XElement("node", new XAttribute("text", visibleName))
        ).ToString();
        Assert.False(
            PFlowProfileDates.MatchesName(
                AtxAgentClient.ParseBloksHierarchy(xml),
                "Marcel Luft Camping"
            )
        );
    }

    private static XElement Node(string label, int y, bool clickable = false) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("text", label),
            new XAttribute("enabled", "true"),
            new XAttribute("clickable", clickable ? "true" : "false"),
            new XAttribute("bounds", $"[82,{y}][500,{y + 30}]")
        );

    private static BloksScreenInfo Screen(params XElement[] nodes) =>
        AtxAgentClient.ParseBloksHierarchy(new XElement("hierarchy", nodes).ToString());

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "PFlowScreenTime", name + ".xml")
        );

    private static XElement ProfileTab(string name, int ordinal, int y) =>
        new(
            "node",
            new XAttribute("package", "com.facebook.katana"),
            new XAttribute("content-desc", $"{name}, tab {ordinal} of 4"),
            new XAttribute("clickable", "true"),
            new XAttribute("bounds", $"[10,{y}][600,{y + 40}]"),
            Node(name, y + 1)
        );

    [Fact]
    public void ProfileNavigationTabsDoNotEndBirthdayScrolling()
    {
        var screen = Screen(ProfileTab("Posts", 1, 40), ProfileTab("Friends", 2, 40));
        Assert.False(PFlowProfileDates.ReachedProfileFeed(screen));
    }

    [Fact]
    public void StickyProfileTabsDoNotHideBornRowAboveTheRealFriendsSection()
    {
        var screen = Screen(
            ProfileTab("Posts", 1, 40),
            ProfileTab("Friends", 2, 40),
            Node("Born on April 10, 1992", 200),
            Node("Friends", 400)
        );
        Assert.Equal(
            "April 10, 1992",
            PFlowProfileDates.Birthdate(screen, confirmedProfileContext: true)
        );
        Assert.True(PFlowProfileDates.ReachedProfileFeed(screen));
    }

    [Theory]
    [InlineData("At a glance", "Born July 8, 1996", "July 8, 1996")]
    [InlineData("At a glance", "Born April 10, 1992", "April 10, 1992")]
    [InlineData("At a glance", "Born on April 10, 1992", "April 10, 1992")]
    [InlineData("At a glance", "Born July 7, 1999", "July 7, 1999")]
    [InlineData("Personal details", "March 3, 1995", "March 3, 1995")]
    [InlineData("Personal details", "December 5, 1984", "December 5, 1984")]
    public void ReadsProvidedBirthdaySectionVariants(string header, string label, string expected)
    {
        var screen = Screen(
            Node("People you may know", 100),
            Node("May 9, 1980", 150),
            Node(header, 300),
            Node("New York", 350),
            Node(label, 400),
            Node("Friends", 500),
            Node("January 2, 1970", 550)
        );
        Assert.Equal(expected, PFlowProfileDates.Birthdate(screen));
    }

    [Theory]
    [InlineData("May 9, 1980")]
    [InlineData("Born April 10, 1992")]
    public void DatesOutsideProfileDetailsAreNeverBirthdays(string label)
    {
        Assert.Empty(
            PFlowProfileDates.Birthdate(Screen(Node("People you may know", 100), Node(label, 200)))
        );
        Assert.Empty(
            PFlowProfileDates.Birthdate(
                Screen(Node("Personal details", 100), Node("Friends", 200), Node(label, 300))
            )
        );
    }

    [Fact]
    public void MultipleDifferentDatesInSectionAreAmbiguous()
    {
        Assert.Empty(
            PFlowProfileDates.Birthdate(
                Screen(
                    Node("Personal details", 100),
                    Node("March 3, 1995", 200),
                    Node("April 4, 1996", 250),
                    Node("Friends", 400)
                )
            )
        );
    }

    [Fact]
    public void ScrolledBirthdayIsReadBeforeStoppingAtFriendsOrComposer()
    {
        foreach (
            var boundary in new[]
            {
                new[] { Node("Friends", 400) },
                new[] { Node("Reel", 400), Node("Live", 400) },
            }
        )
        {
            var screen = Screen(
                new[] { Node("Born on April 10, 1992", 200) }.Concat(boundary).ToArray()
            );
            Assert.Equal("April 10, 1992", PFlowProfileDates.Birthdate(screen, true));
            Assert.True(PFlowProfileDates.ReachedProfileFeed(screen));
        }
        Assert.False(PFlowProfileDates.ReachedProfileFeed(Screen(Node("Reels", 100))));
        Assert.Empty(
            PFlowProfileDates.Birthdate(
                Screen(Node("Friends", 100), Node("Born on April 10, 1992", 200)),
                true
            )
        );
    }

    [Fact]
    public void ActualScrolledBornRowRequiresConfirmedProfileContext()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-born-row-scrolled"));
        Assert.Empty(PFlowProfileDates.Birthdate(screen));
        Assert.Equal("July 7, 1999", PFlowProfileDates.Birthdate(screen, true));
    }

    [Fact]
    public void ActualProfessionalProfileCreationDateIsReadWithoutUsingPageIdAsPersonalUid()
    {
        var screen = AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-profile-transparency"));
        Assert.Equal(
            "January 23, 2024",
            PFlowProfileDates.CreationDate(screen, true, "Scarlet Marcello")
        );
        Assert.Empty(PFlowProfileDates.CreationDate(screen, false, "Scarlet Marcello"));
        Assert.Empty(PFlowProfileDates.CreationDate(screen, true, "Another Person"));
        Assert.Empty(PFlowProfileDates.Birthdate(screen));
    }

    [Fact]
    public void PageCreationIgnoresNameChangesAndFindsCreatedEvent()
    {
        var screen = Screen(
            Node("Page transparency", 50),
            Node("Z Outdoors+", 110),
            Node("Changed name to Z Outdoors+", 300),
            Node("September 23, 2026", 330),
            Node("Changed name to 668 BET Manila", 400),
            Node("March 19, 2024", 430),
            Node("Created - W500 Fun", 500),
            Node("March 12, 2024", 530)
        );
        Assert.Equal(
            "March 12, 2024",
            PFlowProfileDates.CreationDate(screen, false, "Z Outdoors+")
        );
    }

    [Fact]
    public void ProModeDateCannotReplaceMissingProfileCreationDate()
    {
        var screen = Screen(
            Node("Profile transparency", 50),
            Node("Scarlet Marcello", 110),
            Node("Profile created", 300),
            Node("Professional mode turned on", 330),
            Node("January 23, 2024", 360)
        );
        Assert.Empty(PFlowProfileDates.CreationDate(screen, true, "Scarlet Marcello"));
    }

    [Theory]
    [InlineData("Joined Facebook January 2024", false)]
    [InlineData("January 23, 2024", true)]
    [InlineData("Not Set", false)]
    public void RecognizesPrecisionWithoutInventingFirstOfMonth(string value, bool full)
    {
        Assert.Equal(full, PFlowProfileDates.HasFullDate(value));
    }

    [Fact]
    public void CacheMergePreservesFullCreationDateAcrossLaterMonthOnlyFetches()
    {
        var saved = new FacebookPageTarget
        {
            DeviceName = "LD-55",
            AppClone = "FB-01",
            UserUid = "owner",
            PageId = "owner",
            Type = "Profile",
            CreationDate = "January 23, 2024",
        };
        var incoming = saved.CreateSnapshot();
        incoming.CreationDate = "Joined Facebook January 2024";
        Assert.Equal("January 23, 2024", PFlowTargetMerge.Cached(saved, incoming).CreationDate);
        saved.CreationDate = "Joined Facebook January 2024";
        incoming.CreationDate = "January 23, 2024";
        Assert.Equal("January 23, 2024", PFlowTargetMerge.Cached(saved, incoming).CreationDate);
    }

    [Fact]
    public async Task ActualNameSheetTransparencyRouteReturnsFullDateAndClosesOnlyThatRoute()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        var date = await Read(ld);
        Assert.Equal("January 23, 2024", date);
        Assert.Equal(1, fake.Taps);
        Assert.Equal(1, fake.Backs);
    }

    [Fact]
    public async Task FriendSuggestionsAreScrolledPastWithoutTappingAdd()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.BirthdayFlow = true;
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            955,
            "Scarlet Marcello",
            true,
            null,
            default,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );
        Assert.Equal("July 7, 1999", result.Birthdate);
        Assert.Equal(3, fake.Swipes);
        Assert.Equal(2, fake.Taps); // Verified name and Transparency; no Add friend tap.
        Assert.Equal("January 23, 2024", result.CreationDate);
    }

    [Fact]
    public async Task DetailsReaderRejectsUnconfirmedOwnProfileBeforeScrolling()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Output = Fixture("ld55-feed");
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            955,
            "Scarlet Marcello",
            true,
            null,
            default,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );
        Assert.Empty(result.Birthdate);
        Assert.Empty(result.CreationDate);
        Assert.Equal(0, fake.Swipes);
        Assert.Equal(0, fake.Taps);
    }

    [Fact]
    public async Task UnsupportedTransparencyNeverInventsCreationEvidence()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Output = Fixture("ld55-profile-name-sheet");
        Assert.Empty(await Read(ld));
        Assert.Equal(1, fake.Backs);
    }

    [Fact]
    public async Task TransparencyLinkCanRenderAfterJoinedText()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.DelayedEntry = true;
        var initial = Screen(
            Node("Scarlet Marcello", 100),
            Node("Joined Facebook January 2024", 200)
        );
        var date = await PFlowProfileDetailsReader.ReadTransparencyAsync(
            ld,
            955,
            initial,
            "Scarlet Marcello",
            true,
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal("January 23, 2024", date);
        Assert.Equal(1, fake.Taps);
    }

    [Fact]
    public async Task CleanupFailureDoesNotHideConfirmedDateOrPrimaryFailure()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.FailBack = true;
        Assert.Equal("January 23, 2024", await Read(ld));
        fake.FailRead = true;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Read(ld));
        Assert.Equal("primary read failure", ex.Message);
    }

    [Fact]
    public async Task CancellationDoesNotTapAgainOrPerformCleanupNavigation()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PFlowProfileDetailsReader.ReadTransparencyAsync(
                ld,
                955,
                AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-profile-name-sheet")),
                "Scarlet Marcello",
                true,
                null,
                cancellation.Token,
                (_, _) =>
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }
            )
        );
        Assert.Equal(1, fake.Taps);
        Assert.Equal(0, fake.Backs);
    }

    [Fact]
    public async Task PageCreationBelowLongHistoryRetainsVerifiedTargetContext()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.OnRead = swipe =>
            (
                swipe == 0
                    ? Screen(
                        Node("Page transparency", 30),
                        Node("John Ward", 100),
                        Node("Organizations that manage this Page", 300),
                        Node("Changed name to John Ward", 600),
                        Node("February 7, 2026", 640)
                    )
                : swipe < 4
                    ? Screen(
                        Node("Page transparency", 30),
                        Node("Changed name to Plumbing John", 200 + swipe * 20),
                        Node("May 12, 2025", 250 + swipe * 20)
                    )
                : Screen(
                    Node("Page transparency", 30),
                    Node("Created - John Ward", 200),
                    Node("August 9, 2024", 240),
                    Node("People who manage this Page", 400)
                )
            ).RawXml;
        var date = await PFlowProfileDetailsReader.ReadTransparencyAsync(
            ld,
            955,
            Screen(Node("John Ward", 100), Node("Transparency and privacy policy", 200, true)),
            "John Ward",
            false,
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal("August 9, 2024", date);
        Assert.Equal(4, fake.Swipes);
        Assert.Equal(1, fake.Taps);
        Assert.Equal(1, fake.Backs);
    }

    [Fact]
    public async Task TransparencyScrollStopsIfDestinationChanges()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.OnRead = swipe =>
            (
                swipe == 0
                    ? Screen(Node("Profile transparency", 30), Node("Scarlet Marcello", 100))
                    : Screen(
                        Node("All posts", 100),
                        Node("Profile created", 200),
                        Node("January 23, 2024", 240)
                    )
            ).RawXml;
        Assert.Empty(await Read(ld));
        Assert.Equal(1, fake.Swipes);
    }

    [Fact]
    public async Task UnchangedTransparencyStopsAtScrollEnd()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.Output = Screen(
            Node("Profile transparency", 30),
            Node("Scarlet Marcello", 100),
            Node("Professional mode turned on", 200),
            Node("January 23, 2024", 240)
        ).RawXml;
        Assert.Empty(await Read(ld));
        Assert.Equal(2, fake.Swipes);
    }

    [Fact]
    public async Task BirthdayCanBeBeyondEightSuggestionAndDetailsScreens()
    {
        var ld = DispatchProxy.Create<ILDPlayerService, Device>();
        var fake = (Device)(object)ld;
        fake.BirthdayFlow = true;
        fake.BirthdayAfter = 12;
        var result = await PFlowProfileDetailsReader.ReadOwnProfileAsync(
            ld,
            955,
            "Scarlet Marcello",
            true,
            null,
            default,
            (_, _) => Task.CompletedTask
        );
        Assert.Equal("July 7, 1999", result.Birthdate);
        Assert.Equal(12, fake.Swipes);
        Assert.Equal(2, fake.Taps);
    }

    private static Task<string> Read(ILDPlayerService ld) =>
        PFlowProfileDetailsReader.ReadTransparencyAsync(
            ld,
            955,
            AtxAgentClient.ParseBloksHierarchy(Fixture("ld55-profile-name-sheet")),
            "Scarlet Marcello",
            true,
            null,
            default,
            (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );

    public class Device : DispatchProxy
    {
        public int Taps,
            Backs;
        public string? Output;
        public Func<int, string?>? OnRead;
        public int BirthdayAfter = 3;
        public bool FailBack,
            FailRead;
        public bool BirthdayFlow;
        public bool DelayedEntry;
        public int Swipes;
        private bool _nameSheet,
            _transparency;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            foreach (var arg in args ?? [])
                if (arg is CancellationToken ct)
                    ct.ThrowIfCancellationRequested();
            switch (method!.Name)
            {
                case nameof(ILDPlayerService.TapAsync):
                    Taps++;
                    if (BirthdayFlow)
                    {
                        if (_nameSheet)
                            _transparency = true;
                        else
                            _nameSheet = true;
                    }
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.PressBackAsync):
                    Backs++;
                    if (BirthdayFlow)
                    {
                        if (_transparency)
                            _transparency = false;
                        else
                            _nameSheet = false;
                    }
                    if (FailBack)
                        throw new InvalidOperationException("cleanup failure");
                    return Task.FromResult(true);
                case nameof(ILDPlayerService.DumpUiHierarchyAsync):
                    if (FailRead)
                        throw new InvalidOperationException("primary read failure");
                    if (OnRead != null)
                        return Task.FromResult<string?>(OnRead(Swipes));
                    if (DelayedEntry && Taps == 0)
                        return Task.FromResult<string?>(Fixture("ld55-profile-name-sheet"));
                    if (BirthdayFlow && _transparency)
                        return Task.FromResult<string?>(Fixture("ld55-profile-transparency"));
                    if (BirthdayFlow && _nameSheet)
                        return Task.FromResult<string?>(Fixture("ld55-profile-name-sheet"));
                    if (BirthdayFlow)
                        return Task.FromResult<string?>(
                            Swipes == 0
                                ? Screen(
                                    Node("Scarlet Marcello", 100),
                                    Node("Edit profile", 150),
                                    Node("People you may know", 300),
                                    Node("Add friend", 900, true)
                                ).RawXml
                            : Swipes < BirthdayAfter
                                ? Screen(
                                    Node("People you may know", 100),
                                    Node("Add friend", 900, true)
                                ).RawXml
                            : Screen(
                                Node("At a glance", 100),
                                Node("Born July 7, 1999", 200),
                                Node("Friends", 300)
                            ).RawXml
                        );
                    return Task.FromResult<string?>(Output ?? Fixture("ld55-profile-transparency"));
                case nameof(ILDPlayerService.SwipeAsync):
                    Swipes++;
                    return Task.FromResult(true);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
