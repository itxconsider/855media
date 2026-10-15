using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace _855Media.Core.LDPlayer;

public sealed record FacebookLayoutLoadResult(
    bool Applied,
    string Message,
    int ActiveVariants,
    Guid Generation,
    string Source
);

public static partial class FacebookLayoutRegistry
{
    private static readonly Lazy<FacebookLayoutCatalog> BuiltInCatalog = new(
        FacebookLayoutCatalog.BuiltIn
    );
    private static readonly object ConfigurationGate = new();
    private static FacebookLayoutCatalog? _loadedCatalog;
    private static FacebookLayoutLoadResult? _lastLoad;

    public static string ConfigurationPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "855Media",
            "layouts",
            "facebook-layouts.json"
        );
    public static IReadOnlyList<FacebookLayoutDefinition> Definitions =>
        CaptureCatalog().Definitions;

    internal static FacebookLayoutCatalog CaptureCatalog() =>
        Volatile.Read(ref _loadedCatalog) ?? BuiltInCatalog.Value;

    public static FacebookLayoutLoadResult ConfigurationStatus =>
        Volatile.Read(ref _lastLoad)
        ?? Result(true, "Using the built-in layout rules.", CaptureCatalog());

    public static FacebookLayoutLoadResult InitializeConfiguration(
        string? directory = null,
        string? bundledFile = null
    )
    {
        var file = Path.Combine(
            directory ?? Path.GetDirectoryName(ConfigurationPath)!,
            "facebook-layouts.json"
        );
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if (!File.Exists(file))
            {
                bundledFile ??= Path.Combine(
                    AppContext.BaseDirectory,
                    "layouts",
                    "facebook-layouts.json"
                );
                var json = File.Exists(bundledFile)
                    ? ReadRulesFile(bundledFile)
                    : FacebookLayoutCatalog.CreateDefaultJson();
                _ = FacebookLayoutCatalog.Parse(json);
                try
                {
                    using var stream = new FileStream(
                        file,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read
                    );
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    writer.Write(json);
                }
                catch (IOException) when (File.Exists(file))
                { /* Another initializer created it. */
                }
            }
            return ReloadConfiguration(file);
        }
        catch (Exception ex)
            when (ex
                    is IOException
                        or UnauthorizedAccessException
                        or FormatException
                        or ArgumentException
            )
        {
            return RecordFailure(ex);
        }
    }

    public static FacebookLayoutLoadResult ReloadConfiguration(string? file = null)
    {
        lock (ConfigurationGate)
        {
            try
            {
                var path = Path.GetFullPath(file ?? ConfigurationPath);
                var next = FacebookLayoutCatalog.Parse(ReadRulesFile(path), path);
                Volatile.Write(ref _loadedCatalog, next);
                var result = Result(
                    true,
                    $"Loaded {next.Definitions.Count} layout variants from JSON.",
                    next
                );
                Volatile.Write(ref _lastLoad, result);
                return result;
            }
            catch (Exception ex)
                when (ex
                        is IOException
                            or UnauthorizedAccessException
                            or FormatException
                            or ArgumentException
                )
            {
                return RecordFailure(ex);
            }
        }
    }

    public static FacebookLayoutLoadResult UseBuiltInConfiguration()
    {
        lock (ConfigurationGate)
        {
            var next = FacebookLayoutCatalog.BuiltIn();
            Volatile.Write(ref _loadedCatalog, next);
            var result = Result(
                true,
                "Using built-in rules for this session; the JSON file was kept.",
                next
            );
            Volatile.Write(ref _lastLoad, result);
            return result;
        }
    }

    private static FacebookLayoutLoadResult RecordFailure(Exception ex)
    {
        var detail =
            ex is FormatException ? ex.Message : "The layout file could not be read or created.";
        var result = Result(
            false,
            "Rules not reloaded: " + detail + " Previous valid rules remain active.",
            CaptureCatalog()
        );
        Volatile.Write(ref _lastLoad, result);
        return result;
    }

    private static FacebookLayoutLoadResult Result(
        bool applied,
        string message,
        FacebookLayoutCatalog catalog
    ) => new(applied, message, catalog.Definitions.Count, catalog.Generation, catalog.Source);

    private static string ReadRulesFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        var buffer = new byte[FacebookLayoutCatalog.MaximumFileBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = stream.Read(buffer, length, buffer.Length - length);
            if (read == 0)
                break;
            length += read;
        }
        if (length > FacebookLayoutCatalog.MaximumFileBytes)
            throw new FormatException("Layout file exceeds the 1 MiB limit.");
        var start =
            length >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(buffer, start, length - start);
        }
        catch (DecoderFallbackException)
        {
            throw new FormatException("Layout file must be valid UTF-8 JSON.");
        }
    }

    internal static FacebookLayoutMatch MatchCatalog(
        FacebookLayoutCatalog catalog,
        string? xml,
        FacebookScreenKind? expected,
        FacebookLayoutContext? context
    )
    {
        using var scope = FacebookHierarchyCache.Enter();
        return FacebookHierarchyCache.Memo(
            xml,
            ("layout-match", catalog.Generation, expected, context),
            () => Match(FacebookLayoutObservation.ForCatalog(xml, context, catalog), expected)
        );
    }

    private static IEnumerable<(int X, int Y)> ConfiguredControls(
        FacebookLayoutObservation o,
        FacebookControlRole role,
        FacebookLayoutDefinition definition
    )
    {
        var existing = FindControls(o, role, definition.Screen).Distinct().ToArray();
        if (existing.Length > 0)
            return existing; // Never mask ambiguous native controls with a configurable fallback.
        var origin = o.Catalog.FindOrigin(definition.Id);
        if (origin == null || !origin.Aliases.TryGetValue(role, out var aliases))
            return existing;
        return o
            .Nodes.Where(n =>
                (string?)n.Attribute("package") is null or "com.facebook.katana"
                && new[] { (string?)n.Attribute("text"), (string?)n.Attribute("content-desc") }.Any(
                    label =>
                        aliases.Any(alias =>
                            string.Equals(label?.Trim(), alias, StringComparison.OrdinalIgnoreCase)
                        )
                )
            )
            .Select(n => UsableCenter(n))
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .ToArray();
    }
}
