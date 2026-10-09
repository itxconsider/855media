using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Domain service interface for Facebook Bloks Native Shell interactions,
/// Meta Accounts Center automation, Bloks screen extraction, and element targeting.
/// </summary>
public interface IFacebookBloksService
{
    Task<bool> IsBloksNativeShellActiveAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenBloksNativeShellAsync(
        int index,
        string bloksAppId,
        Dictionary<string, string>? parameters = null,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenBloksAccountsCenterAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> OpenBloksProfileSwitcherAsync(
        int index,
        string packageName = "com.facebook.katana",
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<BloksScreenInfo?> DumpBloksScreenAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ClickBloksElementAsync(
        int index,
        string textOrDesc,
        bool partialMatch = true,
        bool caseInsensitive = true,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<string?> ExtractBloksFieldValueAsync(
        int index,
        string fieldLabel,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<FacebookPageInfo?> ExtractBloksPageInfoAsync(
        int index,
        string pageUid,
        string? pageId = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    Task<BloksFacebookAccountInfo?> ExtractBloksAccountInfoAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );
}
