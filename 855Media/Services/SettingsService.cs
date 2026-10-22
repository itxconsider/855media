using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using _855Media.Core.Audio;
using _855Media.Core.Downloading;
using _855Media.Core.Editing;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.ViewModels.Components;
using Cogwheel;
using CommunityToolkit.Mvvm.ComponentModel;
using Container = YoutubeExplode.Videos.Streams.Container;

namespace _855Media.Services;

[ObservableObject]
public partial class SettingsService()
    : SettingsBase(StartOptions.Current.SettingsPath, SerializerContext.Default)
{
    [ObservableProperty]
    public partial bool IsUkraineSupportMessageEnabled { get; set; } = false;

    [ObservableProperty]
    public partial ThemeVariant Theme { get; set; } = ThemeVariant.Dark;

    [ObservableProperty]
    public partial Language Language { get; set; }

    [ObservableProperty]
    public partial bool IsAutoUpdateEnabled { get; set; } = false;

    [ObservableProperty]
    public partial bool IsAuthPersisted { get; set; } = true;

    [ObservableProperty]
    public partial string? FFmpegFilePath { get; set; }

    [ObservableProperty]
    public partial bool ShouldInjectLanguageSpecificAudioStreams { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectSubtitles { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldTranslateCaptionsToEnglish { get; set; } = false;

    [ObservableProperty]
    public partial bool ShouldInjectTags { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldSaveTitleToTextFile { get; set; } = false;

    [ObservableProperty]
    public partial bool ShouldTranslateTitleToEnglish { get; set; } = false;

    [ObservableProperty]
    public partial bool ShouldDownloadProfilePicture { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldSkipExistingFiles { get; set; }

    [ObservableProperty]
    public partial string FileNameTemplate { get; set; } = "$title";

    [ObservableProperty]
    public partial int ParallelLimit { get; set; } = 1;

    [ObservableProperty]
    public partial int ConversionParallelLimit { get; set; } = 2;

    partial void OnConversionParallelLimitChanged(int value)
    {
        var limit = Math.Clamp(value, 1, 10);
        if (value != limit)
        {
            ConversionParallelLimit = limit;
            return;
        }

        MediaCompatibility.MaxConcurrentConversions = limit;
    }

    [ObservableProperty]
    [JsonConverter(typeof(AuthCookiesEncryptionConverter))]
    public partial IReadOnlyList<Cookie>? LastAuthCookies { get; set; }

    [ObservableProperty]
    [JsonConverter(typeof(ContainerJsonConverter))]
    public partial Container LastContainer { get; set; } = Container.Mp4;

    [ObservableProperty]
    public partial VideoQualityPreference LastVideoQualityPreference { get; set; } =
        VideoQualityPreference.Highest;

    [ObservableProperty]
    public partial bool AutoRetryFailedDownloads { get; set; } = true;

    [ObservableProperty]
    public partial int MaxRetryCount { get; set; } = 3;

    [ObservableProperty]
    public partial int RetryDelaySeconds { get; set; } = 5;

    [ObservableProperty]
    public partial bool IsolateSpeechAudio { get; set; } = false;

    [ObservableProperty]
    public partial AudioProcessingMode SelectedAudioProcessingMode { get; set; } =
        AudioProcessingMode.None;

    [ObservableProperty]
    public partial string? LicenseToken { get; set; }

    [ObservableProperty]
    public partial DateTime? FirstRunDate { get; set; }

    [ObservableProperty]
    public partial DateTime? LastExecutionDate { get; set; }

    [ObservableProperty]
    public partial string? LicenseActivationApiUrl { get; set; }

    [ObservableProperty]
    public partial string PurchaseUrl { get; set; } = "https://855media.com/buy";

    // Gemini AI Translation Settings
    [ObservableProperty]
    public partial string? GeminiApiKey { get; set; }

    [ObservableProperty]
    public partial string GeminiModel { get; set; } = "gemini-2.0-flash";

    // Work / Editor State Persistence
    [ObservableProperty]
    [JsonPropertyName("UpscalerOutputDirectory")]
    public partial string? EditorOutputDirectory { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerScratchDirectory")]
    public partial string? EditorScratchDirectory { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerTargetResolution")]
    public partial EditTargetResolution EditorTargetResolution { get; set; } =
        EditTargetResolution.Hd1080p;

    [ObservableProperty]
    [JsonPropertyName("UpscalerTargetAspectRatio")]
    public partial AspectRatioMode EditorTargetAspectRatio { get; set; } = AspectRatioMode.Original;

    [ObservableProperty]
    [JsonPropertyName("UpscalerTrackingMode")]
    public partial SmartTrackingMode EditorTrackingMode { get; set; } =
        SmartTrackingMode.StaticCenter;

    [ObservableProperty]
    [JsonPropertyName("UpscalerCodec")]
    public partial EditVideoCodec EditorCodec { get; set; } = EditVideoCodec.H264;

    [ObservableProperty]
    [JsonPropertyName("UpscalerHardwareAcceleration")]
    public partial HardwareAccelerationMode EditorHardwareAcceleration { get; set; } =
        HardwareAccelerationMode.Auto;

    [ObservableProperty]
    [JsonPropertyName("UpscalerTargetFramerate")]
    public partial TargetFramerate EditorTargetFramerate { get; set; } = TargetFramerate.Original;

    [ObservableProperty]
    [JsonPropertyName("UpscalerMaxConcurrency")]
    public partial int EditorMaxConcurrency { get; set; } = 2;

    [ObservableProperty]
    [JsonPropertyName("UpscalerModelType")]
    public partial EditModelType EditorModelType { get; set; } = EditModelType.FastNative;

    [ObservableProperty]
    [JsonPropertyName("UpscalerHasConfiguredModelType")]
    public partial bool EditorHasConfiguredModelType { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerPresetName")]
    public partial string EditorPresetName { get; set; } = "Default / Neutral";

    [ObservableProperty]
    [JsonPropertyName("UpscalerPostBatchAction")]
    public partial PostBatchAction EditorPostBatchAction { get; set; } = PostBatchAction.DoNothing;

    // Enhance Settings
    [ObservableProperty]
    [JsonPropertyName("UpscalerEnableFacialClarity")]
    public partial bool EditorEnableFacialClarity { get; set; } = true;

    [ObservableProperty]
    [JsonPropertyName("UpscalerEnableDenoise")]
    public partial bool EditorEnableDenoise { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerEnableDeinterlace")]
    public partial bool EditorEnableDeinterlace { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerEnableMicroZoom")]
    public partial bool EditorEnableMicroZoom { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerMicroZoomPercent")]
    public partial double EditorMicroZoomPercent { get; set; } = 3.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerZoomMode")]
    public partial SmartZoomMode EditorZoomMode { get; set; } = SmartZoomMode.ActionAnchored;

    [ObservableProperty]
    [JsonPropertyName("UpscalerSpeedMode")]
    public partial RenderSpeedMode EditorSpeedMode { get; set; } = RenderSpeedMode.Balanced;

    [ObservableProperty]
    [JsonPropertyName("UpscalerPlaybackSpeed")]
    public partial double EditorPlaybackSpeed { get; set; } = 1.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerAudioMode")]
    public partial _855Media.Core.Editing.EditAudioMode EditorAudioMode { get; set; } =
        _855Media.Core.Editing.EditAudioMode.CopyOriginal;

    // Split Settings
    [ObservableProperty]
    [JsonPropertyName("UpscalerEnableSplitAndUpscale")]
    public partial bool EditorEnableSplitAndEdit { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerMergeAfterUpscale")]
    public partial bool EditorMergeAfterEdit { get; set; } = true;

    [ObservableProperty]
    [JsonPropertyName("UpscalerSplitMode")]
    public partial SplitMode EditorSplitMode { get; set; } = SplitMode.InHalf;

    [ObservableProperty]
    [JsonPropertyName("UpscalerCustomSplitPartCount")]
    public partial int EditorCustomSplitPartCount { get; set; } = 2;

    [ObservableProperty]
    [JsonPropertyName("UpscalerCustomSplitSegmentDurationSeconds")]
    public partial double EditorCustomSplitSegmentDurationSeconds { get; set; } = 60.0;

    // Camera & Metadata Normalization Settings
    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraProfileType")]
    public partial CameraProfileType EditorCameraProfileType { get; set; } =
        CameraProfileType.CleanNormalized;

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraMake")]
    public partial string? EditorCameraMake { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraModel")]
    public partial string? EditorCameraModel { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraSoftware")]
    public partial string? EditorCameraSoftware { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraLensModel")]
    public partial string? EditorCameraLensModel { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraFocalLength")]
    public partial string? EditorCameraFocalLength { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraFNumber")]
    public partial string? EditorCameraFNumber { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraArtist")]
    public partial string? EditorCameraArtist { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraCopyright")]
    public partial string? EditorCameraCopyright { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerCameraInjectTimestamp")]
    public partial bool EditorCameraInjectTimestamp { get; set; } = true;

    // Color Grading & Preview Settings
    [ObservableProperty]
    [JsonPropertyName("UpscalerColorPresetName")]
    public partial string EditorColorPresetName { get; set; } = "Neutral / Custom";

    [ObservableProperty]
    [JsonPropertyName("UpscalerIsColorGradingPanelOpen")]
    public partial bool EditorIsColorGradingPanelOpen { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerSplitDividerRatio")]
    public partial double EditorSplitDividerRatio { get; set; } = 0.50;

    [ObservableProperty]
    [JsonPropertyName("UpscalerZoomScale")]
    public partial double EditorZoomScale { get; set; } = 1.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerIsBasicExposureExpanded")]
    public partial bool EditorIsBasicExposureExpanded { get; set; } = true;

    [ObservableProperty]
    [JsonPropertyName("UpscalerIsColorWheelsExpanded")]
    public partial bool EditorIsColorWheelsExpanded { get; set; } = true;

    [ObservableProperty]
    [JsonPropertyName("UpscalerIsFilmEmulationExpanded")]
    public partial bool EditorIsFilmEmulationExpanded { get; set; } = true;

    // Persisted Baseline Color Grading Values
    [ObservableProperty]
    [JsonPropertyName("UpscalerColorBrightness")]
    public partial double EditorColorBrightness { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorContrast")]
    public partial double EditorColorContrast { get; set; } = 1.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorSaturation")]
    public partial double EditorColorSaturation { get; set; } = 1.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorGamma")]
    public partial double EditorColorGamma { get; set; } = 1.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorVibrance")]
    public partial double EditorColorVibrance { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerShowHistogram")]
    public partial bool EditorShowHistogram { get; set; } = true;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorAutoNormalize")]
    public partial bool EditorColorAutoNormalize { get; set; } = false;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorTemperature")]
    public partial int EditorColorTemperature { get; set; } = 6500;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorShadowRed")]
    public partial double EditorColorShadowRed { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorShadowGreen")]
    public partial double EditorColorShadowGreen { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorShadowBlue")]
    public partial double EditorColorShadowBlue { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorMidtoneRed")]
    public partial double EditorColorMidtoneRed { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorMidtoneGreen")]
    public partial double EditorColorMidtoneGreen { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorMidtoneBlue")]
    public partial double EditorColorMidtoneBlue { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorHighlightRed")]
    public partial double EditorColorHighlightRed { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorHighlightGreen")]
    public partial double EditorColorHighlightGreen { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorHighlightBlue")]
    public partial double EditorColorHighlightBlue { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorToneCurve")]
    public partial string EditorColorToneCurve { get; set; } = "None";

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorFilmGrain")]
    public partial int EditorColorFilmGrain { get; set; } = 0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorVignette")]
    public partial double EditorColorVignette { get; set; } = 0.0;

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorLutPath")]
    public partial string? EditorColorLutPath { get; set; }

    [ObservableProperty]
    [JsonPropertyName("UpscalerColorLutOpacity")]
    public partial double EditorColorLutOpacity { get; set; } = 1.0;

    // Selected Dashboard Tab
    [ObservableProperty]
    public partial DashboardTab LastSelectedDashboardTab { get; set; } = DashboardTab.Editor;

    public override bool Load()
    {
        try
        {
            var path = StartOptions.Current.SettingsPath;
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                // If the file is 0 bytes or essentially empty, purge it so base.Load() initializes defaults cleanly
                if (fileInfo.Length < 2)
                {
                    File.Delete(path);
                    return false;
                }
            }

            return base.Load();
        }
        catch (Exception)
        {
            // If the settings file is corrupted or contains invalid JSON tokens,
            // delete the corrupted file so the application gracefully falls back to default settings.
            try
            {
                var path = StartOptions.Current.SettingsPath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }

            return false;
        }
    }

    public override void Save()
    {
        // Clear the cookies if they are not supposed to be persisted
        var lastAuthCookies = LastAuthCookies;
        if (!IsAuthPersisted)
            LastAuthCookies = null;

        base.Save();

        LastAuthCookies = lastAuthCookies;
    }
}

public partial class SettingsService
{
    private class ContainerJsonConverter : JsonConverter<Container>
    {
        public override Container Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            Container? result = null;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (
                        reader.TokenType == JsonTokenType.PropertyName
                        && reader.GetString() == "Name"
                        && reader.Read()
                        && reader.TokenType == JsonTokenType.String
                    )
                    {
                        var name = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            result = new Container(name);
                    }
                }
            }

            return result
                ?? throw new InvalidOperationException(
                    $"Invalid JSON for type '{typeToConvert.FullName}'."
                );
        }

        public override void Write(
            Utf8JsonWriter writer,
            Container value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString("Name", value.Name);
            writer.WriteEndObject();
        }
    }
}

public partial class SettingsService
{
    [JsonSerializable(typeof(SettingsService))]
    [JsonSerializable(typeof(EditTargetResolution))]
    [JsonSerializable(typeof(EditVideoCodec))]
    [JsonSerializable(typeof(HardwareAccelerationMode))]
    [JsonSerializable(typeof(EditModelType))]
    [JsonSerializable(typeof(PostBatchAction))]
    [JsonSerializable(typeof(SplitMode))]
    [JsonSerializable(typeof(CameraProfileType))]
    [JsonSerializable(typeof(AspectRatioMode))]
    [JsonSerializable(typeof(DashboardTab))]
    private partial class SerializerContext : JsonSerializerContext;
}
