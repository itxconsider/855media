using System;
using System.Collections.Generic;
using System.Globalization;

namespace _855Media.Core.Editing;

internal static class TrackingCropPath
{
    internal static string? Axis(ActionAnalysisResult? analysis, bool horizontal)
    {
        var points = analysis?.Trajectory;
        if (points is null || points.Count == 0)
            return null;
        static string N(double n) => n.ToString("0.######", CultureInfo.InvariantCulture);
        double V(ActionTrajectoryPoint p) => horizontal ? p.NormalizedX : p.NormalizedY;
        var terms = new List<string> { N(V(points[0])) };
        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            double seconds = (b.Timestamp - a.Timestamp).TotalSeconds;
            if (seconds <= 0)
                continue;
            double delta = V(b) - V(a);
            if (Math.Abs(delta) < 0.0000001)
                continue;
            // Independent ramps avoid FFmpeg's nested-expression depth limit.
            // Steps reset at cuts and gaps rather than panning across them.
            var weight = b.IsSceneCut
                ? $"gte(t,{N(b.Timestamp.TotalSeconds)})"
                : $"clip((t-{N(a.Timestamp.TotalSeconds)})/{N(seconds)},0,1)";
            terms.Add($"({N(delta)})*{weight}");
        }
        // A balanced sum also bounds parser recursion for long left-associative sums.
        while (terms.Count > 1)
        {
            var next = new List<string>();
            for (int i = 0; i < terms.Count; i += 2)
                next.Add(i + 1 < terms.Count ? $"({terms[i]}+{terms[i + 1]})" : terms[i]);
            terms = next;
        }
        return $"({terms[0]})";
    }
}
