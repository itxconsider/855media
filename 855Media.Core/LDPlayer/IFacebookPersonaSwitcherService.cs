using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public interface IFacebookPersonaSwitcherService
{
    Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Switches to the target persona represented by a FacebookPageTarget.
    /// If target is a Page, utilizes Tier 1 deep-link switching with its PageId/PersonaUid.
    /// If target is a Profile, routes to EnsureSwitchedToPersonalProfileAsync.
    /// </summary>
    Task<bool> SwitchToPersonaAsync(
        int index,
        FacebookPageTarget target,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Switches to a persona by name, optionally specifying pageId to enable Tier 1 deep-link switching.
    /// </summary>
    Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Direct deep-link switch pipeline to manage a Facebook Page by its numeric PageId,
    /// bypassing navigation bar, menu, and switcher sheet variations.
    /// </summary>
    Task<bool> SwitchToPageByIdAsync(
        int index,
        string pageName,
        string pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Multi-tiered profile switch sequence to reliably return to personal profile.
    /// </summary>
    Task<bool> EnsureSwitchedToPersonalProfileAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Finds and clicks the Facebook Menu / Profile tab on the navigation bar.
    /// </summary>
    Task<bool> OpenFacebookMenuFromFeedAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Clicks the dropdown profile list button (chevron / options button on the active profile card).
    /// </summary>
    Task<bool> ClickProfileSwitcherDropdownAsync(
        int index,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Queries the Bloks / UI hierarchy of the switcher bottom sheet to find the personal profile account.
    /// </summary>
    Task<(int X, int Y)?> QueryPersonalProfileInSwitcherAsync(
        int index,
        string? targetProfileName = null,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );
}
