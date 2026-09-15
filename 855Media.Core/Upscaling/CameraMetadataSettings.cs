using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace _855Media.Core.Upscaling;

public enum CameraProfileType
{
    [Display(Name = "Copy Original Metadata")]
    None,

    [Display(Name = "Clean Normalized (Strip Tracking Metadata)")]
    CleanNormalized,

    [Display(Name = "Apple iPhone 15 Pro (iOS Camera)")]
    AppleIPhone15Pro,

    [Display(Name = "Sony Alpha a7 IV (Mirrorless Camera)")]
    SonyAlphaA7IV,

    [Display(Name = "Canon EOS R6 Mark II (DSLR / Mirrorless)")]
    CanonEosR6,

    [Display(Name = "Samsung Galaxy S24 Ultra (Android Camera)")]
    SamsungGalaxyS24,

    [Display(Name = "Custom Camera Metadata")]
    Custom,
}

public class CameraMetadataSettings
{
    public CameraProfileType ProfileType { get; set; } = CameraProfileType.CleanNormalized;
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Software { get; set; }
    public string? Artist { get; set; }
    public string? Copyright { get; set; }
    public bool InjectCurrentTimestamp { get; set; } = true;

    public CameraMetadataSettings Clone()
    {
        return new CameraMetadataSettings
        {
            ProfileType = ProfileType,
            Make = Make,
            Model = Model,
            Software = Software,
            Artist = Artist,
            Copyright = Copyright,
            InjectCurrentTimestamp = InjectCurrentTimestamp,
        };
    }

    public List<string> BuildFfmpegMetadataArgs(string defaultTitle)
    {
        if (ProfileType == CameraProfileType.None)
        {
            return ["-map_metadata", "1"]; // Legacy fallback: copy original metadata
        }

        var args = new List<string> { "-map_metadata", "-1" }; // Strip old tracking metadata

        string make = Make ?? string.Empty;
        string model = Model ?? string.Empty;
        string software = Software ?? string.Empty;
        string handler = "VideoHandler";
        string encoder = "855Media Normalized Encoder";

        switch (ProfileType)
        {
            case CameraProfileType.CleanNormalized:
                if (!string.IsNullOrWhiteSpace(defaultTitle))
                    args.AddRange(["-metadata", $"title={defaultTitle}"]);
                args.AddRange(["-metadata", "comment="]);
                args.AddRange(["-metadata", "artist="]);
                return args;

            case CameraProfileType.AppleIPhone15Pro:
                make = string.IsNullOrWhiteSpace(make) ? "Apple" : make;
                model = string.IsNullOrWhiteSpace(model) ? "iPhone 15 Pro" : model;
                software = string.IsNullOrWhiteSpace(software) ? "17.5.1" : software;
                handler = "Core Media Video";
                encoder = "Apple H.264 Encoder";
                break;

            case CameraProfileType.SonyAlphaA7IV:
                make = string.IsNullOrWhiteSpace(make) ? "Sony" : make;
                model = string.IsNullOrWhiteSpace(model) ? "ILCE-7M4" : model;
                software = string.IsNullOrWhiteSpace(software) ? "v3.00" : software;
                handler = "Sony Video Media Handler";
                encoder = "Sony XAVC S Encoder";
                break;

            case CameraProfileType.CanonEosR6:
                make = string.IsNullOrWhiteSpace(make) ? "Canon" : make;
                model = string.IsNullOrWhiteSpace(model) ? "Canon EOS R6 Mark II" : model;
                software = string.IsNullOrWhiteSpace(software) ? "Firmware 1.3.0" : software;
                handler = "Canon Video Media Handler";
                encoder = "Canon MP4 Encoder";
                break;

            case CameraProfileType.SamsungGalaxyS24:
                make = string.IsNullOrWhiteSpace(make) ? "Samsung" : make;
                model = string.IsNullOrWhiteSpace(model) ? "SM-S928B" : model;
                software = string.IsNullOrWhiteSpace(software) ? "Android 14" : software;
                handler = "VideoHandler";
                encoder = "Samsung AVC Encoder";
                break;

            case CameraProfileType.Custom:
                break;
        }

        if (!string.IsNullOrWhiteSpace(defaultTitle))
            args.AddRange(["-metadata", $"title={defaultTitle}"]);
        if (!string.IsNullOrWhiteSpace(Artist))
            args.AddRange(["-metadata", $"artist={Artist}"]);
        if (!string.IsNullOrWhiteSpace(Copyright))
            args.AddRange(["-metadata", $"copyright={Copyright}"]);

        if (!string.IsNullOrWhiteSpace(make))
        {
            args.AddRange(["-metadata", $"make={make}"]);
            args.AddRange(["-metadata:g", $"com.apple.quicktime.make={make}"]);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            args.AddRange(["-metadata", $"model={model}"]);
            args.AddRange(["-metadata:g", $"com.apple.quicktime.model={model}"]);
        }

        if (!string.IsNullOrWhiteSpace(software))
        {
            args.AddRange(["-metadata", $"software={software}"]);
            args.AddRange(["-metadata:g", $"com.apple.quicktime.software={software}"]);
        }

        args.AddRange(["-metadata", $"handler_name={handler}"]);
        args.AddRange(["-metadata", $"encoder={encoder}"]);

        if (InjectCurrentTimestamp)
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            args.AddRange(["-metadata", $"creation_time={now}"]);
        }

        return args;
    }
}
