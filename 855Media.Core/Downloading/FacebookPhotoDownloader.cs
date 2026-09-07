using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using Gress;

namespace _855Media.Core.Downloading;

public class FacebookPhotoDownloader
{
    private static readonly HttpClientHandler SharedHandler = new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
    };

    private static readonly HttpClient SharedClient = new(SharedHandler)
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    public async Task DownloadPhotoAsync(
        string filePath,
        VideoInfo photo,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var imageUrls = photo.ThumbnailUrls;
        if (imageUrls.Count == 0 && !string.IsNullOrWhiteSpace(photo.Url))
        {
            imageUrls = [photo.Url];
        }

        if (imageUrls.Count == 0)
            throw new InvalidOperationException("No image URL available for this Facebook photo.");

        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        Exception? lastException = null;
        var downloadedSuccessfully = false;

        foreach (var imageUrl in imageUrls)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                continue;

            // Strategy 1: Browser headers with Facebook referer (preferring standard JPEG/PNG)
            try
            {
                await DownloadInternalAsync(
                    imageUrl,
                    filePath,
                    useReferer: true,
                    progress,
                    cancellationToken
                );
                downloadedSuccessfully = true;
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            // Strategy 2: Without referer
            try
            {
                await DownloadInternalAsync(
                    imageUrl,
                    filePath,
                    useReferer: false,
                    progress,
                    cancellationToken
                );
                downloadedSuccessfully = true;
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (!downloadedSuccessfully)
        {
            if (File.Exists(filePath) && new FileInfo(filePath).Length == 0)
            {
                try
                {
                    File.Delete(filePath);
                }
                catch { }
            }

            throw lastException
                ?? new InvalidOperationException(
                    "Failed to download Facebook photo from all candidate URLs."
                );
        }
    }

    private static async Task DownloadInternalAsync(
        string url,
        string destinationPath,
        bool useReferer,
        IProgress<Percentage>? progress,
        CancellationToken cancellationToken
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
        );
        // Explicitly request standard JPEG/PNG so CDNs don't send AVIF/WebP containers
        request.Headers.Accept.ParseAdd("image/jpeg,image/png,image/*;q=0.8");
        request.Headers.TryAddWithoutValidation(
            "sec-ch-ua",
            "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\""
        );
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "image");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "no-cors");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "cross-site");

        if (useReferer)
        {
            request.Headers.Referrer = new Uri("https://www.facebook.com/");
        }

        using var response = await SharedClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var tempPath = destinationPath + ".tmp";

        try
        {
            await using (
                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken)
            )
            await using (
                var fileStream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true
                )
            )
            {
                var buffer = new byte[81920];
                long totalBytesRead = 0;
                int bytesRead;

                while (
                    (
                        bytesRead = await contentStream.ReadAsync(
                            buffer.AsMemory(0, buffer.Length),
                            cancellationToken
                        )
                    ) > 0
                )
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytesRead += bytesRead;

                    if (totalBytes is > 0)
                    {
                        var percentage = Math.Clamp(
                            (double)totalBytesRead / totalBytes.Value,
                            0.0,
                            1.0
                        );
                        progress?.Report(Percentage.FromFraction(percentage));
                    }
                }
            }

            var tempFileInfo = new FileInfo(tempPath);
            if (tempFileInfo.Length == 0)
            {
                throw new InvalidOperationException("Downloaded image file was empty.");
            }

            // Verify file magic bytes. If it is AVIF, HEIC, WebP or ISOBMFF container (starts with ftyp/RIFF),
            // convert it using FFmpeg so Windows Photo Viewer and all standard viewers can open it as JPEG!
            var isStandardJpegOrPng = false;
            if (tempFileInfo.Length >= 4)
            {
                byte[] header = new byte[4];
                await using (var fs = File.OpenRead(tempPath))
                {
                    _ = await fs.ReadAsync(header.AsMemory(0, 4), cancellationToken);
                }

                // JPEG starts with 0xFF, 0xD8, 0xFF
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                {
                    isStandardJpegOrPng = true;
                }
                // PNG starts with 0x89, 0x50, 0x4E, 0x47
                else if (
                    header[0] == 0x89
                    && header[1] == 0x50
                    && header[2] == 0x4E
                    && header[3] == 0x47
                )
                {
                    isStandardJpegOrPng = true;
                }
            }

            if (!isStandardJpegOrPng)
            {
                // Attempt conversion with FFmpeg to standard JPEG
                var ffmpegPath = FFmpeg.TryGetCliFilePath();
                if (!string.IsNullOrWhiteSpace(ffmpegPath))
                {
                    try
                    {
                        if (File.Exists(destinationPath))
                            File.Delete(destinationPath);

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = ffmpegPath,
                            Arguments =
                                $"-y -i \"{tempPath}\" -frames:v 1 -f image2 -vcodec mjpeg -update 1 -q:v 2 \"{destinationPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true,
                        };

                        using var proc = Process.Start(startInfo);
                        if (proc is not null)
                        {
                            await proc.WaitForExitAsync(cancellationToken);
                            if (
                                proc.ExitCode == 0
                                && File.Exists(destinationPath)
                                && new FileInfo(destinationPath).Length > 0
                            )
                            {
                                progress?.Report(Percentage.FromFraction(1.0));
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback to direct move if conversion fails
                    }
                }
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
            progress?.Report(Percentage.FromFraction(1.0));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch { }
            }
        }
    }
}
