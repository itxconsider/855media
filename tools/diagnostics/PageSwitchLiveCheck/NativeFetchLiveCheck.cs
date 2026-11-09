using System.Text.Json;
using _855Media.Core.LDPlayer;

internal static class NativeFetchLiveCheck
{
    public static async Task RunAsync(
        LDPlayerService ld,
        int index,
        string folder,
        string output,
        Func<string, Task> capture,
        CancellationToken ct,
        bool linksOnly = false,
        bool switchProofOnly = false,
        bool ownerPreparationOnly = false,
        bool useGraph = false,
        bool ownerReturnOnly = false,
        bool savedAccountStartup = false,
        string? startPageName = null,
        string? savedAccountFile = null,
        string? onlyPageName = null,
        bool startupFeedStart = false,
        bool selectedPageDetailsOnly = false,
        bool discoveryOnly = false
    )
    {
        using var audit = new UiOnlySwitchCheck.GraphRequestAudit();
        using var policy = new PFlowNativeFetchScope(
            ld,
            index,
            folder,
            allowGraphSessionRead: useGraph
        );
        await using var keyboard = new PFlowKeyboardSession(ld, index, folder);
        await keyboard.CaptureAsync(ct);
        var instance = (await ld.GetInstancesAsync(folder, ct)).Single(i => i.Index == index);
        var installationData = new PFlowInstallationData(folder);
        var scoped = Directory.Exists(installationData.DirectoryPath);
        var accountFile =
            savedAccountFile ?? (scoped ? installationData.FilePath("pflow_accounts.json") : null);
        var saved = (
            await PFlowPageStorage.LoadPagesAsync(
                scoped ? installationData.FilePath("facebook_pages.json") : null
            )
        )
            .Where(p =>
                p.AppClone == "FB-01"
                && (p.DeviceName == instance.Name || p.DeviceName == $"LD-{index}")
            )
            .ToArray();
        Task Log(string message)
        {
            var line = $"{DateTimeOffset.Now:O} {message}";
            Console.WriteLine(line);
            return File.AppendAllTextAsync(
                Path.Combine(output, "native-fetch.log"),
                line + Environment.NewLine
            );
        }
        using var publicHttp = new HttpClient(new PublicLinkTrace(Log));
        using var publicResolver = new PFlowProfileLinkResolver(publicHttp);
        var device = new Observed(
            new PFlowNativeFetchDevice(
                ld,
                index,
                folder,
                Log,
                publicResolver.ResolveUidAsync,
                useGraph: useGraph
            ),
            Log,
            capture
        );
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(12));
        try
        {
            await Log(
                "Starting production Fetch; optional API="
                    + useGraph
                    + "; saved stores are read-only; no publication."
            );
            PFlowPersonalAccount? startupAccount = null;
            FacebookPageTarget? requestedPage = null;
            if (savedAccountStartup)
            {
                var savedAccounts = await new PFlowAccountStore(accountFile).LoadAsync(ct);
                requestedPage =
                    onlyPageName == null
                        ? null
                        : saved.Single(p => p.PageName == onlyPageName && p.Type == "Page");
                startupAccount =
                    requestedPage != null
                        ? PFlowSinglePageFetchRecovery.OwnerCandidate(
                            requestedPage,
                            savedAccounts,
                            index
                        )
                        : savedAccounts.SingleOrDefault(a =>
                            a.Binding.DeviceIndex == index
                            && a.Binding.AppPackage == "com.facebook.katana"
                        )
                            ?? throw new InvalidOperationException(
                                "No saved account exists for this LD in the selected account store."
                            );
                async Task RequireIdle(CancellationToken token)
                {
                    token.ThrowIfCancellationRequested();
                    PFlowAccountStore.RequireCurrentBinding(
                        startupAccount.Binding,
                        (await ld.GetInstancesAsync(folder, token)).ToArray(),
                        folder
                    );
                    var xml = await ld.DumpUiHierarchyAsync(index, folder, token) ?? "";
                    if (
                        PFlowUiPrivacy.ContainsInput(xml)
                        || PFlowLoginScreen.RequiresAuthentication(PFlowLoginScreen.Classify(xml))
                        || new[]
                        {
                            FacebookScreenKind.ReelComposer,
                            FacebookScreenKind.PhotoComposer,
                            FacebookScreenKind.PhotoPicker,
                            FacebookScreenKind.StoryEditor,
                        }.Any(kind => FacebookLayoutRegistry.Match(xml, kind).IsMatch)
                    )
                        throw new InvalidOperationException(
                            "Existing draft/input/authentication blocks startup test."
                        );
                    var notifications = await ld.ExecuteAdbAsync(
                        index,
                        "shell dumpsys notification --noredact",
                        folder,
                        token
                    );
                    if (
                        PFlowConfirmation.ReadUploadNotifications(notifications).Any(n => n.Ongoing)
                    )
                        throw new InvalidOperationException("Active upload blocks startup test.");
                }
                await RequireIdle(budget.Token);
                if (startPageName != null)
                {
                    device.Configure(saved, startupAccount.Binding.OwnerUid);
                    var actors = await device.ScanAsync(budget.Token);
                    var page = actors.Single(a =>
                        a.Type == "Page"
                        && string.Equals(a.Name, startPageName, StringComparison.Ordinal)
                    );
                    if (!await device.SelectAsync(page, budget.Token))
                        throw new InvalidOperationException("Could not stage requested Page.");
                    var switcher = new PFlowPersonaSwitcherService(ld);
                    if (!await switcher.OpenFacebookMenuFromFeedAsync(index, folder, budget.Token))
                        throw new InvalidOperationException("Could not stage Page Menu.");
                    await capture("saved-startup-page-menu");
                    await Log(
                        "Staged Page Menu: "
                            + page.Name
                            + "; expected saved owner="
                            + startupAccount.Name
                    );
                }
                if (startupFeedStart)
                {
                    await RequireIdle(budget.Token);
                    if (
                        !await ld.OpenDeepLinkAsync(
                            index,
                            "fb://feed",
                            "com.facebook.katana",
                            folder,
                            budget.Token
                        )
                    )
                        throw new InvalidOperationException(
                            "Could not stage Feed for saved-account startup."
                        );
                    string staged = "";
                    for (var frame = 0; frame < 20; frame++)
                    {
                        await Task.Delay(500, budget.Token);
                        staged = await ld.DumpUiHierarchyAsync(index, folder, budget.Token) ?? "";
                        if (
                            FacebookLayoutRegistry
                                .Match(staged, FacebookScreenKind.NewsFeed)
                                .IsMatch
                        )
                            break;
                    }
                    await RequireIdle(budget.Token);
                    if (!FacebookLayoutRegistry.Match(staged, FacebookScreenKind.NewsFeed).IsMatch)
                        throw new InvalidOperationException("Staged Feed is not recognized.");
                    var (width, height) = PFlowPersonaSwitcherService.ParseScreenDimensions(staged);
                    if (
                        width <= 0
                        || height <= 0
                        || !await ld.SwipeAsync(
                            index,
                            width / 2,
                            height * 3 / 4,
                            width / 2,
                            height / 3,
                            350,
                            folder,
                            budget.Token
                        )
                    )
                        throw new InvalidOperationException("Could not stage scrolled Feed.");
                    await Task.Delay(800, budget.Token);
                    await capture("saved-startup-scrolled-feed");
                    await Log("Staged scrolled Feed for startup; no actor proof granted.");
                }
                var preparationGraphStart = audit.Count;
                var navigator = new PFlowNativeAccountNavigator(
                    ld,
                    folder,
                    _ => startupAccount.Name,
                    requireRecoveryIdle: (_, token) => RequireIdle(token),
                    postingNameOnly: true,
                    savedTargets: _ => saved
                );
                var automatic = new PFlowAutomaticSessionService(
                    navigator,
                    new NoDiagnosticCredentials(),
                    RequireIdle,
                    postingNameOnly: true
                );
                // Change only the in-memory copy: this diagnostic never logs out or reads credentials.
                startupAccount.AllowAutomaticLogin = false;
                startupAccount.AllowAccountRotation = false;
                await Log("BEGIN saved-account startup: " + startupAccount.Name);
                await automatic.EnsureAsync(
                    startupAccount,
                    Log,
                    budget.Token,
                    deviceLeaseAlreadyHeld: true
                );
                await capture("saved-startup-owner-confirmed");
                await Log(
                    "Saved-account startup passed: "
                        + startupAccount.Name
                        + "; preparationGraphCalls="
                        + (audit.Count - preparationGraphStart)
                );
            }
            if (ownerPreparationOnly)
            {
                var account = (await new PFlowAccountStore(accountFile).LoadAsync(ct)).Single(a =>
                    a.Binding.DeviceIndex == index && a.Binding.AppPackage == "com.facebook.katana"
                );
                var navigator = new PFlowNativeAccountNavigator(
                    ld,
                    folder,
                    _ => account.Name,
                    postingNameOnly: true,
                    savedTargets: _ => saved
                );
                var observed = await navigator.ObserveAsync(account.Binding, budget.Token);
                if (!PFlowPostingNameVerification.Verified(observed, account.Name))
                    throw new InvalidOperationException(
                        "Owner preparation failed: " + observed.FailureReason
                    );
                await Log("Owner preparation passed: " + account.Name + "; Graph=" + audit.Count);
                return;
            }
            if (discoveryOnly)
            {
                device.Configure(saved, null);
                var actors = await device.ScanAsync(budget.Token);
                if (
                    actors.Count(a => a.Type == "Profile") != 1
                    || actors.Any(a => a.Type == "Unknown")
                )
                    throw new Exception("Complete typed discovery was not confirmed.");
                await File.WriteAllTextAsync(
                    Path.Combine(output, "discovery.json"),
                    JsonSerializer.Serialize(new { actors, graphCalls = audit.Count }),
                    ct
                );
                await Log(
                    "Complete typed discovery passed; no metadata persistence; Graph=" + audit.Count
                );
                return;
            }
            if (switchProofOnly)
            {
                device.Configure(saved, null);
                var actors = await device.ScanAsync(budget.Token);
                var owner = actors.Single(a => a.Type == "Profile");
                try
                {
                    foreach (
                        var actor in ownerReturnOnly
                            ? new[] { owner }
                            : actors.OrderBy(a => a.Type == "Profile" ? 0 : 1).ToArray()
                    )
                        if (!await device.SelectAsync(actor, budget.Token))
                            throw new InvalidOperationException(
                                "Switch proof failed: " + actor.Name
                            );
                }
                finally
                {
                    using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    if (!await device.SelectAsync(owner, restore.Token))
                        throw new InvalidOperationException(
                            "Switch proof personal restoration failed."
                        );
                }
                await Log(
                    (
                        ownerReturnOnly
                            ? "Owner return passed; personal actor verified; Graph="
                            : "Switch proof passed for all actors; personal actor restored; Graph="
                    ) + audit.Count
                );
                if (audit.Count != 0)
                    throw new Exception("Unexpected Graph request.");
                return;
            }
            if (selectedPageDetailsOnly)
            {
                var page = saved.Single(p => p.Type == "Page" && p.PageName == onlyPageName);
                device.Configure(saved, page.UserUid);
                var actors = await device.ScanAsync(budget.Token);
                var owner = actors.Single(a =>
                    a.Type == "Profile"
                    && string.Equals(a.Name, page.ManagedBy, StringComparison.OrdinalIgnoreCase)
                );
                var actor = actors.Single(a =>
                    a.Type == "Page"
                    && string.Equals(a.Name, page.PageName, StringComparison.OrdinalIgnoreCase)
                );
                try
                {
                    if (!await device.SelectAsync(actor, budget.Token))
                        throw new Exception("Page selection failed.");
                    var details = await device.PageDetailsAsync(
                        new FacebookPageInfo
                        {
                            PageName = page.PageName,
                            PageId = page.PageId,
                            PersonaUid = page.PersonaUid,
                            UserUid = page.UserUid,
                            Type = "Page",
                        },
                        page.UserUid,
                        owner.Name,
                        budget.Token
                    );
                    if (
                        details == null
                        || !string.Equals(
                            details.PageName,
                            actor.Name,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                        throw new Exception("Page metadata proof failed.");
                    await File.WriteAllTextAsync(
                        Path.Combine(output, "selected-page-details.json"),
                        JsonSerializer.Serialize(new { details, graphCalls = audit.Count }),
                        ct
                    );
                }
                finally
                {
                    using var restore = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                    if (!await device.SelectAsync(owner, restore.Token))
                        throw new Exception("Owner restoration failed.");
                }
                await Log(
                    "Selected Page details probe completed; owner restored; Graph=" + audit.Count
                );
                return;
            }
            var result = await new PFlowNativeFetch(device).RunAsync(
                saved,
                Log,
                budget.Token,
                expectedOwnerName: startupAccount?.Name,
                expectedOwnerId: startupAccount?.Binding.OwnerUid,
                onlyPageName: onlyPageName,
                readPersonalDetails: !linksOnly
            );
            if (requestedPage != null && startupAccount != null)
            {
                var fresh = result.Details?.Pages.SingleOrDefault(p =>
                    PFlowSinglePageFetchRecovery.FreshPageMatches(
                        requestedPage,
                        p,
                        startupAccount.Binding
                    )
                );
                if (fresh == null)
                    throw new InvalidOperationException(
                        "Requested pending Page did not pass identity recovery proof."
                    );
                await Log(
                    "Requested pending Page identity verified; production data remains unchanged: "
                        + fresh.PageName
                );
            }
            await File.WriteAllTextAsync(
                Path.Combine(output, "native-fetch-result.json"),
                JsonSerializer.Serialize(
                    new { result, appGraphCalls = audit.Count },
                    new JsonSerializerOptions { WriteIndented = true }
                ),
                ct
            );
            await Log(
                $"Finished: owner={result.OwnerName}; pages={result.Details?.Pages.Count}; unresolved={string.Join(", ", result.Unresolved)}; Graph={audit.Count}"
            );
            if (!useGraph && audit.Count != 0)
                throw new Exception("Unexpected managed Graph request.");
        }
        catch (Exception ex)
        {
            await Log($"FAILED {ex.GetType().Name}: {ex.Message}; Graph={audit.Count}");
            await capture("native-fetch-failure");
            throw;
        }
        finally
        {
            await Log($"Keyboard restored={await keyboard.RestoreAsync()}");
            await capture("native-fetch-final");
        }
    }

    private sealed class NoDiagnosticCredentials : IPFlowCredentialStore
    {
        public Task<string> SaveAsync(
            PFlowAccountBinding binding,
            PFlowCredential credential,
            CancellationToken ct = default
        ) =>
            throw new InvalidOperationException(
                "Credential writes are disabled in this diagnostic."
            );

        public Task<PFlowCredential> LoadAsync(
            PFlowAccountBinding binding,
            string reference,
            CancellationToken ct = default
        ) =>
            throw new InvalidOperationException(
                "Credential reads are disabled in this diagnostic."
            );

        public void Delete(string reference) =>
            throw new InvalidOperationException(
                "Credential deletion is disabled in this diagnostic."
            );
    }

    private sealed class PublicLinkTrace(Func<string, Task> log)
        : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            var response = await base.SendAsync(request, ct);
            var location = response.Headers.Location;
            await log(
                $"Public-link response: HTTP {(int)response.StatusCode}; redirect path={location?.GetComponents(UriComponents.Path, UriFormat.SafeUnescaped)}; cookie/auth=false."
            );
            return response;
        }
    }

    private sealed class Observed(
        IPFlowNativeFetchDevice inner,
        Func<string, Task> log,
        Func<string, Task> capture
    ) : IPFlowNativeFetchDevice
    {
        public void Configure(IReadOnlyList<FacebookPageTarget> saved, string? expectedOwnerId) =>
            inner.Configure(saved, expectedOwnerId);

        private int sequence;

        private async Task<T> Step<T>(string label, Func<Task<T>> run)
        {
            var id = ++sequence;
            await log($"BEGIN {id} {label}");
            var result = await run();
            await log($"END {id} {label}: {JsonSerializer.Serialize(result)}");
            await capture($"fetch-{id:00}");
            return result;
        }

        public Task<PFlowFetchActor[]> ScanAsync(CancellationToken ct) =>
            Step("scan switcher", () => inner.ScanAsync(ct));

        public Task<bool> SelectAsync(PFlowFetchActor actor, CancellationToken ct) =>
            Step("select " + actor.Name, () => inner.SelectAsync(actor, ct));

        public Task<string?> ReadNumericIdAsync(PFlowFetchActor actor, CancellationToken ct) =>
            Step("read own link " + actor.Name, () => inner.ReadNumericIdAsync(actor, ct));

        public Task<FacebookAccountDetails?> PersonalDetailsAsync(
            string id,
            string name,
            CancellationToken ct
        ) => Step("personal details " + name, () => inner.PersonalDetailsAsync(id, name, ct));

        public Task<FacebookPageInfo?> PageDetailsAsync(
            FacebookPageInfo page,
            string ownerId,
            string ownerName,
            CancellationToken ct
        ) =>
            Step(
                "Page details " + page.PageName,
                () => inner.PageDetailsAsync(page, ownerId, ownerName, ct)
            );
    }
}
