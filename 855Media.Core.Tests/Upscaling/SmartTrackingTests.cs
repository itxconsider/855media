using System;
using _855Media.Core.Upscaling;
using Xunit;

namespace _855Media.Core.Tests.Upscaling;

public class SmartTrackingTests
{
    [Fact]
    public void UpscaleJob_TrackingDefaults_AreStaticCenterAndActionAnchored()
    {
        var job = new UpscaleJob();
        Assert.Equal(SmartTrackingMode.StaticCenter, job.TrackingMode);
        Assert.Equal(SmartZoomMode.ActionAnchored, job.ZoomMode);
        Assert.Equal(0.5, job.ActionCentroidX);
        Assert.Equal(0.5, job.ActionCentroidY);
    }

    [Fact]
    public void AspectRatioFilterBuilder_StaticCenter_ProducesStandardCrop()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(
            AspectRatioMode.Vertical916Crop,
            UpscaleTargetResolution.Hd1080p,
            SmartTrackingMode.StaticCenter
        );

        Assert.NotNull(filter);
        Assert.Contains(
            "crop=w='min(iw,ih*9/16)':h='min(ih,iw*16/9)',scale=1080:1920:flags=lanczos",
            filter
        );
        Assert.DoesNotContain("out_w", filter);
    }

    [Theory]
    [InlineData(
        SmartTrackingMode.MotionCentroid,
        0.25,
        0.50,
        "max(0,min(iw-out_w,iw*0.250-out_w/2))"
    )]
    [InlineData(
        SmartTrackingMode.FacePriority,
        0.72,
        0.40,
        "max(0,min(iw-out_w,iw*0.720-out_w/2))"
    )]
    public void AspectRatioFilterBuilder_SmartTracking_ProducesActionAnchoredX(
        SmartTrackingMode mode,
        double centroidX,
        double centroidY,
        string expectedXExpr
    )
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(
            AspectRatioMode.Vertical916Crop,
            UpscaleTargetResolution.Hd1080p,
            mode,
            centroidX,
            centroidY
        );

        Assert.NotNull(filter);
        Assert.Contains(expectedXExpr, filter);
        Assert.Contains("scale=1080:1920:flags=lanczos", filter);
    }

    [Fact]
    public void AspectRatioFilterBuilder_Square11_SmartTracking_ProducesXAndYClamps()
    {
        var filter = AspectRatioFilterBuilder.BuildFilter(
            AspectRatioMode.Square11,
            UpscaleTargetResolution.Hd1080p,
            SmartTrackingMode.FacePriority,
            actionCentroidX: 0.35,
            actionCentroidY: 0.65
        );

        Assert.NotNull(filter);
        Assert.Contains("crop=w='min(iw,ih)':h='min(iw,ih)'", filter);
        Assert.Contains("x='max(0,min(iw-out_w,iw*0.350-out_w/2))'", filter);
        Assert.Contains("y='max(0,min(ih-out_h,ih*0.650-out_h/2))'", filter);
        Assert.Contains("scale=1080:1080:flags=lanczos", filter);
    }

    [Fact]
    public void AspectRatioFilterBuilder_BuildMicroZoomFilter_CenterCrop_ProducesUniformCrop()
    {
        var filter = AspectRatioFilterBuilder.BuildMicroZoomFilter(
            zoomPercent: 3.0,
            zoomMode: SmartZoomMode.CenterCrop,
            actionCentroidX: 0.3,
            actionCentroidY: 0.4
        );

        Assert.NotNull(filter);
        Assert.Equal("crop=w='iw*0.97':h='ih*0.97'", filter);
    }

    [Fact]
    public void AspectRatioFilterBuilder_BuildMicroZoomFilter_ActionAnchored_ProducesActionClamps()
    {
        var filter = AspectRatioFilterBuilder.BuildMicroZoomFilter(
            zoomPercent: 4.0,
            zoomMode: SmartZoomMode.ActionAnchored,
            actionCentroidX: 0.25,
            actionCentroidY: 0.70
        );

        Assert.NotNull(filter);
        Assert.Contains("crop=w='iw*0.96':h='ih*0.96'", filter);
        Assert.Contains("x='max(0,min(iw-out_w,iw*0.250-out_w/2))'", filter);
        Assert.Contains("y='max(0,min(ih-out_h,ih*0.700-out_h/2))'", filter);
    }

    [Fact]
    public void AspectRatioFilterBuilder_BuildMicroZoomFilter_CinematicPushIn_ProducesTimeExpression()
    {
        var filter = AspectRatioFilterBuilder.BuildMicroZoomFilter(
            zoomPercent: 5.0,
            zoomMode: SmartZoomMode.CinematicPushIn,
            actionCentroidX: 0.60,
            actionCentroidY: 0.40
        );

        Assert.NotNull(filter);
        Assert.Contains("sin(t/10)", filter);
        Assert.Contains("x='max(0,min(iw-out_w,iw*0.600-out_w/2+sin(t/10)*15))'", filter);
        Assert.Contains("y='max(0,min(ih-out_h,ih*0.400-out_h/2+cos(t/10)*15))'", filter);
    }

    [Fact]
    public void SmartActionAnalyzer_DetectFaceOrSkinCentroid_WithSkinPatch_FindsCentroid()
    {
        int width = 100;
        int height = 100;
        byte[] frame = new byte[width * height * 3];

        // Draw a skin tone rectangle in the top-left quadrant (x: 10..30, y: 10..30)
        // Typical skin tone in BGR: B=110, G=140, R=200
        for (int y = 10; y <= 30; y++)
        {
            for (int x = 10; x <= 30; x++)
            {
                int p = (y * width + x) * 3;
                frame[p] = 110; // Blue
                frame[p + 1] = 140; // Green
                frame[p + 2] = 200; // Red
            }
        }

        var centroid = SmartActionAnalyzer.DetectFaceOrSkinCentroid(frame, width, height);

        Assert.NotNull(centroid);
        // Expected centroid around x=20/100=0.20, y=20/100=0.20
        Assert.InRange(centroid.Value.X, 0.15, 0.25);
        Assert.InRange(centroid.Value.Y, 0.15, 0.25);
    }

    [Fact]
    public void SmartActionAnalyzer_AnalyzeFrame_MotionCentroid_TracksMovingObject()
    {
        int width = 100;
        int height = 100;
        byte[] prevFrame = new byte[width * height * 3];
        byte[] currFrame = new byte[width * height * 3];

        // PrevFrame: uniform gray
        Array.Fill(prevFrame, (byte)128);
        Array.Fill(currFrame, (byte)128);

        // CurrFrame: bright moving object on the right side (x: 70..90, y: 40..60)
        for (int y = 40; y <= 60; y++)
        {
            for (int x = 70; x <= 90; x++)
            {
                int p = (y * width + x) * 3;
                currFrame[p] = 255;
                currFrame[p + 1] = 255;
                currFrame[p + 2] = 255;
            }
        }

        var (centroidX, centroidY, isCut) = SmartActionAnalyzer.AnalyzeFrame(
            currFrame,
            width,
            height,
            SmartTrackingMode.MotionCentroid,
            prevFrame
        );

        Assert.False(isCut);
        // Centroid of moving object is at x=80/100=0.80, y=50/100=0.50
        Assert.InRange(centroidX, 0.70, 0.90);
        Assert.InRange(centroidY, 0.40, 0.60);
    }

    [Fact]
    public void SmartActionAnalyzer_AnalyzeFrame_SceneCut_DetectedWhenThresholdExceeded()
    {
        int width = 100;
        int height = 100;
        byte[] prevFrame = new byte[width * height * 3];
        byte[] currFrame = new byte[width * height * 3];

        // Entire scene changes from black to white (>50% difference)
        Array.Fill(prevFrame, (byte)10);
        Array.Fill(currFrame, (byte)240);

        var (x, y, isCut) = SmartActionAnalyzer.AnalyzeFrame(
            currFrame,
            width,
            height,
            SmartTrackingMode.MotionCentroid,
            prevFrame
        );

        Assert.True(isCut);
    }
}
