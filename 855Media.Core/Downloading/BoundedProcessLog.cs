using System;
using System.Collections.Generic;

namespace _855Media.Core.Downloading;

internal sealed class BoundedProcessLog
{
    private const int MaxLines = 128;
    private const int MaxLineLength = 4096;
    private readonly Queue<string> _lines = new();
    private readonly object _sync = new();

    public void Add(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_sync)
        {
            if (_lines.Count == MaxLines)
                _lines.Dequeue();
            _lines.Enqueue(line.Length > MaxLineLength ? line[..MaxLineLength] : line);
        }
    }

    public override string ToString()
    {
        lock (_sync)
            return string.Join(Environment.NewLine, _lines);
    }
}
