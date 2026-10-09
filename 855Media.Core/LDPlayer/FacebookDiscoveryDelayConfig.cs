using System;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Configurable timing and delay thresholds (in milliseconds) used during Facebook account discovery,
/// page info extraction, persona switching, and UI hierarchy navigation on LDPlayer instances.
/// </summary>
public class FacebookDiscoveryDelayConfig
{
    /// <summary>
    /// Delay after launching Facebook application (com.facebook.katana) in foreground (ms).
    /// Default: 2500ms.
    /// </summary>
    public int DelayOpenAppMs { get; set; } = 2500;

    /// <summary>
    /// Delay after launching Facebook app during restart/relaunch (ms).
    /// Default: 3000ms.
    /// </summary>
    public int DelayRelaunchAppMs { get; set; } = 3000;

    /// <summary>
    /// Delay after deep linking to Facebook Page or general page navigation (ms).
    /// Default: 3500ms.
    /// </summary>
    public int DelayPageLoadMs { get; set; } = 3500;

    /// <summary>
    /// Delay after deep linking to User Profile (fb://profile) (ms).
    /// Default: 2500ms.
    /// </summary>
    public int DelayProfileNavMs { get; set; } = 2500;

    /// <summary>
    /// Delay after deep linking to Pages list (fb://pages) (ms).
    /// Default: 3000ms.
    /// </summary>
    public int DelayPagesNavMs { get; set; } = 3000;

    /// <summary>
    /// Delay after deep linking to Menu (fb://menu) (ms).
    /// Default: 2000ms.
    /// </summary>
    public int DelayMenuNavMs { get; set; } = 2000;

    /// <summary>
    /// Delay after tapping profile switcher or persona target in switcher sheet (ms).
    /// Default: 3500ms.
    /// </summary>
    public int DelaySwitchPersonaMs { get; set; } = 3500;

    /// <summary>
    /// Delay after UI tap actions (e.g. settings, about info, switcher chevron) (ms).
    /// Default: 1500ms.
    /// </summary>
    public int DelayAfterTapMs { get; set; } = 1500;

    /// <summary>
    /// Delay after swipe or scroll gesture (ms).
    /// Default: 1500ms.
    /// </summary>
    public int DelayAfterSwipeMs { get; set; } = 1500;

    /// <summary>
    /// Delay after hardware back button press (ms).
    /// Default: 1000ms.
    /// </summary>
    public int DelayAfterBackMs { get; set; } = 1000;

    /// <summary>
    /// Delay between app lifecycle steps (e.g. stop app, clear cache) (ms).
    /// Default: 1000ms.
    /// </summary>
    public int DelayAppLifecycleStepMs { get; set; } = 1000;

    /// <summary>
    /// Adapts timing values from an AutoPostConfig instance (seconds to milliseconds).
    /// </summary>
    public static FacebookDiscoveryDelayConfig FromAutoPostConfig(AutoPostConfig? config)
    {
        if (config == null)
            return new FacebookDiscoveryDelayConfig();

        var openAppMs = config.DelayOpenApp > 0 ? config.DelayOpenApp * 1000 : 2500;
        var switchPageMs =
            config.DelayBeforeSwitchPage > 0 ? config.DelayBeforeSwitchPage * 1000 : 3500;

        return new FacebookDiscoveryDelayConfig
        {
            DelayOpenAppMs = openAppMs,
            DelayRelaunchAppMs = Math.Max(openAppMs, 3000),
            DelaySwitchPersonaMs = switchPageMs,
            DelayPageLoadMs = Math.Max(switchPageMs, 3500),
            DelayProfileNavMs = Math.Min(openAppMs, 3000),
            DelayPagesNavMs = Math.Min(openAppMs, 3000),
            DelayMenuNavMs = Math.Min(openAppMs, 2500),
            DelayAfterTapMs = 1500,
            DelayAfterSwipeMs = 1500,
            DelayAfterBackMs = 1000,
            DelayAppLifecycleStepMs = 1000,
        };
    }

    /// <summary>
    /// Adapts timing values from an LDSettings instance (seconds to milliseconds).
    /// </summary>
    public static FacebookDiscoveryDelayConfig FromLdSettings(LDSettings? settings)
    {
        if (settings == null)
            return new FacebookDiscoveryDelayConfig();

        var openAppMs = settings.DelayOpenApp > 0 ? settings.DelayOpenApp * 1000 : 2500;
        var switchPageMs =
            settings.DelayBeforeSwitchPage > 0 ? settings.DelayBeforeSwitchPage * 1000 : 3500;

        return new FacebookDiscoveryDelayConfig
        {
            DelayOpenAppMs = openAppMs,
            DelayRelaunchAppMs = Math.Max(openAppMs, 3000),
            DelaySwitchPersonaMs = switchPageMs,
            DelayPageLoadMs = Math.Max(switchPageMs, 3500),
            DelayProfileNavMs = Math.Min(openAppMs, 3000),
            DelayPagesNavMs = Math.Min(openAppMs, 3000),
            DelayMenuNavMs = Math.Min(openAppMs, 2500),
            DelayAfterTapMs = 1500,
            DelayAfterSwipeMs = 1500,
            DelayAfterBackMs = 1000,
            DelayAppLifecycleStepMs = 1000,
        };
    }
}
