using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.Utils;

public static class FileUtils
{
    public static async Task ReplaceFileWithRetryAsync(
        string sourceTempFilePath,
        string destinationFilePath,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Move(sourceTempFilePath, destinationFilePath, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(150 * (attempt + 1), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(150 * (attempt + 1), cancellationToken);
            }
        }

        File.Move(sourceTempFilePath, destinationFilePath, overwrite: true);
    }

    public static async Task TryDeleteWithRetryAsync(
        string filePath,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default
    )
    {
        if (!File.Exists(filePath))
            return;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (Exception) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(100 * (attempt + 1), cancellationToken);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
