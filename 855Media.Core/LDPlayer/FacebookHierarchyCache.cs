using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace _855Media.Core.LDPlayer;

// A synchronous recognition call owns this scope. Nothing is cached across captures.
// Readers share documents; writers (including diagnostic redaction) must parse their own copy.
internal sealed class FacebookHierarchyCache : IDisposable
{
    [ThreadStatic]
    private static FacebookHierarchyCache? _current;
    private readonly FacebookHierarchyCache? _previous;
    private readonly Dictionary<string, XDocument> _strict = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<XDocument>> _safe = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XElement[]> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Xml, object Key), object?> _results = [];
    private readonly Dictionary<(string Xml, string? Activity), BloksScreenInfo> _screens = [];
    private readonly Dictionary<string, Capture> _captures = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<BloksScreenInfo, Capture> Captures = new();

    private sealed class Capture(string xml, XDocument document)
    {
        internal string Xml { get; } = xml;
        internal XDocument Document { get; } = document;
        internal Lazy<XElement[]> Nodes { get; } =
            new(() => document.Descendants("node").ToArray());
    }

    internal static void Remember(BloksScreenInfo screen, string xml, XDocument document) =>
        Captures.Add(screen, new Capture(xml, document));

    internal static bool CanReuse(BloksScreenInfo screen) =>
        Captures.TryGetValue(screen, out var capture)
        && string.Equals(screen.RawXml, capture.Xml, StringComparison.Ordinal);

    internal static void Seed(BloksScreenInfo? screen)
    {
        if (
            _current == null
            || screen == null
            || !Captures.TryGetValue(screen, out var capture)
            || !string.Equals(screen.RawXml, capture.Xml, StringComparison.Ordinal)
        )
            return;
        _current._strict.TryAdd(capture.Xml, capture.Document);
        _current._captures.TryAdd(capture.Xml, capture);
        if (string.IsNullOrEmpty(screen.CurrentActivity))
            _current._screens.TryAdd((capture.Xml, null), screen);
    }

    private FacebookHierarchyCache()
    {
        _previous = _current;
        _current = this;
    }

    internal static IDisposable Enter() =>
        _current == null ? new FacebookHierarchyCache() : EmptyScope.Instance;

    internal static bool Active => _current != null;

    internal static XDocument ParseStrict(string xml)
    {
        if (_current == null)
            return XDocument.Parse(xml);
        if (!_current._strict.TryGetValue(xml, out var doc))
            _current._strict.Add(xml, doc = XDocument.Parse(xml));
        return doc;
    }

    internal static List<XDocument> Documents(string? xml)
    {
        if (_current == null)
            return PFlowAutoPostService.ParseXmlDocumentsUncached(xml);
        var key = xml ?? "";
        if (!_current._safe.TryGetValue(key, out var docs))
            _current._safe.Add(key, docs = PFlowAutoPostService.ParseXmlDocumentsUncached(xml));
        return docs;
    }

    // Alias the hierarchy substring to the strict document when the full XML is valid.
    // Concatenated dumps and adb prefixes retain the original permissive-parser behavior.
    internal static XDocument ParseHierarchySegment(string raw, string segment, bool single)
    {
        if (_current != null && single)
        {
            var start = raw.IndexOf(segment, StringComparison.Ordinal);
            var prefix = start >= 0 ? raw[..start] : "invalid";
            var suffix = start >= 0 ? raw[(start + segment.Length)..] : "invalid";
            // Only alias a whole XML body. Regex extraction may otherwise stop
            // at a closing tag inside a comment or nested hierarchy.
            if (
                !Regex.IsMatch(prefix, @"^\s*(?:(?:<\?xml[^>]*\?>|<!--[\s\S]*?-->)\s*)*$")
                || !Regex.IsMatch(suffix, @"^\s*(?:<!--[\s\S]*?-->\s*)*$")
            )
                return ParseStrict(segment);
            try
            {
                var doc = ParseStrict(raw);
                if (
                    doc.Root?.Name.LocalName.Equals("hierarchy", StringComparison.OrdinalIgnoreCase)
                    == true
                )
                {
                    _current._strict.TryAdd(segment, doc);
                    return doc;
                }
            }
            catch (System.Xml.XmlException) { }
        }
        return ParseStrict(segment);
    }

    internal static XElement[] Nodes(string? xml)
    {
        if (_current == null)
            return Documents(xml).SelectMany(d => d.Descendants("node")).ToArray();
        var key = xml ?? "";
        if (!_current._nodes.TryGetValue(key, out var nodes))
        {
            var documents = Documents(xml);
            nodes =
                _current._captures.TryGetValue(key, out var capture)
                && documents.Count == 1
                && ReferenceEquals(documents[0], capture.Document)
                    ? capture.Nodes.Value
                    : documents.SelectMany(d => d.Descendants("node")).ToArray();
            _current._nodes.Add(key, nodes);
        }
        return nodes;
    }

    internal static T Memo<T>(string? xml, object key, Func<T> read)
    {
        if (_current == null)
            return read();
        var cacheKey = (xml ?? "", key);
        if (_current._results.TryGetValue(cacheKey, out var value))
            return (T)value!;
        var result = read();
        _current._results.Add(cacheKey, result);
        return result;
    }

    internal static BloksScreenInfo Screen(
        string? xml,
        string? activity,
        Func<BloksScreenInfo> read
    )
    {
        if (_current == null)
            return read();
        var key = (xml ?? "", activity);
        if (!_current._screens.TryGetValue(key, out var screen))
            _current._screens.Add(key, screen = read());
        return screen;
    }

    public void Dispose() => _current = _previous;

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();

        public void Dispose() { }
    }
}
