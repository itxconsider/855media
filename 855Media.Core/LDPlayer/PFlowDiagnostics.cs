using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace _855Media.Core.LDPlayer;

public static class PFlowDiagnostics
{
    public static async Task CaptureAsync(ILDPlayerService ld, int index, string step, string? path)
    {
        // A failed diagnostic must never replace the original posting/navigation error.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var directory = Path.Combine(Path.GetDirectoryName(PFlowTrace.PathName)!, "p-flow-ui");
            Directory.CreateDirectory(directory);
            var safeStep = string.Concat(step.Where(c => char.IsLetterOrDigit(c) || c == '-'));
            var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-LD-{index}-{safeStep}";
            var xml = await ld.DumpUiHierarchyAsync(index, path, timeout.Token);
            if (!string.IsNullOrWhiteSpace(xml))
                await File.WriteAllTextAsync(
                    Path.Combine(directory, name + ".xml"),
                    PFlowUiPrivacy.RedactForDiagnostics(xml),
                    timeout.Token
                );
            // Do not capture screenshots when editable inputs could expose login/OTP data.
            var image = PFlowUiPrivacy.ContainsInput(xml)
                ? null
                : await ld.TakeScreenshotAsync(index, path, timeout.Token);
            if (image?.Length > 0)
                await File.WriteAllBytesAsync(
                    Path.Combine(directory, name + ".png"),
                    image,
                    timeout.Token
                );
            await PFlowTrace.SessionAsync(
                index,
                "diagnostic.saved",
                Path.Combine(directory, name),
                timeout.Token
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"P-Flow diagnostic capture failed: {ex.Message}");
        }
    }
}
