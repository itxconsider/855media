using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public interface IPFlowPersonaSwitcherService
{
    Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Switches to the target persona represented by a FacebookPageTarget.
    /// Uses the native identity sheet, current navigation bounds and own-profile confirmation.
    /// Feed can reveal hidden navigation; Reels uses visible navigation. Navigation failure
    /// permits one Facebook restart without clearing app data or cache.
    /// </summary>
    Task<bool> SwitchToPersonaAsync(
        int index,
        FacebookPageTarget target,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Selects a Page through the shared native switcher when pageId is supplied,
    /// otherwise selects the personal profile. A public deep link does not prove identity.
    /// </summary>
    Task<bool> SwitchToPersonaAsync(
        int index,
        string personaName,
        string? pageId,
        string? customPath = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Selects a Facebook Page through the shared native switcher and confirms its own profile.
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
