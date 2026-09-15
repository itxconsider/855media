using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using _855Media.Core.Audio;
using _855Media.Core.Downloading;
using _855Media.Core.Upscaling;
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
    public partial bool ShouldInjectTags { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldSaveTitleToTextFile { get; set; } = false;

    [ObservableProperty]
    public partial bool ShouldSkipExistingFiles { get; set; }

    [ObservableProperty]
    public partial string FileNameTemplate { get; set; } = "$title";

    [ObservableProperty]
    public partial int ParallelLimit { get; set; } = 2;

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

    // Work / Upscaler State Persistence
    [ObservableProperty]
    public partial string? UpscalerOutputDirectory { get; set; }

    [ObservableProperty]
    public partial string? UpscalerScratchDirectory { get; set; }

    [ObservableProperty]
    public partial UpscaleTargetResolution UpscalerTargetResolution { get; set; } =
        UpscaleTargetResolution.Hd1080p;

    [ObservableProperty]
    public partial AspectRatioMode UpscalerTargetAspectRatio { get; set; } =
        AspectRatioMode.Original;

    [ObservableProperty]
    public partial SmartTrackingMode UpscalerTrackingMode { get; set; } =
        SmartTrackingMode.StaticCenter;

    [ObservableProperty]
    public partial UpscaleVideoCodec UpscalerCodec { get; set; } = UpscaleVideoCodec.H264;

    [ObservableProperty]
    public partial HardwareAccelerationMode UpscalerHardwareAcceleration { get; set; } =
        HardwareAccelerationMode.Auto;

    [ObservableProperty]
    public partial int UpscalerMaxConcurrency { get; set; } = 2;

    [ObservableProperty]
    public partial UpscaleModelType UpscalerModelType { get; set; } = UpscaleModelType.RealWorld;

    [ObservableProperty]
    public partial string UpscalerPresetName { get; set; } = "Default / Neutral";

    [ObservableProperty]
    public partial PostBatchAction UpscalerPostBatchAction { get; set; } =
        PostBatchAction.DoNothing;

    // Enhance Settings
    [ObservableProperty]
    public partial bool UpscalerEnableFacialClarity { get; set; } = true;

    [ObservableProperty]
    public partial bool UpscalerEnableFaceRestoration { get; set; } = false;

    [ObservableProperty]
    public partial double UpscalerFaceRestorationFidelity { get; set; } = 0.7;

    [ObservableProperty]
    public partial bool UpscalerEnableDenoise { get; set; } = false;

    [ObservableProperty]
    public partial bool UpscalerEnableDeinterlace { get; set; } = false;

    [ObservableProperty]
    public partial bool UpscalerEnableMicroZoom { get; set; } = false;

    [ObservableProperty]
    public partial double UpscalerMicroZoomPercent { get; set; } = 3.0;

    [ObservableProperty]
    public partial SmartZoomMode UpscalerZoomMode { get; set; } = SmartZoomMode.ActionAnchored;

    // Split Settings
    [ObservableProperty]
    public partial bool UpscalerEnableSplitAndUpscale { get; set; } = false;

    [ObservableProperty]
    public partial bool UpscalerMergeAfterUpscale { get; set; } = true;

    [ObservableProperty]
    public partial SplitMode UpscalerSplitMode { get; set; } = SplitMode.InHalf;

    [ObservableProperty]
    public partial int UpscalerCustomSplitPartCount { get; set; } = 2;

    [ObservableProperty]
    public partial double UpscalerCustomSplitSegmentDurationSeconds { get; set; } = 60.0;

    // Camera & Metadata Normalization Settings
    [ObservableProperty]
    public partial CameraProfileType UpscalerCameraProfileType { get; set; } =
        CameraProfileType.CleanNormalized;

    [ObservableProperty]
    public partial string? UpscalerCameraMake { get; set; }

    [ObservableProperty]
    public partial string? UpscalerCameraModel { get; set; }

    [ObservableProperty]
    public partial string? UpscalerCameraSoftware { get; set; }

    [ObservableProperty]
    public partial string? UpscalerCameraArtist { get; set; }

    [ObservableProperty]
    public partial string? UpscalerCameraCopyright { get; set; }

    [ObservableProperty]
    public partial bool UpscalerCameraInjectTimestamp { get; set; } = true;

    // Color Grading & Preview Settings
    [ObservableProperty]
    public partial string UpscalerColorPresetName { get; set; } = "Neutral / Custom";

    [ObservableProperty]
    public partial bool UpscalerIsColorGradingPanelOpen { get; set; } = false;

    [ObservableProperty]
    public partial double UpscalerSplitDividerRatio { get; set; } = 0.50;

    [ObservableProperty]
    public partial double UpscalerZoomScale { get; set; } = 1.0;

    [ObservableProperty]
    public partial bool UpscalerIsBasicExposureExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool UpscalerIsColorWheelsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool UpscalerIsFilmEmulationExpanded { get; set; } = true;

    // Persisted Baseline Color Grading Values
    [ObservableProperty]
    public partial double UpscalerColorBrightness { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorContrast { get; set; } = 1.0;

    [ObservableProperty]
    public partial double UpscalerColorSaturation { get; set; } = 1.0;

    [ObservableProperty]
    public partial double UpscalerColorGamma { get; set; } = 1.0;

    [ObservableProperty]
    public partial double UpscalerColorVibrance { get; set; } = 0.0;

    [ObservableProperty]
    public partial bool UpscalerShowHistogram { get; set; } = true;

    [ObservableProperty]
    public partial bool UpscalerColorAutoNormalize { get; set; } = false;

    [ObservableProperty]
    public partial int UpscalerColorTemperature { get; set; } = 6500;

    [ObservableProperty]
    public partial double UpscalerColorShadowRed { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorShadowGreen { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorShadowBlue { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorMidtoneRed { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorMidtoneGreen { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorMidtoneBlue { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorHighlightRed { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorHighlightGreen { get; set; } = 0.0;

    [ObservableProperty]
    public partial double UpscalerColorHighlightBlue { get; set; } = 0.0;

    [ObservableProperty]
    public partial string UpscalerColorToneCurve { get; set; } = "None";

    [ObservableProperty]
    public partial int UpscalerColorFilmGrain { get; set; } = 0;

    [ObservableProperty]
    public partial double UpscalerColorVignette { get; set; } = 0.0;

    [ObservableProperty]
    public partial string? UpscalerColorLutPath { get; set; }

    [ObservableProperty]
    public partial double UpscalerColorLutOpacity { get; set; } = 1.0;

    // Selected Dashboard Tab
    [ObservableProperty]
    public partial DashboardTab LastSelectedDashboardTab { get; set; } = DashboardTab.Upscaler;

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
    [JsonSerializable(typeof(UpscaleTargetResolution))]
    [JsonSerializable(typeof(UpscaleVideoCodec))]
    [JsonSerializable(typeof(HardwareAccelerationMode))]
    [JsonSerializable(typeof(UpscaleModelType))]
    [JsonSerializable(typeof(PostBatchAction))]
    [JsonSerializable(typeof(SplitMode))]
    [JsonSerializable(typeof(CameraProfileType))]
    [JsonSerializable(typeof(AspectRatioMode))]
    [JsonSerializable(typeof(DashboardTab))]
    private partial class SerializerContext : JsonSerializerContext;
}
