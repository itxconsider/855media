using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Clean Architecture implementation of Facebook account discovery, session verification,
/// detailed profile/page extraction, and persistence synchronization.
/// </summary>
public class PFlowAccountDiscoveryService : IPFlowAccountDiscoveryService
{
    // A Page can repeat its single display label in both legacy person-name fields.
    // The cached name itself can also be the concatenation of those repeated fields.
    internal static string ResolveRootPageName(string resolved, string dump, bool isPage)
    {
        if (!isPage)
            return resolved;
        string Field(string key) =>
            Regex
                .Match(
                    dump,
                    @"(?:^|[\r\n\t])" + key + @"[\r\n\s]+([^\r\n]{1,60})",
                    RegexOptions.IgnoreCase
                )
                .Groups[1]
                .Value.Trim();
        var first = Field("first_name");
        var last = Field("last_name");
        return
            first.Length > 0
            && string.Equals(first, last, StringComparison.Ordinal)
            && Regex.Replace(resolved.Trim(), @"\s+", " ") == first + " " + last
            ? first
            : resolved;
    }

    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;
    private readonly IPFlowBloksService _bloks;
    private readonly IPFlowPersonaSwitcherService _personaSwitcher;
    private PFlowDiscoveryDelayConfig _delayConfig;

    public PFlowDiscoveryDelayConfig DelayConfig
    {
        get => _delayConfig;
        set => _delayConfig = value ?? new PFlowDiscoveryDelayConfig();
    }

    public PFlowAccountDiscoveryService(
        ILDPlayerService ldPlayer,
        AtxAgentClient? atxClient = null,
        IPFlowBloksService? bloksService = null,
        PFlowDiscoveryDelayConfig? delayConfig = null,
        IPFlowPersonaSwitcherService? personaSwitcher = null
    )
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
        _bloks = bloksService ?? new PFlowBloksService(_ldPlayer, _atxClient);
        _personaSwitcher =
            personaSwitcher ?? new PFlowPersonaSwitcherService(_ldPlayer, _atxClient);
        _delayConfig = delayConfig ?? new PFlowDiscoveryDelayConfig();
    }

    public void ApplyDelayConfig(PFlowDiscoveryDelayConfig config)
    {
        _delayConfig = config ?? new PFlowDiscoveryDelayConfig();
    }

    public void ApplyDelayConfig(AutoPostConfig config)
    {
        _delayConfig = PFlowDiscoveryDelayConfig.FromAutoPostConfig(config);
    }

    public void ApplyDelayConfig(LDSettings settings)
    {
        _delayConfig = PFlowDiscoveryDelayConfig.FromLdSettings(settings);
    }

    public async Task<FacebookPageInfo?> FetchPageInfoFromDeviceAsync(
        int index,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        // 1. If pageId provided, navigate directly to page or profile
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            await _ldPlayer.OpenDeepLinkAsync(
                index,
                $"fb://page/{pageId}",
                "com.facebook.katana",
                customPath,
                cancellationToken
            );
            await PFlowFetchPacing.DelayAsync(_delayConfig.DelayPageLoadMs, cancellationToken);
        }

        // 2. Dump UI hierarchy
        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        return ParsePageInfoFromXml(xml, pageId);
    }

    public static FacebookPageInfo ParsePageInfoFromXml(string xml, string fallbackPageId)
    {
        var info = new FacebookPageInfo { PageId = fallbackPageId, Type = "Page" };

        try
        {
            var doc = XDocument.Parse(xml);
            var nodes = doc.Descendants("node").ToList();

            var texts = new List<string>();
            foreach (var node in nodes)
            {
                var text = node.Attribute("text")?.Value;
                var desc = node.Attribute("content-desc")?.Value;
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text.Trim());
                if (!string.IsNullOrWhiteSpace(desc) && desc != text)
                    texts.Add(desc.Trim());
            }

            // Extract follower / like / reach stats
            // Matches: "1.2K followers", "120 likes", "530 reels", "1.2M views"
            var followerRegex = new Regex(
                @"([\d,.]+\s*[KMBkmb]?)\s*(followers|following|likes|fans)",
                RegexOptions.IgnoreCase
            );
            var reachRegex = new Regex(
                @"([\d,.]+\s*[KMBkmb]?)\s*(views|reach|post reach|talking about this)",
                RegexOptions.IgnoreCase
            );
            var reelsRegex = new Regex(@"(\d+)\s*reels", RegexOptions.IgnoreCase);
            var videosRegex = new Regex(@"(\d+)\s*videos", RegexOptions.IgnoreCase);

            foreach (var t in texts)
            {
                var createdMatch = Regex.Match(
                    t,
                    @"(?:Page\s+)?(?:created|joined)(?:\s+Facebook)?(?:\s+on|\s+in)?\s*[:·,]?\s*((?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}|(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4})",
                    RegexOptions.IgnoreCase
                );
                if (createdMatch.Success && string.IsNullOrWhiteSpace(info.CreationDate))
                    info.CreationDate = createdMatch.Groups[1].Value.Trim();

                var fm = followerRegex.Match(t);
                if (fm.Success && info.Followers == 0)
                {
                    info.Followers = ParseCountString(fm.Groups[1].Value);
                }

                var rm = reachRegex.Match(t);
                if (rm.Success && string.IsNullOrEmpty(info.Reach))
                {
                    info.Reach = $"{rm.Groups[1].Value} Views";
                }

                var reelMatch = reelsRegex.Match(t);
                if (reelMatch.Success && info.ReelsCount == 0)
                {
                    if (int.TryParse(reelMatch.Groups[1].Value, out var c))
                        info.ReelsCount = c;
                }

                // Check Type
                if (
                    t.Contains("Add friend", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Mutual friends", StringComparison.OrdinalIgnoreCase)
                )
                {
                    info.Type = "Profile";
                }
                else if (
                    t.Contains("Digital creator", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Product/service", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("Page ·", StringComparison.OrdinalIgnoreCase)
                )
                {
                    info.Type = "Page";
                }
            }

            // Facebook often renders a statistic's number and label as separate
            // accessibility nodes (for example, "68" followed by "followers").
            var joinedText = string.Join(" ", texts);
            var joinedFollowers = followerRegex.Match(joinedText);
            if (joinedFollowers.Success && info.Followers == 0)
                info.Followers = ParseCountString(joinedFollowers.Groups[1].Value);

            var joinedReach = reachRegex.Match(joinedText);
            if (joinedReach.Success && string.IsNullOrEmpty(info.Reach))
                info.Reach = $"{joinedReach.Groups[1].Value} Views";

            var joinedReels = reelsRegex.Match(joinedText);
            if (joinedReels.Success && info.ReelsCount == 0)
            {
                if (int.TryParse(joinedReels.Groups[1].Value, out var reelsCount))
                    info.ReelsCount = reelsCount;
            }

            var joinedVideos = videosRegex.Match(joinedText);
            if (joinedVideos.Success && info.VideosCount == 0)
            {
                if (int.TryParse(joinedVideos.Groups[1].Value, out var videosCount))
                    info.VideosCount = videosCount;
            }

            // Extract page name: Look for prominent text node near top
            var titleCandidates = nodes
                .Where(n =>
                {
                    var text = n.Attribute("text")?.Value;
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                        return false;
                    if (
                        text.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
                        || text.Equals("Search", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("followers", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("likes", StringComparison.OrdinalIgnoreCase)
                    )
                        return false;
                    return true;
                })
                .Select(n => n.Attribute("text")?.Value?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (titleCandidates.Count > 0)
            {
                info.PageName = titleCandidates[0]!;
            }
        }
        catch
        {
            // Graceful fallback if XML parsing fails
        }

        return info;
    }

    private async Task<FacebookPageInfo?> FetchPageInfoFromBloksAsync(
        int index,
        string? pageUid,
        string? pageId,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(pageUid))
            return null;

        try
        {
            var info = await _bloks.ExtractBloksPageInfoAsync(
                index,
                pageUid,
                pageId,
                customPath,
                cancellationToken
            );
            return info;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[LD-{index}] Bloks Page lookup failed for UID {pageUid}: {ex.Message}"
            );
            return null;
        }
    }

    private async Task<string?> FetchBirthdayFromBloksAsync(
        int index,
        string? customPath,
        CancellationToken cancellationToken
    )
    {
        if (
            !await _bloks.OpenBloksAccountsCenterAsync(
                index,
                "com.facebook.katana",
                customPath,
                cancellationToken
            )
        )
            return null;

        await PFlowFetchPacing.DelayAsync(_delayConfig.DelayAfterTapMs, cancellationToken);
        var screen = await _bloks.DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (screen?.FindElement("Personal details") != null)
        {
            if (
                !await _bloks.ClickBloksElementAsync(
                    index,
                    "Personal details",
                    customPath: customPath,
                    cancellationToken: cancellationToken
                )
            )
                return null;

            await PFlowFetchPacing.DelayAsync(_delayConfig.DelayAfterTapMs, cancellationToken);
            screen = await _bloks.DumpBloksScreenAsync(index, customPath, cancellationToken);
        }

        if (screen?.FindElement("Birthday") == null)
            return null;

        if (
            !await _bloks.ClickBloksElementAsync(
                index,
                "Birthday",
                customPath: customPath,
                cancellationToken: cancellationToken
            )
        )
            return null;

        await PFlowFetchPacing.DelayAsync(_delayConfig.DelayAfterTapMs, cancellationToken);
        var accountInfo = await _bloks.ExtractBloksAccountInfoAsync(
            index,
            customPath,
            cancellationToken
        );
        return string.IsNullOrWhiteSpace(accountInfo?.Birthday) ? null : accountInfo.Birthday;
    }

    private static void MergePageDetails(FacebookPageInfo target, FacebookPageInfo? source)
    {
        if (source == null)
            return;

        if (
            string.IsNullOrWhiteSpace(target.PageName)
            && !string.IsNullOrWhiteSpace(source.PageName)
        )
            target.PageName = source.PageName;
        if (!string.IsNullOrWhiteSpace(source.PageId))
            target.PageId = source.PageId;
        if (!string.IsNullOrWhiteSpace(source.PersonaUid))
            target.PersonaUid = source.PersonaUid;
        if (!string.IsNullOrWhiteSpace(source.CreationDate))
            target.CreationDate = source.CreationDate;
        if (!string.IsNullOrWhiteSpace(source.Email))
            target.Email = source.Email;
        if (source.Followers > 0)
            target.Followers = source.Followers;
        if (!string.IsNullOrWhiteSpace(source.Reach))
            target.Reach = source.Reach;
        if (source.ReelsCount > 0)
            target.ReelsCount = source.ReelsCount;
        if (source.VideosCount > 0)
            target.VideosCount = source.VideosCount;
    }

    public async Task<FacebookDeviceAccountInfo?> FetchDeviceFacebookAccountAndPagesAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var details = await PFlowNativeDiscovery.ReadAsync(
            _ldPlayer,
            index,
            customPath,
            cancellationToken
        );
        return details == null
            ? null
            : new FacebookDeviceAccountInfo
            {
                DeviceName = details.DeviceName,
                UserUid = details.Uid,
                UserName = details.Name,
                UserProfile = new FacebookPageInfo
                {
                    PageId = details.Uid,
                    UserUid = details.Uid,
                    PageName = details.Name,
                    Type = "Profile",
                },
                Pages = details.Pages,
            };
    }

    public static FacebookDeviceAccountInfo ParseDeviceAccountAndPagesFromXml(
        string? profileXml,
        string? pagesXml,
        string? prefsOutput,
        int deviceIndex
    )
    {
        var deviceName = $"LD-{deviceIndex}";
        var result = new FacebookDeviceAccountInfo { DeviceName = deviceName };

        // 1. Try to extract User UID
        string userUid = string.Empty;
        if (!string.IsNullOrWhiteSpace(prefsOutput))
        {
            var uidMatch = Regex.Match(prefsOutput, @"\b(615\d{11,13}|\d{14,16})\b");
            if (uidMatch.Success)
            {
                userUid = uidMatch.Groups[1].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(userUid))
        {
            userUid = $"6159{deviceIndex:D2}000{DateTime.Now.Year % 100}1";
        }
        result.UserUid = userUid;

        // 2. Extract User Profile Name from profile XML
        string userName = $"User LD-{deviceIndex}";
        if (!string.IsNullOrWhiteSpace(profileXml))
        {
            var copyMatch = Regex.Match(
                profileXml,
                @"(?:Copy\s+)?([A-Za-z0-9\s.'-]{2,40})'s\s+link",
                RegexOptions.IgnoreCase
            );
            if (
                copyMatch.Success
                && !copyMatch
                    .Groups[1]
                    .Value.Contains("facebook", StringComparison.OrdinalIgnoreCase)
            )
            {
                userName = copyMatch.Groups[1].Value.Trim();
            }
            else
            {
                try
                {
                    var doc = XDocument.Parse(profileXml);
                    var candidates = doc.Descendants("node")
                        .Select(n =>
                        {
                            var text = n.Attribute("text")?.Value?.Trim();
                            if (string.IsNullOrWhiteSpace(text))
                                text = n.Attribute("content-desc")?.Value?.Trim();
                            return text;
                        })
                        .OfType<string>()
                        .Where(t => !IsIgnoredProfileText(t))
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        userName = candidates[0]!;
                    }
                }
                catch { }
            }
        }
        result.UserName = userName;

        result.UserProfile = new FacebookPageInfo
        {
            PageId = userUid,
            UserUid = userUid,
            PageName = userName,
            Type = "Profile",
        };

        // 3. Extract Pages from pages XML
        if (!string.IsNullOrWhiteSpace(pagesXml))
        {
            try
            {
                var doc = XDocument.Parse(pagesXml);
                var pageNodes = doc.Descendants("node")
                    .Where(n =>
                    {
                        var text = n.Attribute("text")?.Value?.Trim();
                        var desc = n.Attribute("content-desc")?.Value?.Trim();
                        var str = !string.IsNullOrEmpty(text) ? text : desc;
                        if (string.IsNullOrWhiteSpace(str) || str.Length < 2)
                            return false;
                        if (
                            str.Equals("Pages", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Create", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Discover", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Invitations", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Liked Pages", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
                            || str.Equals("Search", StringComparison.OrdinalIgnoreCase)
                            || str.Contains("followers", StringComparison.OrdinalIgnoreCase)
                        )
                            return false;
                        return true;
                    })
                    .Select(n =>
                        n.Attribute("text")?.Value?.Trim()
                        ?? n.Attribute("content-desc")?.Value?.Trim()!
                    )
                    .Distinct()
                    .ToList();

                int pIdx = 1;
                foreach (var pName in pageNodes)
                {
                    if (pName.Equals(userName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var pId = $"6159{deviceIndex:D2}{pIdx:D2}890";
                    result.Pages.Add(
                        new FacebookPageInfo
                        {
                            PageId = pId,
                            UserUid = userUid,
                            PageName = pName,
                            Type = "Page",
                        }
                    );
                    pIdx++;
                }
            }
            catch { }
        }

        return result;
    }

    public async Task<FacebookAccountDetails?> ExtractFacebookAccountDetailsAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default,
        Func<FacebookAccountDetails, Task>? rootShellCompleted = null
    )
    {
        var details = await PFlowNativeDiscovery.ReadAsync(
            _ldPlayer,
            index,
            customPath,
            cancellationToken
        );
        if (details != null && rootShellCompleted != null)
            await rootShellCompleted(details); // Compatibility callback; payload is native UI data.
        return details;
    }

    public async Task<(
        bool IsActive,
        string? AccountName,
        string? StatusReason
    )> VerifyFacebookAccountActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var policy = new PFlowNativeFetchScope(_ldPlayer, index, customPath);
        var actors = await new PFlowNativeFetchDevice(
            _ldPlayer,
            index,
            customPath,
            _ => Task.CompletedTask
        ).ScanAsync(cancellationToken);
        var selected = actors.Where(a => a.Selected).ToArray();
        return selected.Length == 1
            ? (true, selected[0].Name, "Native selected actor; no live UID ownership claim")
            : (false, null, "Native account selection is unavailable or ambiguous");
    }

    public Task<List<FacebookAccountInfo>> ExtractFacebookAccountsFromRootShellAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<List<FacebookAccountInfo>>(
            new NotSupportedException(
                "Private Facebook storage extraction was removed. Use native UI Fetch."
            )
        );
    }

    public Task<List<FacebookAccountInfo>> ExtractFacebookAccountsDirectFromRootAdbAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException<List<FacebookAccountInfo>>(
            new NotSupportedException(
                "Private Facebook storage extraction was removed. Use native UI Fetch."
            )
        );
    }

    private static string SanitizeRootDump(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;
        return new string(
            raw.Select(ch => ch < 32 && ch is not '\r' and not '\n' and not '\t' ? '\n' : ch)
                .ToArray()
        );
    }

    private static void MergeRootAccount(
        List<FacebookAccountInfo> accounts,
        FacebookAccountInfo account
    )
    {
        var existing = accounts.FirstOrDefault(a =>
            string.Equals(a.Type, account.Type, StringComparison.OrdinalIgnoreCase)
            && (
                a.Uid == account.Uid
                || (!string.IsNullOrWhiteSpace(a.PageId) && a.PageId == account.PageId)
            )
        );
        if (existing is null)
        {
            accounts.Add(account);
            return;
        }
        if (string.IsNullOrWhiteSpace(existing.Name))
            existing.Name = account.Name;
        if (string.IsNullOrWhiteSpace(existing.PageId))
            existing.PageId = account.PageId;
        if (string.IsNullOrWhiteSpace(existing.UserUid))
            existing.UserUid = account.UserUid;
        if (string.IsNullOrWhiteSpace(existing.Email))
            existing.Email = account.Email;
        if (string.IsNullOrWhiteSpace(existing.PicUrl))
            existing.PicUrl = account.PicUrl;
        existing.IsActive |= account.IsActive;
    }

    public async Task<List<FacebookPageTarget>> SyncDiscoveredAccountsToStorageAsync(
        int index,
        string? deviceName = null,
        string appClone = "FB-01",
        string packageName = "com.facebook.katana",
        string? storageFilePath = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        var targetDevice = !string.IsNullOrWhiteSpace(deviceName) ? deviceName : $"LD-{index}";
        if (packageName != "com.facebook.katana")
            throw new NotSupportedException("Native Facebook discovery supports FB-01 only.");
        var discovered = await PFlowNativeDiscovery.ReadAccountsAsync(
            _ldPlayer,
            index,
            customPath,
            cancellationToken,
            storageFilePath
        );

        var existingPages = await PFlowPageStorage.LoadPagesAsync(storageFilePath);

        foreach (var account in discovered)
        {
            var match = existingPages.FirstOrDefault(p =>
                string.Equals(p.DeviceName, targetDevice, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.AppClone, appClone, StringComparison.OrdinalIgnoreCase)
                && (
                    string.Equals(p.PageId, account.PageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PageId, account.Uid, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PageName, account.Name, StringComparison.OrdinalIgnoreCase)
                )
            );

            if (match != null)
            {
                if (!string.IsNullOrWhiteSpace(account.PageId))
                    match.PageId = account.PageId;
                if (!string.IsNullOrWhiteSpace(account.Uid))
                    match.PersonaUid = account.Uid;
                if (!string.IsNullOrWhiteSpace(account.UserUid))
                    match.UserUid = account.UserUid;
                if (!string.IsNullOrWhiteSpace(account.Name))
                    match.PageName = account.Name;
                if (!string.IsNullOrWhiteSpace(account.Type))
                    match.Type = account.Type;
                if (!string.IsNullOrWhiteSpace(account.Email))
                    match.Email = account.Email;
                if (
                    string.Equals(account.Type, "Page", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(account.UserUid)
                )
                    match.ManagedBy = account.UserUid;
            }
            else
            {
                var newTarget = account.ToFacebookPageTarget(targetDevice, appClone);
                existingPages.Add(newTarget);
            }
        }

        await PFlowPageStorage.SavePagesAsync(existingPages, storageFilePath);
        return existingPages
            .Where(p =>
                string.Equals(p.DeviceName, targetDevice, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    public static List<FacebookAccountInfo> ParseRootShellAccountOutput(string? output)
    {
        var accounts = new List<FacebookAccountInfo>();
        if (string.IsNullOrWhiteSpace(output))
            return accounts;

        string activeUid = string.Empty;
        string activeName = string.Empty;
        string activeAccessToken = string.Empty;
        string activeCookies = string.Empty;

        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("AUTH|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('|');
                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    activeUid = parts[1].Trim();
                if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    activeName = parts[2].Trim();
                if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
                    activeAccessToken = parts[3].Trim();
                if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
                    activeCookies = parts[4].Trim();
            }
            else if (line.StartsWith("PERSONA|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('|');
                if (parts.Length < 3)
                    continue;

                var userUid = parts[1].Trim();
                var personaUid = parts[2].Trim();
                if (
                    !Regex.IsMatch(userUid, @"^\d{10,20}$")
                    || !Regex.IsMatch(personaUid, @"^\d{10,20}$")
                )
                    continue;

                var isProfile = string.Equals(userUid, personaUid, StringComparison.Ordinal);
                MergeOrAddAccount(
                    accounts,
                    new FacebookAccountInfo
                    {
                        Type = isProfile ? "Profile" : "Page",
                        Uid = personaUid,
                        // prefs_db exposes the Page persona UID, not delegate_page_id.
                        PageId = isProfile ? personaUid : string.Empty,
                        Name = isProfile ? activeName : string.Empty,
                        UserUid = userUid,
                        IsActive =
                            isProfile
                            || string.Equals(personaUid, activeUid, StringComparison.Ordinal),
                        AccessToken = string.Equals(personaUid, activeUid, StringComparison.Ordinal)
                            ? activeAccessToken
                            : string.Empty,
                        Cookies = string.Equals(personaUid, activeUid, StringComparison.Ordinal)
                            ? activeCookies
                            : string.Empty,
                    },
                    activeUid,
                    activeAccessToken,
                    activeCookies
                );
            }
            else if (line.StartsWith("ACCOUNT|", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split('|');
                if (parts.Length < 6)
                    continue;

                var type = parts[1].Trim();
                var uid = parts[2].Trim();
                var pageId = parts[3].Trim();
                var name = parts[4].Trim();
                var userUid = parts[5].Trim();
                var email = parts.Length > 6 ? parts[6].Trim() : string.Empty;
                var pic = parts.Length > 7 ? parts[7].Trim() : string.Empty;

                if (
                    string.IsNullOrWhiteSpace(name)
                    && string.IsNullOrWhiteSpace(uid)
                    && string.IsNullOrWhiteSpace(pageId)
                )
                    continue;

                if (string.Equals(type, "Profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(pageId))
                        pageId = uid;
                    if (string.IsNullOrWhiteSpace(userUid))
                        userUid = uid;
                }

                var acc = new FacebookAccountInfo
                {
                    Type = string.Equals(type, "Page", StringComparison.OrdinalIgnoreCase)
                        ? "Page"
                        : "Profile",
                    Uid = uid,
                    PageId = pageId,
                    Name = name,
                    UserUid = userUid,
                    Email = email,
                    PicUrl = pic,
                    IsActive = (
                        !string.IsNullOrEmpty(activeUid)
                        && (uid == activeUid || pageId == activeUid)
                    ),
                };

                if (acc.IsActive)
                {
                    acc.AccessToken = activeAccessToken;
                    acc.Cookies = activeCookies;
                }

                var existing = accounts.FirstOrDefault(a =>
                    string.Equals(a.Type, acc.Type, StringComparison.OrdinalIgnoreCase)
                    && (
                        (!string.IsNullOrEmpty(a.PageId) && a.PageId == acc.PageId)
                        || (!string.IsNullOrEmpty(a.Uid) && a.Uid == acc.Uid)
                    )
                );

                if (existing == null)
                {
                    accounts.Add(acc);
                }
                else
                {
                    if (
                        string.IsNullOrWhiteSpace(existing.Name)
                        && !string.IsNullOrWhiteSpace(acc.Name)
                    )
                        existing.Name = acc.Name;
                    if (
                        string.IsNullOrWhiteSpace(existing.Email)
                        && !string.IsNullOrWhiteSpace(acc.Email)
                    )
                        existing.Email = acc.Email;
                    if (
                        string.IsNullOrWhiteSpace(existing.PicUrl)
                        && !string.IsNullOrWhiteSpace(acc.PicUrl)
                    )
                        existing.PicUrl = acc.PicUrl;
                    if (
                        string.IsNullOrWhiteSpace(existing.PageId)
                        && !string.IsNullOrWhiteSpace(acc.PageId)
                    )
                        existing.PageId = acc.PageId;
                    if (
                        string.IsNullOrWhiteSpace(existing.UserUid)
                        && !string.IsNullOrWhiteSpace(acc.UserUid)
                    )
                        existing.UserUid = acc.UserUid;
                    if (!existing.IsActive && acc.IsActive)
                    {
                        existing.IsActive = true;
                        existing.AccessToken = activeAccessToken;
                        existing.Cookies = activeCookies;
                    }
                }
            }
            else if (line.StartsWith("DBL|", StringComparison.OrdinalIgnoreCase))
            {
                var json = line[4..].Trim();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    string dblUid = root.TryGetProperty("uid", out var u)
                        ? u.GetString() ?? ""
                        : "";
                    string dblName = root.TryGetProperty("full_name", out var fn)
                        ? fn.GetString() ?? ""
                        : "";
                    if (
                        string.IsNullOrWhiteSpace(dblName) && root.TryGetProperty("name", out var n)
                    )
                        dblName = n.GetString() ?? "";
                    string dblPic = root.TryGetProperty("pic_url", out var p)
                        ? p.GetString() ?? ""
                        : "";

                    if (!string.IsNullOrWhiteSpace(dblUid))
                    {
                        var existing = accounts.FirstOrDefault(a =>
                            a.Uid == dblUid || a.PageId == dblUid
                        );
                        if (existing == null)
                        {
                            accounts.Add(
                                new FacebookAccountInfo
                                {
                                    Type = "Profile",
                                    Uid = dblUid,
                                    PageId = dblUid,
                                    Name = dblName,
                                    UserUid = dblUid,
                                    PicUrl = dblPic,
                                    IsActive = (
                                        !string.IsNullOrEmpty(activeUid) && dblUid == activeUid
                                    ),
                                    AccessToken = (dblUid == activeUid ? activeAccessToken : ""),
                                    Cookies = (dblUid == activeUid ? activeCookies : ""),
                                }
                            );
                        }
                        else
                        {
                            if (
                                string.IsNullOrWhiteSpace(existing.PicUrl)
                                && !string.IsNullOrWhiteSpace(dblPic)
                            )
                                existing.PicUrl = dblPic;
                            if (
                                string.IsNullOrWhiteSpace(existing.Name)
                                && !string.IsNullOrWhiteSpace(dblName)
                            )
                                existing.Name = dblName;
                        }
                    }
                }
                catch { }
            }
        }

        if (
            !string.IsNullOrWhiteSpace(activeUid)
            && !accounts.Any(a => a.Uid == activeUid || a.PageId == activeUid)
        )
        {
            accounts.Insert(
                0,
                new FacebookAccountInfo
                {
                    Type = "Profile",
                    Uid = activeUid,
                    PageId = activeUid,
                    Name = activeName,
                    UserUid = activeUid,
                    IsActive = true,
                    AccessToken = activeAccessToken,
                    Cookies = activeCookies,
                }
            );
        }

        if (accounts.Count == 0 && !string.IsNullOrWhiteSpace(output))
        {
            if (
                output.Contains("underlying_account", StringComparison.OrdinalIgnoreCase)
                || output.Contains("logged_in_", StringComparison.OrdinalIgnoreCase)
                || output.Contains(
                    "auth_device_based_login_credentials",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return ParseDirectDumps(output, output, output, output);
            }
        }

        return accounts;
    }

    public static List<FacebookAccountInfo> ParseDirectDumps(
        string? authDump,
        string? loggedInDump,
        string? sharedPrefsDump,
        string? dbDump
    )
    {
        var accounts = new List<FacebookAccountInfo>();
        var cleanAuth = SanitizeBinaryDump(authDump);
        var cleanLoggedIn = SanitizeBinaryDump(loggedInDump);
        var cleanShared = sharedPrefsDump ?? string.Empty;
        var cleanDb = dbDump ?? string.Empty;

        string activeUid = string.Empty;
        string activeName = string.Empty;
        string activeAccessToken = string.Empty;
        string activeCookies = string.Empty;

        // 1. Extract from authDump (underlying_account & authentication)
        if (!string.IsNullOrWhiteSpace(cleanAuth))
        {
            var uaMatch = Regex.Match(
                cleanAuth,
                @"underlying_account_uid[\r\n\s]+(\d{10,20})",
                RegexOptions.IgnoreCase
            );
            if (uaMatch.Success)
            {
                activeUid = uaMatch.Groups[1].Value;
            }
            else
            {
                var uidMatch = Regex.Match(
                    cleanAuth,
                    @"user_id[\r\n\s]+(\d{10,20})",
                    RegexOptions.IgnoreCase
                );
                if (uidMatch.Success)
                    activeUid = uidMatch.Groups[1].Value;
                else
                {
                    var rawMatch = Regex.Match(cleanAuth, @"\b(615\d{11,13}|\d{14,16})\b");
                    if (rawMatch.Success)
                        activeUid = rawMatch.Groups[1].Value;
                }
            }

            var nameMatch = Regex.Match(
                cleanAuth,
                @"(?:username|name)[\r\n\s]+([^\r\n]{2,60})",
                RegexOptions.IgnoreCase
            );
            if (nameMatch.Success)
                activeName = nameMatch.Groups[1].Value.Trim();

            var tokenMatch = Regex.Match(
                cleanAuth,
                @"access_token[\r\n\s]+([^\r\n]{10,})",
                RegexOptions.IgnoreCase
            );
            if (!tokenMatch.Success)
                tokenMatch = Regex.Match(cleanAuth, @"\b(EAA[A-Za-z0-9_-]{10,})\b");
            if (tokenMatch.Success)
                activeAccessToken = tokenMatch.Groups[1].Value.Trim();

            var cookieMatch = Regex.Match(
                cleanAuth,
                @"(\[\s*\{.*?c_user.*?\}\s*\])",
                RegexOptions.Singleline
            );
            if (cookieMatch.Success)
                activeCookies = cookieMatch.Groups[1].Value.Trim().Replace("\\\"", "\"");
        }

        // 2. Fallback for activeUid and activeName from shared_prefs if still empty
        if (string.IsNullOrWhiteSpace(activeUid) && !string.IsNullOrWhiteSpace(cleanShared))
        {
            var spUidMatch = Regex.Match(
                cleanShared,
                @"<string name=""(?:user_id|uid)"">(\d+)</string>"
            );
            if (spUidMatch.Success)
                activeUid = spUidMatch.Groups[1].Value;
            else
            {
                var rawSpUid = Regex.Match(cleanShared, @"\b(615\d{11,13}|\d{14,16})\b");
                if (rawSpUid.Success)
                    activeUid = rawSpUid.Groups[1].Value;
            }
        }
        if (string.IsNullOrWhiteSpace(activeName) && !string.IsNullOrWhiteSpace(cleanShared))
        {
            var spNameMatch = Regex.Match(
                cleanShared,
                @"<string name=""(?:profile_name|name|username)"">([^<]+)</string>"
            );
            if (spNameMatch.Success)
                activeName = spNameMatch.Groups[1].Value.Trim();
        }

        // 3. Extract logged in profiles & pages from logged_in_* files
        if (!string.IsNullOrWhiteSpace(cleanLoggedIn))
        {
            var chunks = cleanLoggedIn.Split(["===FILE:"], StringSplitOptions.RemoveEmptyEntries);
            foreach (var chunk in chunks)
            {
                var firstLineEnd = chunk.IndexOfAny(['\r', '\n']);
                var header = firstLineEnd > 0 ? chunk[..firstLineEnd] : chunk;
                var body = firstLineEnd > 0 ? chunk[firstLineEnd..] : "";

                var fileUidMatch = Regex.Match(header, @"logged_in_(\d+)");
                if (!fileUidMatch.Success)
                    continue;
                var uid = fileUidMatch.Groups[1].Value;

                var pNameMatch = Regex.Match(
                    body,
                    @"(?:^|[\r\n])name[\r\n\s]+([^\r\n]{2,60})",
                    RegexOptions.IgnoreCase
                );
                string name = pNameMatch.Success ? pNameMatch.Groups[1].Value.Trim() : "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    var fn = Regex.Match(
                        body,
                        @"(?:^|[\r\n])first_name[\r\n\s]+([^\r\n]{1,30})",
                        RegexOptions.IgnoreCase
                    );
                    var ln = Regex.Match(
                        body,
                        @"(?:^|[\r\n])last_name[\r\n\s]+([^\r\n]{1,30})",
                        RegexOptions.IgnoreCase
                    );
                    if (fn.Success)
                        name = $"{fn.Groups[1].Value.Trim()} {ln.Groups[1].Value.Trim()}".Trim();
                }

                var pageIdMatch = Regex.Match(
                    body,
                    @"(?:delegate_page_id|page_id)[\r\n\s]+(\d{10,20})",
                    RegexOptions.IgnoreCase
                );
                string pageId = pageIdMatch.Success ? pageIdMatch.Groups[1].Value : uid;

                bool isPage =
                    body.Contains("is_page", StringComparison.OrdinalIgnoreCase)
                    || pageIdMatch.Success;
                string type = isPage ? "Page" : "Profile";

                name = ResolveRootPageName(name, body, isPage);

                var emailMatch = Regex.Match(
                    body,
                    @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}"
                );
                string email = emailMatch.Success ? emailMatch.Value : "";

                var picMatch = Regex.Match(body, @"https?://[^\s""'<>\n\r]+");
                string pic = picMatch.Success ? picMatch.Value : "";

                var userUidMatch = Regex.Match(
                    body,
                    @"(?:^|[\r\n])user_id[\r\n\s]+(\d{10,20})",
                    RegexOptions.IgnoreCase
                );
                string userUid = userUidMatch.Success ? userUidMatch.Groups[1].Value : activeUid;

                if (
                    string.IsNullOrWhiteSpace(name)
                    && string.IsNullOrWhiteSpace(uid)
                    && string.IsNullOrWhiteSpace(pageId)
                )
                    continue;

                if (string.Equals(type, "Profile", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(pageId))
                        pageId = uid;
                    if (string.IsNullOrWhiteSpace(userUid))
                        userUid = uid;
                }

                var acc = new FacebookAccountInfo
                {
                    Type = type,
                    Uid = uid,
                    PageId = pageId,
                    Name = name,
                    UserUid = userUid,
                    Email = email,
                    PicUrl = pic,
                    IsActive = (
                        !string.IsNullOrEmpty(activeUid)
                        && (uid == activeUid || pageId == activeUid)
                    ),
                };

                if (acc.IsActive)
                {
                    acc.AccessToken = activeAccessToken;
                    acc.Cookies = activeCookies;
                }

                MergeOrAddAccount(accounts, acc, activeUid, activeAccessToken, activeCookies);
            }
        }

        // 4. Extract from shared_prefs XML (DBL credentials)
        if (!string.IsNullOrWhiteSpace(cleanShared))
        {
            var unescaped = cleanShared.Replace("&quot;", "\"");
            var dblMatches = Regex.Matches(unescaped, @"\{[^{}]*""uid""\s*:\s*""(\d+)""[^{}]*\}");
            foreach (Match m in dblMatches)
            {
                try
                {
                    using var doc = JsonDocument.Parse(m.Value);
                    var root = doc.RootElement;
                    string dblUid = root.TryGetProperty("uid", out var u)
                        ? u.GetString() ?? ""
                        : "";
                    string dblName = root.TryGetProperty("full_name", out var fn)
                        ? fn.GetString() ?? ""
                        : (root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
                    string dblPic = root.TryGetProperty("pic_url", out var p)
                        ? p.GetString() ?? ""
                        : "";

                    if (!string.IsNullOrWhiteSpace(dblUid))
                    {
                        var acc = new FacebookAccountInfo
                        {
                            Type = "Profile",
                            Uid = dblUid,
                            PageId = dblUid,
                            Name = dblName,
                            UserUid = dblUid,
                            PicUrl = dblPic,
                            IsActive = (!string.IsNullOrEmpty(activeUid) && dblUid == activeUid),
                            AccessToken = (dblUid == activeUid ? activeAccessToken : ""),
                            Cookies = (dblUid == activeUid ? activeCookies : ""),
                        };
                        MergeOrAddAccount(
                            accounts,
                            acc,
                            activeUid,
                            activeAccessToken,
                            activeCookies
                        );
                    }
                }
                catch { }
            }
        }

        // 5. Extract from dbDump (sqlite output)
        if (!string.IsNullOrWhiteSpace(cleanDb))
        {
            var dbLines = cleanDb.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in dbLines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('{') && trimmed.EndsWith('}') && trimmed.Contains("\"uid\""))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(trimmed);
                        var root = doc.RootElement;
                        string dblUid = root.TryGetProperty("uid", out var u)
                            ? u.GetString() ?? ""
                            : "";
                        string dblName = root.TryGetProperty("full_name", out var fn)
                            ? fn.GetString() ?? ""
                            : (root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
                        string dblPic = root.TryGetProperty("pic_url", out var p)
                            ? p.GetString() ?? ""
                            : "";

                        if (!string.IsNullOrWhiteSpace(dblUid))
                        {
                            var acc = new FacebookAccountInfo
                            {
                                Type = "Profile",
                                Uid = dblUid,
                                PageId = dblUid,
                                Name = dblName,
                                UserUid = dblUid,
                                PicUrl = dblPic,
                                IsActive = (
                                    !string.IsNullOrEmpty(activeUid) && dblUid == activeUid
                                ),
                                AccessToken = (dblUid == activeUid ? activeAccessToken : ""),
                                Cookies = (dblUid == activeUid ? activeCookies : ""),
                            };
                            MergeOrAddAccount(
                                accounts,
                                acc,
                                activeUid,
                                activeAccessToken,
                                activeCookies
                            );
                        }
                    }
                    catch { }
                }
            }
        }

        // 6. Ensure active account is included if found
        if (
            !string.IsNullOrWhiteSpace(activeUid)
            && !accounts.Any(a => a.Uid == activeUid || a.PageId == activeUid)
        )
        {
            accounts.Insert(
                0,
                new FacebookAccountInfo
                {
                    Type = "Profile",
                    Uid = activeUid,
                    PageId = activeUid,
                    Name = activeName,
                    UserUid = activeUid,
                    IsActive = true,
                    AccessToken = activeAccessToken,
                    Cookies = activeCookies,
                }
            );
        }

        return accounts;
    }

    private static void MergeOrAddAccount(
        List<FacebookAccountInfo> accounts,
        FacebookAccountInfo acc,
        string activeUid,
        string activeAccessToken,
        string activeCookies
    )
    {
        var existing = accounts.FirstOrDefault(a =>
            string.Equals(a.Type, acc.Type, StringComparison.OrdinalIgnoreCase)
            && (
                (!string.IsNullOrEmpty(a.PageId) && a.PageId == acc.PageId)
                || (!string.IsNullOrEmpty(a.Uid) && a.Uid == acc.Uid)
            )
        );

        if (existing == null)
        {
            accounts.Add(acc);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(acc.Name))
                existing.Name = acc.Name;
            if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(acc.Email))
                existing.Email = acc.Email;
            if (
                string.IsNullOrWhiteSpace(existing.PicUrl) && !string.IsNullOrWhiteSpace(acc.PicUrl)
            )
                existing.PicUrl = acc.PicUrl;
            if (
                string.IsNullOrWhiteSpace(existing.PageId) && !string.IsNullOrWhiteSpace(acc.PageId)
            )
                existing.PageId = acc.PageId;
            if (
                string.IsNullOrWhiteSpace(existing.UserUid)
                && !string.IsNullOrWhiteSpace(acc.UserUid)
            )
                existing.UserUid = acc.UserUid;
            if (!existing.IsActive && acc.IsActive)
            {
                existing.IsActive = true;
                existing.AccessToken = activeAccessToken;
                existing.Cookies = activeCookies;
            }
        }
    }

    private static string SanitizeBinaryDump(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var chars = raw.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] < 32 && chars[i] != '\t' && chars[i] != '\n' && chars[i] != '\r')
            {
                chars[i] = '\n';
            }
        }
        return new string(chars);
    }

    public static FacebookAccountDetails ParseFacebookAccountDetails(
        string? switcherXml,
        string? personalInfoXml,
        string? profileXml,
        string? prefsDump,
        string? dbDump,
        int deviceIndex
    )
    {
        var result = new FacebookAccountDetails { DeviceName = $"LD-{deviceIndex}" };

        // 1. Parse Profile Switcher for Profile Name and Pages
        if (!string.IsNullOrWhiteSpace(switcherXml))
        {
            try
            {
                var switcherDocs = SafeParseXmlDocuments(switcherXml);
                string? detectedProfileName = null;
                var detectedPages = new List<(string Name, string Notifications)>();

                foreach (var doc in switcherDocs)
                {
                    var allNodes = doc.Descendants("node").ToList();

                    for (int i = 0; i < allNodes.Count; i++)
                    {
                        var text = allNodes[i].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(text))
                            text = allNodes[i].Attribute("content-desc")?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                            continue;

                        // Clean common suffix e.g. "Laura Lindholm, see your profile"
                        text = Regex
                            .Replace(
                                text,
                                @"(?:,\s*see your profile|\s+see your profile)$",
                                "",
                                RegexOptions.IgnoreCase
                            )
                            .Trim();

                        if (IsIgnoredSwitcherHeaderOrAction(text) || IsNotificationSubtitle(text))
                        {
                            continue;
                        }

                        // Look ahead within next 12 nodes for "Page" badge and notification count
                        bool isPage = false;
                        string notifications = string.Empty;

                        for (int j = i + 1; j < allNodes.Count && j <= i + 12; j++)
                        {
                            var nextText = allNodes[j].Attribute("text")?.Value?.Trim();
                            if (string.IsNullOrWhiteSpace(nextText))
                                nextText = allNodes[j].Attribute("content-desc")?.Value?.Trim();

                            if (string.IsNullOrWhiteSpace(nextText))
                                continue;

                            if (string.Equals(nextText, "Page", StringComparison.OrdinalIgnoreCase))
                            {
                                isPage = true;
                                continue;
                            }

                            if (IsNotificationSubtitle(nextText))
                            {
                                notifications = CleanNotificationSubtitle(nextText);
                                continue;
                            }

                            if (IsIgnoredSwitcherHeaderOrAction(nextText))
                            {
                                break;
                            }

                            break;
                        }

                        var isChecked =
                            string.Equals(
                                allNodes[i].Attribute("checked")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            )
                            || string.Equals(
                                allNodes[i].Attribute("selected")?.Value,
                                "true",
                                StringComparison.OrdinalIgnoreCase
                            );

                        if (isPage)
                        {
                            if (
                                !detectedPages.Any(p =>
                                    p.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
                                )
                            )
                            {
                                detectedPages.Add((text, notifications));
                            }
                        }
                        else
                        {
                            if (detectedProfileName == null || isChecked)
                            {
                                detectedProfileName = text;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(detectedProfileName))
                {
                    detectedPages.RemoveAll(p =>
                        p.Name.Equals(detectedProfileName, StringComparison.OrdinalIgnoreCase)
                    );
                    result.Name = detectedProfileName;
                }

                int pIdx = 1;
                foreach (var (pageName, notifs) in detectedPages)
                {
                    result.Pages.Add(
                        new FacebookPageInfo
                        {
                            PageId = $"6159{deviceIndex:D2}{pIdx:D2}890",
                            PageName = pageName,
                            Type = "Page",
                            Notifications = notifs,
                            ManagedBy = result.Name,
                        }
                    );
                    pIdx++;
                }
            }
            catch { }
        }

        var combinedText = $"{prefsDump}\n{dbDump}\n{personalInfoXml}\n{profileXml}";

        // 2. Extract verified Profile Name
        // A) Check Profile Settings link: "Copy Laura Lindholm's link"
        var copyLinkNameMatch = Regex.Match(
            combinedText,
            @"(?:Copy\s+)?([A-Za-z0-9\s.'-]{2,40})'s\s+link",
            RegexOptions.IgnoreCase
        );
        if (copyLinkNameMatch.Success)
        {
            var candName = copyLinkNameMatch.Groups[1].Value.Trim();
            if (
                !string.IsNullOrWhiteSpace(candName)
                && !candName.Contains("facebook", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (
                    string.IsNullOrWhiteSpace(result.Name)
                    || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase)
                    || result.Name.Contains("profile", StringComparison.OrdinalIgnoreCase)
                )
                {
                    result.Name = candName;
                }
            }
        }

        // B) Fallback name from profileXml if not found in switcher or settings
        if (
            (
                string.IsNullOrWhiteSpace(result.Name)
                || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase)
            ) && !string.IsNullOrWhiteSpace(profileXml)
        )
        {
            try
            {
                var profDocs = SafeParseXmlDocuments(profileXml);
                foreach (var doc in profDocs)
                {
                    var title = doc.Descendants("node")
                        .Select(n =>
                        {
                            var text = n.Attribute("text")?.Value?.Trim();
                            if (string.IsNullOrWhiteSpace(text))
                                text = n.Attribute("content-desc")?.Value?.Trim();
                            return text;
                        })
                        .OfType<string>()
                        .FirstOrDefault(t => !IsIgnoredProfileText(t));

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        result.Name = title;
                        break;
                    }
                }
            }
            catch { }
        }

        // C) Check prefsDump for user's real name if still empty or fallback
        if (
            string.IsNullOrWhiteSpace(result.Name)
            || result.Name.StartsWith("Facebook User", StringComparison.OrdinalIgnoreCase)
        )
        {
            var nameMatch = Regex.Match(
                prefsDump ?? string.Empty,
                @"(?:""name""|""userName""|""user_name"")\s*:\s*""([^""]{2,50})"""
            );
            if (nameMatch.Success)
            {
                var cand = nameMatch.Groups[1].Value.Trim();
                if (
                    !cand.Contains("facebook", StringComparison.OrdinalIgnoreCase)
                    && !cand.Contains("@")
                )
                {
                    result.Name = cand;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            result.Name = $"Facebook User (LD-{deviceIndex})";
        }

        // 3. Parse UID from profile.php URL / prefsDump / dbDump / XML
        var profilePhpUid = Regex.Match(
            combinedText,
            @"profile\.php\?id=([0-9]{10,16})",
            RegexOptions.IgnoreCase
        );
        if (profilePhpUid.Success)
        {
            result.Uid = profilePhpUid.Groups[1].Value;
        }
        else
        {
            var uidMatch = Regex.Match(
                combinedText,
                @"(?:user_id|userId|uid)[""':\s=]+([0-9]{10,16})"
            );
            if (!uidMatch.Success)
            {
                uidMatch = Regex.Match(combinedText, @"\b(615\d{11,13}|1000\d{11,13}|\d{14,16})\b");
            }
            result.Uid = uidMatch.Success
                ? (uidMatch.Groups[1].Success ? uidMatch.Groups[1].Value : uidMatch.Value)
                : $"6159{deviceIndex:D2}000{DateTime.Now.Year % 100}1";
        }

        // 4. Parse Email (exclusively from Facebook UI Contact info, Bloks Accounts Center, or Facebook app storage - NOT from Android AccountManager / dumpsys)
        var contactDocs = SafeParseXmlDocuments($"{personalInfoXml}\n{profileXml}");
        foreach (var doc in contactDocs)
        {
            var nodes = doc.Descendants("node").ToList();
            for (int i = 0; i < nodes.Count - 1; i++)
            {
                var t = nodes[i].Attribute("text")?.Value?.Trim();
                if (
                    t != null
                    && (
                        t.Equals("Email", StringComparison.OrdinalIgnoreCase)
                        || t.Equals("Contact info", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    var val = nodes[i + 1].Attribute("text")?.Value?.Trim();
                    if (
                        !string.IsNullOrWhiteSpace(val)
                        && Regex.IsMatch(val, @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")
                        && !IsInternalMetaEmail(val)
                    )
                    {
                        result.Email = val;
                        break;
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(result.Email))
                break;
        }

        if (string.IsNullOrWhiteSpace(result.Email))
        {
            // Parse from Facebook's app shared preferences or databases
            var emailMatches = Regex.Matches(
                combinedText,
                @"\b([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})\b"
            );
            foreach (Match m in emailMatches)
            {
                var cand = m.Groups[1].Value;
                if (!IsInternalMetaEmail(cand))
                {
                    result.Email = cand;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.Email))
        {
            result.Email = "Private / Hidden";
        }

        // 5. Parse Birthdate
        var bdayPrefixedMatch = Regex.Match(
            combinedText,
            @"Birthday[,\s:]+([A-Za-z]+\s+\d{1,2},\s+\d{4})",
            RegexOptions.IgnoreCase
        );
        if (bdayPrefixedMatch.Success)
        {
            result.Birthdate = bdayPrefixedMatch.Groups[1].Value.Trim();
        }
        else
        {
            var birthMatch = Regex.Match(
                combinedText,
                @"\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}\b",
                RegexOptions.IgnoreCase
            );
            if (birthMatch.Success)
            {
                result.Birthdate = birthMatch.Value.Trim();
            }
            else
            {
                var birthAltMatch = Regex.Match(
                    combinedText,
                    @"\b\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}\b",
                    RegexOptions.IgnoreCase
                );
                if (birthAltMatch.Success)
                {
                    result.Birthdate = birthAltMatch.Value.Trim();
                }
                else
                {
                    var birthIsoMatch = Regex.Match(
                        combinedText,
                        @"\b(19\d{2}|200\d)[-/](0[1-9]|1[0-2])[-/](0[1-9]|[12]\d|3[01])\b"
                    );
                    if (
                        birthIsoMatch.Success && DateTime.TryParse(birthIsoMatch.Value, out var bdt)
                    )
                    {
                        result.Birthdate = bdt.ToString("MMMM dd, yyyy");
                    }
                    else
                    {
                        var birthDocs = SafeParseXmlDocuments($"{personalInfoXml}\n{profileXml}");
                        foreach (var doc in birthDocs)
                        {
                            var nodes = doc.Descendants("node").ToList();
                            for (int i = 0; i < nodes.Count - 1; i++)
                            {
                                var t = nodes[i].Attribute("text")?.Value?.Trim();
                                if (
                                    t != null
                                    && (
                                        t.Equals("Birth date", StringComparison.OrdinalIgnoreCase)
                                        || t.Equals(
                                            "Date of birth",
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                        || t.Equals("Birthday", StringComparison.OrdinalIgnoreCase)
                                    )
                                )
                                {
                                    var val = nodes[i + 1].Attribute("text")?.Value?.Trim();
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        result.Birthdate = val;
                                        break;
                                    }
                                }
                            }
                            if (!string.IsNullOrWhiteSpace(result.Birthdate))
                                break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(result.Birthdate))
                    {
                        result.Birthdate = "Private / Hidden";
                    }
                }
            }
        }

        // 6. Parse Creation Date
        var joinedMatch = Regex.Match(
            combinedText,
            @"(Joined\s+(?:Facebook\s+(?:in\s+)?)?[A-Za-z]+\s+\d{4}|Account created:\s*([A-Za-z0-9, ]+)|Member since\s+[A-Za-z]+\s+\d{4}|Joined\s+\d{4})",
            RegexOptions.IgnoreCase
        );
        if (joinedMatch.Success)
        {
            result.CreationDate = joinedMatch.Value;
        }
        else if (!string.IsNullOrWhiteSpace(profileXml))
        {
            var profDocs = SafeParseXmlDocuments(profileXml);
            foreach (var doc in profDocs)
            {
                var joinedNode = doc.Descendants("node")
                    .Select(n => n.Attribute("text")?.Value?.Trim())
                    .OfType<string>()
                    .FirstOrDefault(t =>
                        t.StartsWith("Joined ", StringComparison.OrdinalIgnoreCase)
                    );
                if (!string.IsNullOrWhiteSpace(joinedNode))
                {
                    result.CreationDate = joinedNode;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(result.CreationDate))
        {
            result.CreationDate = "Not Set";
        }

        // 7. Parse Personal Details (Location, Hometown, Work, Education, RelationshipStatus)
        var locMatch = Regex.Match(
            combinedText,
            @"(?:Location,\s*|Lives in\s+)([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
            RegexOptions.IgnoreCase
        );
        if (locMatch.Success)
        {
            result.Location = locMatch.Groups[1].Value.Trim();
        }

        var homeMatch = Regex.Match(
            combinedText,
            @"(?:Hometown,\s*|From\s+)([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
            RegexOptions.IgnoreCase
        );
        if (homeMatch.Success)
        {
            result.Hometown = homeMatch.Groups[1].Value.Trim();
        }

        var relMatch = Regex.Match(
            combinedText,
            @"(?:Status,\s*|Relationship status,\s*)([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
            RegexOptions.IgnoreCase
        );
        if (relMatch.Success)
        {
            result.RelationshipStatus = relMatch.Groups[1].Value.Trim();
        }

        // Work & Education via node inspection with regex fallback
        var detailDocs = SafeParseXmlDocuments($"{personalInfoXml}\n{profileXml}");
        foreach (var doc in detailDocs)
        {
            var allNodes = doc.Descendants("node").ToList();
            for (int i = 0; i < allNodes.Count; i++)
            {
                var text = allNodes[i].Attribute("text")?.Value?.Trim();
                var desc = allNodes[i].Attribute("content-desc")?.Value?.Trim();

                if (
                    string.IsNullOrEmpty(result.Work)
                    && (
                        string.Equals(text, "Work", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(desc, "Work", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    for (int j = i + 1; j < allNodes.Count && j <= i + 6; j++)
                    {
                        var val = allNodes[j].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(val))
                            val = allNodes[j].Attribute("content-desc")?.Value?.Trim();
                        if (
                            !string.IsNullOrWhiteSpace(val)
                            && !val.Equals("Work", StringComparison.OrdinalIgnoreCase)
                            && !val.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            result.Work = val;
                            break;
                        }
                    }
                }

                if (
                    string.IsNullOrEmpty(result.Education)
                    && (
                        string.Equals(text, "Education", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(desc, "Education", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    for (int j = i + 1; j < allNodes.Count && j <= i + 6; j++)
                    {
                        var val = allNodes[j].Attribute("text")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(val))
                            val = allNodes[j].Attribute("content-desc")?.Value?.Trim();
                        if (
                            !string.IsNullOrWhiteSpace(val)
                            && !val.Equals("Education", StringComparison.OrdinalIgnoreCase)
                            && !val.StartsWith("Edit", StringComparison.OrdinalIgnoreCase)
                            && !val.StartsWith("See more", StringComparison.OrdinalIgnoreCase)
                            && !FacebookNavigationLabels.IsSectionExpansion(val)
                        )
                        {
                            result.Education = val;
                            break;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(result.Work))
        {
            var workMatch = Regex.Match(
                combinedText,
                @"(?:Works at|Work)\s*[:,\n]\s*([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
                RegexOptions.IgnoreCase
            );
            if (
                workMatch.Success
                && !workMatch
                    .Groups[1]
                    .Value.Equals("Edit Work", StringComparison.OrdinalIgnoreCase)
            )
            {
                result.Work = workMatch.Groups[1].Value.Trim();
            }
        }

        if (string.IsNullOrEmpty(result.Education))
        {
            var eduMatch = Regex.Match(
                combinedText,
                @"(?:Went to|Studied at|Education)\s*[:,\n]\s*([A-Za-z0-9\s,.-]+?)(?=\r|\n|bounds|""|'|<|$)",
                RegexOptions.IgnoreCase
            );
            if (
                eduMatch.Success
                && !eduMatch
                    .Groups[1]
                    .Value.Equals("Edit Education", StringComparison.OrdinalIgnoreCase)
            )
            {
                result.Education = eduMatch.Groups[1].Value.Trim();
            }
        }

        // Associate UID and account info with pages
        foreach (var page in result.Pages)
        {
            page.UserUid = result.Uid;
            page.Email = result.Email;
            page.Birthdate = result.Birthdate;
            page.CreationDate = result.CreationDate;
            page.Location = result.Location;
            page.Hometown = result.Hometown;
            page.Work = result.Work;
            page.Education = result.Education;
            page.RelationshipStatus = result.RelationshipStatus;
            if (string.IsNullOrWhiteSpace(page.ManagedBy))
            {
                page.ManagedBy = result.Name;
            }
        }

        return result;
    }

    private static (int X, int Y)? FindProfileSwitcherCoordinates(string? menuXml)
    {
        return PFlowPersonaSwitcherService.FindQuickSwitchAvatarInMenu(menuXml, "");
    }

    public static bool IsIgnoredSwitcherHeaderOrAction(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return true;

        var t = text.Trim();
        return t.Equals("Menu", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Your shortcuts", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All shortcuts", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Create Facebook Page", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Create Page", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Go to Accounts Center", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Accounts Center", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Go to Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Meta Account settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Open profile switcher", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
            || FacebookNavigationLabels.IsSectionExpansion(t)
            || t.Equals("See less", StringComparison.OrdinalIgnoreCase)
            || t.Equals("See all profiles", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Log into another account", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Log in to another account", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Create new profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("See your profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Settings & privacy", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Help & support", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Page", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Meta", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Search", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Switch Profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Switch profile", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIgnoredProfileText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return true;

        var t = text.Trim();
        return t.Equals("Facebook", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Search", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Navigate up", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Back", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Menu", StringComparison.OrdinalIgnoreCase)
            || t.Equals("All", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Photos", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Reels", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Posts", StringComparison.OrdinalIgnoreCase)
            || t.Equals("About", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Personal details", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Work", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Education", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Location", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Hometown", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Birthday", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Friends", StringComparison.OrdinalIgnoreCase)
            || t.Equals("More profile settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile settings", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Your profile link", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Your personalized link on Facebook.", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Copy link", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Share a note…", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Share a note...", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Cover photo", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Profile picture", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit profile picture", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit cover photo", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit profile", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit Personal details", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit Work", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Edit Education", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Add to story", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Manage", StringComparison.OrdinalIgnoreCase)
            || t.Equals("View tools", StringComparison.OrdinalIgnoreCase)
            || t.Contains("followers", StringComparison.OrdinalIgnoreCase)
            || t.Contains("friends", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("See ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Joined ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Create note", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Edit ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Copy ", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Birthday,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Location,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Hometown,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Relationship status,", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Status,", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNotificationSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var trimmed = text.Trim().TrimStart('•', ' ', '\t', '-', '·');
        return Regex.IsMatch(trimmed, @"^\d+\+?\s+notification", RegexOptions.IgnoreCase);
    }

    private static string CleanNotificationSubtitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return text.Trim().TrimStart('•', ' ', '\t', '-', '·').Trim();
    }

    private static bool IsInternalMetaEmail(string email)
    {
        return email.EndsWith("@facebook.com", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@meta.com", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@tfbnw.net", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@fb.com", StringComparison.OrdinalIgnoreCase)
            || email.EndsWith("@sample.com", StringComparison.OrdinalIgnoreCase);
    }

    public static int ParseCountString(string? countStr)
    {
        if (string.IsNullOrWhiteSpace(countStr))
            return 0;

        var cleaned = countStr.Trim().Replace(",", "");
        var m = Regex.Match(cleaned, @"^([\d.]+)\s*([KMBkmb])?", RegexOptions.IgnoreCase);
        if (!m.Success)
            return 0;

        if (!double.TryParse(m.Groups[1].Value, out var num))
            return 0;

        var suffix = m.Groups[2].Value.ToUpperInvariant();
        return suffix switch
        {
            "K" => (int)(num * 1_000),
            "M" => (int)(num * 1_000_000),
            "B" => (int)(num * 1_000_000_000),
            _ => (int)num,
        };
    }

    private static XElement? FindNodeByContentDesc(string? xml, string keyword)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var docs = SafeParseXmlDocuments(xml);
        foreach (var doc in docs)
        {
            var node = doc.Descendants("node")
                .FirstOrDefault(n =>
                {
                    var desc = n.Attribute("content-desc")?.Value;
                    return !string.IsNullOrWhiteSpace(desc)
                        && desc.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                });
            if (node != null)
                return node;
        }

        return null;
    }

    private static List<XDocument> SafeParseXmlDocuments(string? raw)
    {
        var docs = new List<XDocument>();
        if (string.IsNullOrWhiteSpace(raw))
            return docs;

        var matches = Regex.Matches(
            raw,
            @"<hierarchy[\s\S]*?<\/hierarchy>",
            RegexOptions.IgnoreCase
        );
        if (matches.Count > 0)
        {
            foreach (Match m in matches)
            {
                try
                {
                    docs.Add(XDocument.Parse(m.Value));
                }
                catch { }
            }
        }
        else
        {
            try
            {
                docs.Add(XDocument.Parse(raw));
            }
            catch { }
        }

        return docs;
    }
}
