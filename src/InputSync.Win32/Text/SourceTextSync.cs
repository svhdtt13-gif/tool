using InputSync.Core.Text;

namespace InputSync.Win32;

/// <summary>
/// Mirrors observed source text to targets: snapshots readable source text,
/// diffs each change and emits it as plain characters. First read only
/// establishes the baseline and emits nothing. Thread-safe: pump, debounce
/// and monitor threads share one instance.
/// </summary>
public sealed class SourceTextSync
{
    private readonly Func<string?> _readText;
    private readonly Action<string> _emitText;
    private readonly int _maxLength;
    private readonly object _gate = new();
    private string? _snapshot;

    public SourceTextSync(Func<string?> readText, Action<string> emitText, int maxLength = 30000)
    {
        _readText = readText ?? throw new ArgumentNullException(nameof(readText));
        _emitText = emitText ?? throw new ArgumentNullException(nameof(emitText));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        _maxLength = maxLength;
    }

    public string Sync()
    {
        string? current = Read();
        if (current is null)
        {
            return string.Empty;
        }

        return EmitFor(Truncate(current));
    }

    public string SyncStable(int pollMs = 10, int timeoutMs = 300)
    {
        string? first = Read();
        if (first is null)
        {
            return string.Empty;
        }

        string previous = Truncate(first);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            System.Threading.Thread.Sleep(pollMs);
            string? current = Read();
            if (current is null)
            {
                return string.Empty;
            }

            string truncated = Truncate(current);
            if (string.Equals(truncated, previous, StringComparison.Ordinal))
            {
                break;
            }

            previous = truncated;
        }

        return EmitFor(previous);
    }

    private string EmitFor(string current)
    {
        string payload;
        lock (_gate)
        {
            if (_snapshot is null)
            {
                _snapshot = current;
                return string.Empty;
            }

            TextEdit edit = TextDiffer.Compute(_snapshot, current, _maxLength);
            _snapshot = current;
            if (edit.IsEmpty)
            {
                return string.Empty;
            }

            payload = (edit.Backspaces > 0 ? new string('\b', edit.Backspaces) : string.Empty)
                + edit.Inserted;
            try
            {
                _emitText(payload);
            }
            catch
            {
                return string.Empty;
            }
        }

        return payload;
    }

    public void Adopt()
    {
        string? current = Read();
        if (current is not null)
        {
            lock (_gate)
            {
                _snapshot = Truncate(current);
            }
        }
    }

    public bool AdoptUntilChanged(int timeoutMs = 300, int pollMs = 20)
    {
        if (Snapshot is null)
        {
            Adopt();
            return false;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            string? current = Read();
            if (current is null)
            {
                return false;
            }

            lock (_gate)
            {
                if (_snapshot is not null
                    && !string.Equals(current, _snapshot, StringComparison.Ordinal))
                {
                    _snapshot = Truncate(current);
                    return true;
                }
            }

            System.Threading.Thread.Sleep(pollMs);
        }

        return false;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }

    private string? Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    private string? Read()
    {
        try
        {
            return _readText();
        }
        catch
        {
            return null;
        }
    }

    private string Truncate(string text) =>
        text.Length > _maxLength ? text[^_maxLength..] : text;
}
