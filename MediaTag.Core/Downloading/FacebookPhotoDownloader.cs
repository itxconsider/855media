using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gress;
using Gress.Integrations;
using MediaTag.Core.Resolving;
using MediaTag.Core.Utils;

namespace MediaTag.Core.Downloading;

public class FacebookPhotoDownloader
{
    public async Task DownloadPhotoAsync(
        string filePath,
        VideoInfo photo,
        IProgress<Percentage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var imageUrl =
            photo.ThumbnailUrls.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No image URL available for this Facebook photo."
            );

        var dirPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dirPath))
            Directory.CreateDirectory(dirPath);

        await Http.Client.DownloadAsync(imageUrl, filePath, progress, cancellationToken);
    }
}
