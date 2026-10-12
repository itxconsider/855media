using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Clean Architecture implementation of Facebook Bloks Native Shell interactions,
/// Meta Accounts Center automation, Bloks screen extraction, and field extraction.
/// </summary>
public class PFlowBloksService : IPFlowBloksService
{
    private readonly ILDPlayerService _ldPlayer;
    private readonly AtxAgentClient? _atxClient;

    public PFlowBloksService(ILDPlayerService ldPlayer, AtxAgentClient? atxClient = null)
    {
        _ldPlayer = ldPlayer ?? throw new ArgumentNullException(nameof(ldPlayer));
        _atxClient = atxClient;
    }

    public async Task<bool> IsBloksNativeShellActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.IsBloksNativeShellActiveAsync(index, cancellationToken);
        }

        var focus = await _ldPlayer.GetCurrentFocusedActivityAsync(
            index,
            customPath,
            cancellationToken
        );
        if (BloksConstants.IsKnownBloksActivity(focus))
            return true;

        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        var screen = AtxAgentClient.ParseBloksHierarchy(xml, focus);
        return screen.IsBloksActive;
    }

    public async Task<bool> OpenBloksNativeShellAsync(
        int index,
        string bloksAppId,
        Dictionary<string, string>? parameters = null,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.OpenBloksNativeShellAsync(
                index,
                bloksAppId,
                parameters,
                packageName,
                cancellationToken
            );
        }

        var uri = BloksConstants.CreateBloksScreenQueryUri(bloksAppId, parameters);
        return await _ldPlayer.OpenDeepLinkAsync(
            index,
            uri,
            packageName,
            customPath,
            cancellationToken
        );
    }

    public async Task<bool> OpenBloksAccountsCenterAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.OpenBloksAccountsCenterAsync(
                index,
                packageName,
                cancellationToken
            );
        }

        var ok = await _ldPlayer.OpenDeepLinkAsync(
            index,
            BloksConstants.AccountsCenterDeepLink,
            packageName,
            customPath,
            cancellationToken
        );

        if (!ok)
        {
            ok = await OpenBloksNativeShellAsync(
                index,
                BloksConstants.AccountsCenterAppId,
                null,
                packageName,
                customPath,
                cancellationToken
            );
        }

        return ok;
    }

    public async Task<bool> OpenBloksProfileSwitcherAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.OpenBloksProfileSwitcherAsync(
                index,
                packageName,
                cancellationToken
            );
        }

        return await _ldPlayer.OpenDeepLinkAsync(
            index,
            BloksConstants.SwitchAccountDeepLink,
            packageName,
            customPath,
            cancellationToken
        );
    }

    public async Task<BloksScreenInfo?> DumpBloksScreenAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.DumpBloksScreenAsync(index, cancellationToken);
        }

        var xml = await _ldPlayer.DumpUiHierarchyAsync(index, customPath, cancellationToken);
        var focus = await _ldPlayer.GetCurrentFocusedActivityAsync(
            index,
            customPath,
            cancellationToken
        );
        return AtxAgentClient.ParseBloksHierarchy(xml, focus);
    }

    public async Task<bool> ClickBloksElementAsync(
        int index,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ClickBloksElementAsync(
                index,
                textOrDesc,
                partialMatch,
                caseInsensitive,
                cancellationToken
            );
        }

        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        var elem = screen?.FindElement(textOrDesc, partialMatch, caseInsensitive);
        if (elem != null && elem.CenterX > 0 && elem.CenterY > 0)
        {
            return await _ldPlayer.TapAsync(
                index,
                elem.CenterX,
                elem.CenterY,
                customPath,
                cancellationToken
            );
        }

        return false;
    }

    public async Task<FacebookPageInfo?> ExtractBloksPageInfoAsync(
        int index,
        string pageUid,
        string? pageId = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(pageUid))
            return null;

        // Keep the Page persona UID and delegate_page_id distinct. Newer prefs_db
        // records expose only the persona UID; only send the delegate ID when root
        // storage actually provided one. Pages Manager's screen query uses these
        // server parameters to resolve the Page identity.
        var parameters = new Dictionary<string, string> { ["page_uid"] = pageUid };
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            parameters["page_id"] = pageId;
            parameters["delegate_page_id"] = pageId;
        }
        if (
            !await OpenBloksNativeShellAsync(
                index,
                BloksConstants.PagesManagerAppId,
                parameters,
                BloksConstants.KatanaPackage,
                customPath,
                cancellationToken
            )
        )
            return null;

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (screen == null || string.IsNullOrWhiteSpace(screen.RawXml) || !screen.IsBloksActive)
            return null;

        var info = PFlowAccountDiscoveryService.ParsePageInfoFromXml(
            screen.RawXml,
            !string.IsNullOrWhiteSpace(pageId) ? pageId : string.Empty
        );
        // Opening the generic Bloks host is not proof that the Pages Manager query
        // resolved. Only accept the query when its hierarchy yielded a real field;
        // otherwise the caller keeps the root-shell record and continues UI lookup.
        if (
            string.IsNullOrWhiteSpace(info.CreationDate)
            && info.Followers <= 0
            && string.IsNullOrWhiteSpace(info.Reach)
            && info.ReelsCount <= 0
            && info.VideosCount <= 0
        )
        {
            return null;
        }

        info.PersonaUid = pageUid;
        return info;
    }

    public async Task<string?> ExtractBloksFieldValueAsync(
        int index,
        string fieldLabel,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ExtractBloksFieldValueAsync(
                index,
                fieldLabel,
                cancellationToken
            );
        }

        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        return screen?.ExtractFieldValue(fieldLabel);
    }

    public async Task<BloksFacebookAccountInfo?> ExtractBloksAccountInfoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (_atxClient != null && await _atxClient.IsRunningAsync(index, cancellationToken))
        {
            return await _atxClient.ExtractBloksAccountInfoAsync(index, cancellationToken);
        }

        var screen = await DumpBloksScreenAsync(index, customPath, cancellationToken);
        if (screen == null)
            return null;

        var info = new BloksFacebookAccountInfo
        {
            AccountsCenterLoaded =
                screen.IsBloksActive
                || screen.VisibleTexts.Any(t =>
                    t.Contains("Accounts Center", StringComparison.OrdinalIgnoreCase)
                ),
        };

        var emailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        foreach (var text in screen.VisibleTexts)
        {
            var match = emailRegex.Match(text);
            if (match.Success)
            {
                info.Email = match.Value;
                break;
            }
        }

        if (string.IsNullOrEmpty(info.Email))
        {
            var contactVal =
                screen.ExtractFieldValue("Contact info") ?? screen.ExtractFieldValue("Email");
            if (!string.IsNullOrEmpty(contactVal))
            {
                var match = emailRegex.Match(contactVal);
                if (match.Success)
                    info.Email = match.Value;
            }
        }

        var birthdayVal =
            screen.ExtractFieldValue("Birthday") ?? screen.ExtractFieldValue("Date of birth");
        if (!string.IsNullOrEmpty(birthdayVal))
        {
            info.Birthday = birthdayVal;
        }

        var profileElem =
            screen.FindElement("Profiles", partialMatch: false)
            ?? screen.FindElement("Accounts", partialMatch: false);
        if (profileElem != null)
        {
            var profileVal = screen.ExtractFieldValue("Profiles");
            if (!string.IsNullOrEmpty(profileVal))
            {
                info.Name = profileVal;
                info.Profiles.Add(profileVal);
            }
        }

        var uidRegex = new Regex(@"\b([0-9]{14,16})\b");
        foreach (var text in screen.VisibleTexts)
        {
            var match = uidRegex.Match(text);
            if (match.Success)
            {
                info.UserId = match.Groups[1].Value;
                break;
            }
        }

        return info;
    }
}
