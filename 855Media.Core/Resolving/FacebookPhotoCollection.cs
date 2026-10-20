using System;
using System.Linq;

namespace _855Media.Core.Resolving;

public static class FacebookPhotoCollection
{
    public static bool TryGetGalleryUrl(string query, out string galleryUrl)
    {
        galleryUrl = string.Empty;
        var text = query.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (
            !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || !(
                uri.Host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;
        var first = parts[0].ToLowerInvariant();
        var builder = new UriBuilder(uri)
        {
            Scheme = "https",
            Port = -1,
            Fragment = string.Empty,
        };
        if (first == "profile.php")
        {
            var id = uri
                .Query.TrimStart('?')
                .Split('&')
                .FirstOrDefault(p => p.StartsWith("id=", StringComparison.OrdinalIgnoreCase));
            if (id is null || !id[3..].All(char.IsAsciiDigit) || id.Length <= 3)
                return false;
            builder.Query = id + "&sk=photos";
        }
        else if (
            first is "people" or "pages"
            && parts.Length >= 3
            && parts[2].All(char.IsAsciiDigit)
        )
        {
            builder.Path = "/profile.php";
            builder.Query = "id=" + parts[2] + "&sk=photos";
        }
        else if (first is "photo.php" or "photo" or "media")
        {
            // Preserve individual photo and album links when users paste those instead.
        }
        else if (
            first
            is "reel"
                or "reels"
                or "watch"
                or "videos"
                or "share"
                or "groups"
                or "events"
                or "marketplace"
                or "login"
                or "login.php"
                or "checkpoint"
                or "home.php"
        )
            return false;
        else
        {
            if (parts.Length == 1)
                builder.Path = "/" + parts[0] + "/photos";
            else if (
                !parts[1].StartsWith("photos", StringComparison.OrdinalIgnoreCase)
                && !parts[1].Equals("albums", StringComparison.OrdinalIgnoreCase)
            )
                return false;
        }

        galleryUrl = builder.Uri.AbsoluteUri;
        return true;
    }
}
