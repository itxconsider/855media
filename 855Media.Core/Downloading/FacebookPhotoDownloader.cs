using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;
using _855Media.Core.Utils;
using Gress;
using Gress.Integrations;

namespace _855Media.Core.Downloading;

public class FacebookPhotoDownloader(IReadOnlyList<Cookie>? cookies = null)
{
    public async Task DownloadPhotoAsync(
        string filePath,
        VideoInfo photo,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var imageUrls = photo.ThumbnailUrls;
        if (imageUrls.Count == 0)
            throw new InvalidOperationException("No image URL available for this Facebook photo.");

        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        if (cookies?.Any() == true)
        {
            var container = new CookieContainer();
            foreach (var cookie in cookies.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
            {
                try
                {
                    var domain = cookie.Domain.StartsWith("#HttpOnly_", StringComparison.Ordinal)
                        ? cookie.Domain["#HttpOnly_".Length..]
                        : cookie.Domain;

                    var copy = new Cookie(cookie.Name, cookie.Value, cookie.Path, domain)
                    {
                        Secure = cookie.Secure,
                        HttpOnly = cookie.HttpOnly,
                        Expires = cookie.Expires,
                    };

                    var host = domain.TrimStart('.').Replace("#HttpOnly_", "");
                    if (!Uri.TryCreate($"https://{host}", UriKind.Absolute, out var cookieUri))
                        cookieUri = new Uri("https://www.facebook.com");

                    container.Add(cookieUri, copy);
                }
                catch (CookieException)
                {
                    // Ignore cookies rejected by CookieContainer.
                }
            }

            handler.CookieContainer = container;
        }

        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
        );
        http.DefaultRequestHeaders.Referrer = new Uri("https://www.facebook.com/");
        http.DefaultRequestHeaders.Accept.ParseAdd(
            "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8"
        );

        Exception? lastException = null;
        var downloadedSuccessfully = false;

        foreach (var imageUrl in imageUrls)
        {
            try
            {
                await http.DownloadAsync(imageUrl, filePath, progress, cancellationToken);
                downloadedSuccessfully = true;
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        if (!downloadedSuccessfully)
            throw lastException
                ?? new InvalidOperationException("Failed to download Facebook photo.");

        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
                File.Delete(filePath);
        }
    }
}
