using System;
using System.Globalization;

namespace _855Media.Core.Editing;

internal static class EditorThreadPolicy
{
    // FFmpeg otherwise creates a full CPU-sized filter pool for every queue worker.
    // A small pool avoids oversubscription while retaining every filter and frame.
    internal static int FilterThreads(int logicalCores) => Math.Clamp(logicalCores / 2, 1, 4);

    internal static string[] FilterArguments(int logicalCores)
    {
        var threads = FilterThreads(logicalCores).ToString(CultureInfo.InvariantCulture);
        return ["-filter_threads", threads, "-filter_complex_threads", threads];
    }
}
