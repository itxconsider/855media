using System;
using System.Diagnostics;
using System.IO;

namespace _855Media.Services;

public class FacebookBrowserLauncher
{
    public bool LaunchBrowserWithExtension(string targetUrl)
    {
        var appDataExtDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Program.Name,
            "extensions",
            "facebook-bulk-downloader-v1"
        );

        // Ensure extension directory exists
        if (!Directory.Exists(appDataExtDir))
        {
            var fallbackSource = FindSourceExtensionDirectory();
            if (!string.IsNullOrWhiteSpace(fallbackSource))
            {
                CopyDirectory(fallbackSource, appDataExtDir);
            }
        }

        var chromePath = FindBrowserExecutable();
        if (string.IsNullOrWhiteSpace(chromePath) || !File.Exists(chromePath))
            return false;

        try
        {
            var arguments = $"--load-extension=\"{appDataExtDir}\" \"{targetUrl}\"";
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = chromePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                }
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindBrowserExecutable()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData
        );
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        string[] candidates =
        [
            Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"),
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string? FindSourceExtensionDirectory()
    {
        var appDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(appDir, "extensions", "facebook-bulk-downloader-v1"),
            Path.Combine(appDir, "facebook-bulk-downloader-v1"),
            Path.Combine(appDir, "..", "..", "..", "..", "facebook-bulk-downloader-v1"),
            Path.Combine(Directory.GetCurrentDirectory(), "facebook-bulk-downloader-v1"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "facebook-bulk-downloader-v1"),
        ];

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "manifest.json")))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir);
        }
    }
}
