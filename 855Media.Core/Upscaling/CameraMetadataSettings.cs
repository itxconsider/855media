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

    [Display(Name = "Apple iPhone 17 Pro Max (iOS Camera)")]
    AppleIPhone17ProMax,

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
    public string? LensModel { get; set; }
    public string? FocalLength { get; set; }
    public string? FNumber { get; set; }
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
            LensModel = LensModel,
            FocalLength = FocalLength,
            FNumber = FNumber,
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
        string lensModel = LensModel ?? string.Empty;
        string focalLength = FocalLength ?? string.Empty;
        string fNumber = FNumber ?? string.Empty;
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

            case CameraProfileType.AppleIPhone17ProMax:
                make = string.IsNullOrWhiteSpace(make) ? "Apple" : make;
                model = string.IsNullOrWhiteSpace(model) ? "iPhone 17 Pro Max" : model;
                software = string.IsNullOrWhiteSpace(software) ? "iOS 19.1" : software;
                lensModel = string.IsNullOrWhiteSpace(lensModel)
                    ? "iPhone 17 Pro Max back triple camera 24mm f/1.78"
                    : lensModel;
                focalLength = string.IsNullOrWhiteSpace(focalLength) ? "24mm" : focalLength;
                fNumber = string.IsNullOrWhiteSpace(fNumber) ? "1.78" : fNumber;
                handler = "Core Media Video";
                encoder = "Apple HEVC Encoder";
                break;

            case CameraProfileType.AppleIPhone15Pro:
                make = string.IsNullOrWhiteSpace(make) ? "Apple" : make;
                model = string.IsNullOrWhiteSpace(model) ? "iPhone 15 Pro" : model;
                software = string.IsNullOrWhiteSpace(software) ? "iOS 17.5.1" : software;
                lensModel = string.IsNullOrWhiteSpace(lensModel)
                    ? "iPhone 15 Pro back camera 24mm f/1.78"
                    : lensModel;
                focalLength = string.IsNullOrWhiteSpace(focalLength) ? "24mm" : focalLength;
                fNumber = string.IsNullOrWhiteSpace(fNumber) ? "1.78" : fNumber;
                handler = "Core Media Video";
                encoder = "Apple H.264 Encoder";
                break;

            case CameraProfileType.SonyAlphaA7IV:
                make = string.IsNullOrWhiteSpace(make) ? "Sony" : make;
                model = string.IsNullOrWhiteSpace(model) ? "ILCE-7M4" : model;
                software = string.IsNullOrWhiteSpace(software) ? "v3.00" : software;
                lensModel = string.IsNullOrWhiteSpace(lensModel)
                    ? "FE 24-70mm F2.8 GM II"
                    : lensModel;
                handler = "Sony Video Media Handler";
                encoder = "Sony XAVC S Encoder";
                break;

            case CameraProfileType.CanonEosR6:
                make = string.IsNullOrWhiteSpace(make) ? "Canon" : make;
                model = string.IsNullOrWhiteSpace(model) ? "Canon EOS R6 Mark II" : model;
                software = string.IsNullOrWhiteSpace(software) ? "Firmware 1.3.0" : software;
                lensModel = string.IsNullOrWhiteSpace(lensModel)
                    ? "RF24-70mm F2.8 L IS USM"
                    : lensModel;
                handler = "Canon Video Media Handler";
                encoder = "Canon MP4 Encoder";
                break;

            case CameraProfileType.SamsungGalaxyS24:
                make = string.IsNullOrWhiteSpace(make) ? "Samsung" : make;
                model = string.IsNullOrWhiteSpace(model) ? "SM-S928B" : model;
                software = string.IsNullOrWhiteSpace(software) ? "Android 14" : software;
                lensModel = string.IsNullOrWhiteSpace(lensModel)
                    ? "Galaxy S24 Ultra Main Camera 24mm f/1.7"
                    : lensModel;
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

        if (!string.IsNullOrWhiteSpace(lensModel))
        {
            args.AddRange(["-metadata", $"lens_model={lensModel}"]);
            args.AddRange(["-metadata:s:v", $"lens_model={lensModel}"]);
            args.AddRange(["-metadata:g", $"com.apple.quicktime.lens_model={lensModel}"]);
        }

        if (!string.IsNullOrWhiteSpace(focalLength))
        {
            args.AddRange(["-metadata", $"focal_length={focalLength}"]);
            args.AddRange(["-metadata:s:v", $"focal_length={focalLength}"]);
            args.AddRange(["-metadata:g", $"com.apple.quicktime.focal_length={focalLength}"]);
        }

        if (!string.IsNullOrWhiteSpace(fNumber))
        {
            args.AddRange(["-metadata", $"f_number={fNumber}"]);
            args.AddRange(["-metadata:s:v", $"f_number={fNumber}"]);
            args.AddRange(["-metadata:g", $"com.apple.quicktime.f_number={fNumber}"]);
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
