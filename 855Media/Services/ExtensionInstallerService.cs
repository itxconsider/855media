using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using _855Media.Framework;
using _855Media.Localization;
using _855Media.Utils.Extensions;
using PowerKit.Extensions;

namespace _855Media.Services;

public class ExtensionInstallerService
{
    private readonly SnackbarManager _snackbarManager;
    private readonly LocalizationManager _localizationManager;

    public ExtensionInstallerService(
        SnackbarManager snackbarManager,
        LocalizationManager localizationManager
    )
    {
        _snackbarManager = snackbarManager;
        _localizationManager = localizationManager;
    }

    public async Task InstallExtensionsAsync()
    {
        await Task.Run(() =>
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Program.Name,
                "extensions"
            );
            Directory.CreateDirectory(appDataDir);

            string[] extensionFolders = ["facebook-bulk-downloader-v1", "facebook-photo-exporter"];

            foreach (var folder in extensionFolders)
            {
                var sourcePath = FindSourceExtensionDirectory(folder);
                if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
                    continue;

                var destPath = Path.Combine(appDataDir, folder);
                CopyDirectory(sourcePath, destPath);
            }

            // Open File Explorer to the target extensions directory
            if (OperatingSystem.IsWindows() && Directory.Exists(appDataDir))
            {
                try
                {
                    Process.Start("explorer", [appDataDir]);
                }
                catch
                {
                    // Ignore explorer launch error
                }
            }

            // Attempt to open chrome://extensions in default browser
            try
            {
                Process.StartShellExecute("https://chrome.google.com/webstore");
            }
            catch
            {
                // Ignore browser launch error
            }
        });

        _snackbarManager.Notify(
            "Browser extension files installed to AppData! Enable 'Developer mode' & click 'Load unpacked' in Chrome/Edge."
        );
    }

    private static string? FindSourceExtensionDirectory(string folderName)
    {
        var appDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, "extensions", folderName),
            Path.Combine(appDir, folderName),
            Path.Combine(appDir, "..", "..", "..", "..", folderName),
            Path.Combine(Directory.GetCurrentDirectory(), folderName),
            Path.Combine(Directory.GetCurrentDirectory(), "..", folderName),
        };

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
