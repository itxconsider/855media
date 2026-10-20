using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _855Media.Core.Resolving;

namespace _855Media.Core.Downloading;

public static class MediaTextFile
{
    public static Task SaveAsync(
        string mediaPath,
        VideoInfo media,
        string? translatedTitle = null,
        CancellationToken cancellationToken = default
    )
    {
        // An explicitly empty caption means the photo has no available post text.
        // Keep the generated filename/title out of that photo's caption sidecar.
        var content = media.Caption ?? translatedTitle ?? media.Title;
        return string.IsNullOrWhiteSpace(content)
            ? Task.CompletedTask
            : File.WriteAllTextAsync(
                Path.ChangeExtension(mediaPath, ".txt"),
                content,
                new UTF8Encoding(false),
                cancellationToken
            );
    }
}
