using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using _855Media.Core.Dubbing;
using _855Media.ViewModels.Components;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.WebView.Windows.Core;
using Microsoft.Web.WebView2.Core;
using WebViewCore.Events;

namespace _855Media.Views.Components;

public partial class DubbingView : UserControl
{
    private CoreWebView2? _coreWebView2;
    private CoreWebView2Controller? _coreWebView2Controller;
    private DubbingViewModel? _boundViewModel;

    public DubbingView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UpdatePlayerVisibility(false);
        AttachedToVisualTree += (_, _) => UpdatePlayerVisibility(true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void OnSegmentsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dg && DataContext is DubbingViewModel vm)
        {
            vm.UpdateDataGridSelection(dg.SelectedItems.OfType<SubtitleSegment>());
        }
    }

#pragma warning disable CS0618
    private void DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not DubbingViewModel vm)
            return;

        if (e.Data.GetFiles() is { } files)
        {
            var paths = files
                .Select(f => f.TryGetLocalPath() ?? f.Path.ToString())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            if (paths.Length > 0)
            {
                await vm.HandleDroppedFilesAsync(paths);
                e.Handled = true;
            }
        }
    }
#pragma warning restore CS0618

    private void StudioMediaPlayer_OnWebViewCreated(object? sender, WebViewCreatedEventArgs args)
    {
        var platformWebView = StudioMediaPlayer.PlatformWebView as WebView2Core;
        _coreWebView2 = platformWebView?.CoreWebView2;
        if (_coreWebView2 is not null)
        {
            _coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _coreWebView2.Settings.AreDevToolsEnabled = false;
            _coreWebView2.Settings.IsStatusBarEnabled = false;
            _coreWebView2.WebMessageReceived += CoreWebView2_OnWebMessageReceived;
        }
    }

    private void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e
    )
    {
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message) || _boundViewModel is null)
            return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(message);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

            if (type == "timeupdate" && root.TryGetProperty("currentTime", out var ctProp))
            {
                var ct = ctProp.GetDouble();
                var dur = root.TryGetProperty("duration", out var durProp)
                    ? durProp.GetDouble()
                    : 0.0;
                var paused = root.TryGetProperty("paused", out var pProp) && pProp.GetBoolean();

                Dispatcher.UIThread.Post(() =>
                {
                    _boundViewModel?.UpdatePlayheadFromPlayer(ct, dur, paused);
                });
            }
        }
        catch { }
    }

    private CoreWebView2Controller? GetCoreWebView2Controller()
    {
        try
        {
            if (StudioMediaPlayer.PlatformWebView is WebView2Core platformWebView)
            {
                var prop = typeof(WebView2Core).GetProperty(
                    "_coreWebView2Controller",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                );
                return prop?.GetValue(platformWebView) as CoreWebView2Controller;
            }
        }
        catch { }
        return null;
    }

    private void UpdatePlayerVisibility(bool isVisible)
    {
        StudioMediaPlayer.IsVisible = isVisible;
        try
        {
            _coreWebView2Controller ??= GetCoreWebView2Controller();
            if (_coreWebView2Controller is not null)
            {
                _coreWebView2Controller.IsVisible = isVisible;
            }
        }
        catch { }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PlayMediaRequested -= OnPlayMediaRequested;
            _boundViewModel.StopMediaRequested -= OnStopMediaRequested;
            _boundViewModel.StartReviewSessionRequested -= OnStartReviewSessionRequested;
            _boundViewModel.SeekRequested -= OnSeekRequested;
            _boundViewModel.PlaybackToggleRequested -= OnPlaybackToggleRequested;
            _boundViewModel.AudioMixRequested -= OnAudioMixRequested;
            _boundViewModel = null;
        }

        if (DataContext is DubbingViewModel vm)
        {
            _boundViewModel = vm;
            vm.PlayMediaRequested += OnPlayMediaRequested;
            vm.StopMediaRequested += OnStopMediaRequested;
            vm.StartReviewSessionRequested += OnStartReviewSessionRequested;
            vm.SeekRequested += OnSeekRequested;
            vm.PlaybackToggleRequested += OnPlaybackToggleRequested;
            vm.AudioMixRequested += OnAudioMixRequested;
        }
    }

    private void OnSeekRequested(double targetSeconds)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_coreWebView2 is not null)
                {
                    await _coreWebView2.ExecuteScriptAsync(
                        $"if (window.reviewSeek) window.reviewSeek({targetSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)});"
                    );
                }
            }
            catch { }
        });
    }

    private void OnPlaybackToggleRequested(bool isPlaying)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_coreWebView2 is not null)
                {
                    await _coreWebView2.ExecuteScriptAsync(
                        $"if (window.reviewTogglePlay) window.reviewTogglePlay({isPlaying.ToString().ToLowerInvariant()});"
                    );
                }
            }
            catch { }
        });
    }

    private void OnAudioMixRequested(double origVol, double dubVol, string mode)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_coreWebView2 is not null)
                {
                    await _coreWebView2.ExecuteScriptAsync(
                        $"if (window.reviewSetAudioMix) window.reviewSetAudioMix({origVol.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {dubVol.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{mode}');"
                    );
                }
            }
            catch { }
        });
    }

    private void OnStartReviewSessionRequested(DubbingReviewSession session)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (
                string.IsNullOrWhiteSpace(session.VideoFilePath)
                || !File.Exists(session.VideoFilePath)
            )
                return;

            try
            {
                var dir = Path.GetDirectoryName(session.VideoFilePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    dir = Path.GetTempPath();
                }

                var htmlPath = Path.Combine(dir, "_review_session_player.html");
                var videoUri = new Uri(session.VideoFilePath).AbsoluteUri;

                var segItems = session.Segments.Select(s => new
                {
                    index = s.Index,
                    start = s.StartSeconds,
                    end = s.EndSeconds,
                    speaker = s.SpeakerName,
                    color = string.IsNullOrWhiteSpace(s.SpeakerColor) ? "#38BDF8" : s.SpeakerColor,
                    khmer = s.KhmerText,
                    original = s.OriginalText,
                    audioUrl = !string.IsNullOrWhiteSpace(s.AudioClipPath)
                    && File.Exists(s.AudioClipPath)
                        ? new Uri(s.AudioClipPath).AbsoluteUri
                        : null,
                });

                var segmentsJson = System.Text.Json.JsonSerializer.Serialize(segItems);
                var origVolStr = session.OriginalAudioVolume.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                var dubVolStr = session.DubbedAudioVolume.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                var duckRatioStr = session.DuckingVolumeRatio.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                var totalDurStr = session.TotalDurationSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture
                );
                var duckingEnabled = session.EnableDucking ? "true" : "false";

                var reviewHtml =
                    $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  html, body {{
    width: 100%;
    height: 100%;
    background-color: #000000;
    color: #F1F5F9;
    font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    overflow: hidden;
    user-select: none;
  }}
  .player-container {{
    position: relative;
    width: 100%;
    height: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #000;
  }}
  video {{
    width: 100%;
    height: 100%;
    object-fit: contain;
    outline: none;
    cursor: pointer;
  }}
  .overlay-badge {{
    position: absolute;
    top: 10px;
    left: 12px;
    background: rgba(15, 23, 42, 0.88);
    border: 1px solid rgba(56, 189, 248, 0.4);
    backdrop-filter: blur(8px);
    border-radius: 6px;
    padding: 4px 9px;
    font-size: 10px;
    font-weight: 700;
    letter-spacing: 0.5px;
    color: #38BDF8;
    display: flex;
    align-items: center;
    gap: 6px;
    z-index: 10;
  }}
  .overlay-badge .dot {{
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: #10B981;
    animation: blink 1.2s infinite ease-in-out;
  }}
  @keyframes blink {{
    0%, 100% {{ opacity: 1; }}
    50% {{ opacity: 0.3; }}
  }}
  .ducking-indicator {{
    position: absolute;
    top: 10px;
    right: 12px;
    background: rgba(236, 72, 153, 0.9);
    border-radius: 4px;
    padding: 3px 7px;
    font-size: 9px;
    font-weight: 800;
    color: #fff;
    opacity: 0;
    transition: opacity 0.15s ease-in-out;
    z-index: 10;
  }}
  .ducking-indicator.active {{
    opacity: 1;
  }}
  .subtitles-container {{
    position: absolute;
    bottom: 20px;
    left: 4%;
    right: 4%;
    text-align: center;
    pointer-events: none;
    z-index: 10;
  }}
  .subtitle-box {{
    display: inline-block;
    background: rgba(10, 15, 29, 0.85);
    border-radius: 6px;
    padding: 6px 14px;
    border-left: 3px solid #38BDF8;
    max-width: 92%;
    backdrop-filter: blur(6px);
    box-shadow: 0 4px 12px rgba(0,0,0,0.5);
    transition: all 0.1s ease;
  }}
  .sub-speaker {{
    font-size: 9.5px;
    font-weight: 800;
    text-transform: uppercase;
    margin-bottom: 2px;
    letter-spacing: 0.3px;
  }}
  .sub-khmer {{
    font-size: 14.5px;
    font-weight: 600;
    color: #FFFFFF;
    line-height: 1.4;
    text-shadow: 0 1px 3px rgba(0,0,0,0.9);
  }}
  .sub-original {{
    font-size: 11px;
    color: #94A3B8;
    font-style: italic;
    margin-top: 2px;
  }}
</style>
</head>
<body>
<div class=""player-container"">
  <div class=""overlay-badge"">
    <div class=""dot""></div>
    <span id=""modeLabel"">{(session.AudioMode ?? "Dual").ToUpperInvariant()} REVIEW</span>
  </div>
  <div class=""ducking-indicator"" id=""duckingBadge"">DUCKED -25dB</div>
  <video id=""reviewVideo"" src=""{videoUri}"" playsinline></video>
  <div class=""subtitles-container"" id=""subContainer"" style=""display:none;"">
    <div class=""subtitle-box"" id=""subBox"">
      <div class=""sub-speaker"" id=""subSpeaker"">SPEAKER</div>
      <div class=""sub-khmer"" id=""subKhmer"">Khmer Dialogue</div>
      <div class=""sub-original"" id=""subOriginal"">Original Dialogue</div>
    </div>
  </div>
</div>

<script>
  const video = document.getElementById('reviewVideo');
  const modeLabel = document.getElementById('modeLabel');
  const duckingBadge = document.getElementById('duckingBadge');
  const subContainer = document.getElementById('subContainer');
  const subBox = document.getElementById('subBox');
  const subSpeaker = document.getElementById('subSpeaker');
  const subKhmer = document.getElementById('subKhmer');
  const subOriginal = document.getElementById('subOriginal');

  const segments = {segmentsJson};
  let config = {{
    origVol: {origVolStr},
    dubVol: {dubVolStr},
    mode: '{session.AudioMode}',
    ducking: {duckingEnabled},
    duckRatio: {duckRatioStr}
  }};

  const audioMap = new Map();
  segments.forEach(seg => {{
    if (seg.audioUrl) {{
      const a = new Audio(seg.audioUrl);
      a.preload = 'auto';
      audioMap.set(seg.index, a);
    }}
  }});

  let currentActiveSeg = null;

  function updateAudioMix() {{
    if (modeLabel) modeLabel.textContent = (config.mode || 'DUAL').toUpperCase() + ' REVIEW';
    applyVolumes();
  }}

  function applyVolumes() {{
    if (config.mode === 'Original') {{
      video.volume = Math.min(1.0, Math.max(0.0, config.origVol));
      audioMap.forEach(a => {{ a.volume = 0; a.pause(); }});
      duckingBadge.classList.remove('active');
    }} else if (config.mode === 'Dubbed') {{
      video.volume = 0;
      audioMap.forEach(a => {{ a.volume = Math.min(1.0, Math.max(0.0, config.dubVol)); }});
      duckingBadge.classList.remove('active');
    }} else {{
      const isSpeechActive = currentActiveSeg && currentActiveSeg.audioUrl;
      const isDucking = isSpeechActive && config.ducking;
      const effOrig = isDucking ? (config.origVol * config.duckRatio) : config.origVol;
      video.volume = Math.min(1.0, Math.max(0.0, effOrig));
      audioMap.forEach(a => {{ a.volume = Math.min(1.0, Math.max(0.0, config.dubVol)); }});
      if (isDucking) duckingBadge.classList.add('active');
      else duckingBadge.classList.remove('active');
    }}
  }}

  function syncSegments(curTime) {{
    const active = segments.find(s => curTime >= s.start && curTime <= s.end);

    if (active !== currentActiveSeg) {{
      if (currentActiveSeg && audioMap.has(currentActiveSeg.index)) {{
        const prevA = audioMap.get(currentActiveSeg.index);
        prevA.pause();
        prevA.currentTime = 0;
      }}

      currentActiveSeg = active;

      if (active) {{
        subContainer.style.display = 'block';
        subSpeaker.textContent = active.speaker || 'SPEAKER';
        subSpeaker.style.color = active.color || '#38BDF8';
        subBox.style.borderLeftColor = active.color || '#38BDF8';
        subKhmer.textContent = active.khmer || '';
        subKhmer.style.display = active.khmer ? 'block' : 'none';
        subOriginal.textContent = active.original || '';
        subOriginal.style.display = active.original ? 'block' : 'none';

        if (active.audioUrl && audioMap.has(active.index) && config.mode !== 'Original') {{
          const a = audioMap.get(active.index);
          const offset = Math.max(0, curTime - active.start);
          if (offset < (a.duration || 999)) {{
            a.currentTime = offset;
            if (!video.paused) {{
              a.play().catch(() => {{}});
            }}
          }}
        }}
      }} else {{
        subContainer.style.display = 'none';
      }}

      applyVolumes();
    }} else if (active && active.audioUrl && audioMap.has(active.index) && config.mode !== 'Original') {{
      const a = audioMap.get(active.index);
      const expectedOffset = curTime - active.start;
      if (!video.paused && a.paused && expectedOffset >= 0 && expectedOffset < (a.duration || 999)) {{
        a.currentTime = expectedOffset;
        a.play().catch(() => {{}});
      }} else if (Math.abs(a.currentTime - expectedOffset) > 0.3) {{
        a.currentTime = Math.max(0, expectedOffset);
      }}
    }}
  }}

  let lastReportTime = 0;
  video.addEventListener('timeupdate', () => {{
    const now = performance.now();
    syncSegments(video.currentTime);
    if (now - lastReportTime > 80) {{
      lastReportTime = now;
      postTimeUpdate();
    }}
  }});

  video.addEventListener('play', () => {{
    if (currentActiveSeg && audioMap.has(currentActiveSeg.index) && config.mode !== 'Original') {{
      const a = audioMap.get(currentActiveSeg.index);
      a.play().catch(() => {{}});
    }}
    postTimeUpdate();
  }});

  video.addEventListener('pause', () => {{
    audioMap.forEach(a => a.pause());
    postTimeUpdate();
  }});

  video.addEventListener('seeked', () => {{
    syncSegments(video.currentTime);
    postTimeUpdate();
  }});

  function postTimeUpdate() {{
    if (window.chrome && window.chrome.webview) {{
      window.chrome.webview.postMessage(JSON.stringify({{
        type: 'timeupdate',
        currentTime: video.currentTime,
        duration: video.duration || {totalDurStr},
        paused: video.paused
      }}));
    }}
  }}

  window.reviewSeek = function(targetSec) {{
    if (Math.abs(video.currentTime - targetSec) > 0.05) {{
      video.currentTime = targetSec;
      syncSegments(targetSec);
    }}
  }};

  window.reviewTogglePlay = function(shouldPlay) {{
    if (shouldPlay) {{
      video.play().catch(() => {{}});
    }} else {{
      video.pause();
    }}
  }};

  window.reviewSetAudioMix = function(origVol, dubVol, mode) {{
    config.origVol = origVol;
    config.dubVol = dubVol;
    config.mode = mode;
    updateAudioMix();
  }};

  video.addEventListener('click', () => {{
    if (video.paused) video.play();
    else video.pause();
  }});

  video.play().catch(() => {{
    document.body.addEventListener('click', () => {{ video.play(); }}, {{ once: true }});
  }});

  updateAudioMix();
</script>
</body>
</html>";

                File.WriteAllText(htmlPath, reviewHtml);
                var uri = new Uri(htmlPath).AbsoluteUri;

                if (_coreWebView2 is not null)
                {
                    _coreWebView2.Navigate(uri);
                }
                else
                {
                    StudioMediaPlayer.Url = new Uri(uri);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReviewPlayer] Error: {ex.Message}");
            }
        });
    }

    private void OnStopMediaRequested()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_coreWebView2 is not null)
                {
                    _coreWebView2.Navigate("about:blank");
                }
                else
                {
                    StudioMediaPlayer.Url = new Uri("about:blank");
                }
            }
            catch { }
        });
    }

    private void OnPlayMediaRequested(string filePath, bool isVideo, string title)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    dir = Path.GetTempPath();
                }

                var fileName = Path.GetFileName(filePath);
                var htmlPath = Path.Combine(dir, "_monitor_player.html");

                var encodedTitle = WebUtility.HtmlEncode(title);
                var encodedFileName = WebUtility.UrlEncode(fileName).Replace("+", "%20");

                string bodyContent;
                if (isVideo)
                {
                    bodyContent =
                        $@"
<div class=""video-wrap"">
    <video src=""./{encodedFileName}"" controls autoplay playsinline></video>
</div>";
                }
                else
                {
                    bodyContent =
                        $@"
<div class=""audio-wrap"">
    <div class=""wave-bars"">
        <div class=""bar""></div><div class=""bar""></div><div class=""bar""></div>
        <div class=""bar""></div><div class=""bar""></div><div class=""bar""></div><div class=""bar""></div>
    </div>
    <div class=""media-title"">{encodedTitle}</div>
    <audio src=""./{encodedFileName}"" controls autoplay></audio>
</div>";
                }

                var fullHtml =
                    $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  html, body {{
    width: 100%;
    height: 100%;
    background-color: #000000;
    color: #F1F5F9;
    font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, sans-serif;
    overflow: hidden;
    display: flex;
    align-items: center;
    justify-content: center;
  }}
  .video-wrap {{
    width: 100%;
    height: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
    background: #000;
  }}
  video {{
    width: 100%;
    height: 100%;
    object-fit: contain;
    outline: none;
  }}
  .audio-wrap {{
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    width: 90%;
    gap: 12px;
  }}
  .wave-bars {{
    display: flex;
    align-items: center;
    gap: 4px;
    height: 38px;
  }}
  .bar {{
    width: 3px;
    background: #6366F1;
    border-radius: 2px;
    animation: pulse 0.9s infinite alternate ease-in-out;
  }}
  .bar:nth-child(2) {{ animation-delay: 0.15s; background: #8B5CF6; }}
  .bar:nth-child(3) {{ animation-delay: 0.3s; background: #EC4899; }}
  .bar:nth-child(4) {{ animation-delay: 0.45s; background: #10B981; }}
  .bar:nth-child(5) {{ animation-delay: 0.2s; background: #3B82F6; }}
  .bar:nth-child(6) {{ animation-delay: 0.35s; background: #F59E0B; }}
  .bar:nth-child(7) {{ animation-delay: 0.5s; background: #6366F1; }}
  @keyframes pulse {{
    0% {{ height: 6px; opacity: 0.3; }}
    100% {{ height: 32px; opacity: 1; }}
  }}
  .media-title {{
    font-size: 11px;
    font-weight: 600;
    color: #E2E8F0;
    max-width: 95%;
    text-overflow: ellipsis;
    overflow: hidden;
    white-space: nowrap;
    text-align: center;
  }}
  audio {{
    width: 100%;
    height: 36px;
    outline: none;
    border-radius: 18px;
    filter: invert(0.9) hue-rotate(180deg);
  }}
</style>
</head>
<body>
{bodyContent}
</body>
</html>";

                try
                {
                    File.WriteAllText(htmlPath, fullHtml);
                    var uri = new Uri(htmlPath).AbsoluteUri;

                    if (_coreWebView2 is not null)
                    {
                        _coreWebView2.Navigate(uri);
                    }
                    else
                    {
                        StudioMediaPlayer.Url = new Uri(uri);
                    }
                }
                catch
                {
                    // Fallback to direct media URI navigation if folder is read-only
                    var directUri = new Uri(filePath).AbsoluteUri;
                    if (_coreWebView2 is not null)
                    {
                        _coreWebView2.Navigate(directUri);
                    }
                    else
                    {
                        StudioMediaPlayer.Url = new Uri(directUri);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StudioMonitor] Error: {ex.Message}");
            }
        });
    }
}
