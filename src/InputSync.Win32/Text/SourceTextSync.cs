using InputSync.Core.Text;

namespace InputSync.Win32;

/// <summary>
/// Mirrors observed source text to targets: snapshots readable source text,
/// diffs each change and emits it as plain characters. First read only
/// establishes the baseline and emits nothing. All I/O goes through injected
/// delegates, so the diff logic is fully unit-testable.
/// </summary>
public sealed class SourceTextSync
{
    private readonly Func<string?> _readText;
    private readonly Action<string> _emitText;
    private readonly int _maxLength;
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

        if (_snapshot is null)
        {
            _snapshot = Truncate(current);
            return string.Empty;
        }

        TextEdit edit = TextDiffer.Compute(_snapshot, current, _maxLength);
        _snapshot = Truncate(current);
        if (edit.IsEmpty)
        {
            return string.Empty;
        }

        string payload = (edit.Backspaces > 0 ? new string('\b', edit.Backspaces) : string.Empty)
            + edit.Inserted;
        try
        {
            _emitText(payload);
        }
        catch
        {
            return string.Empty;
        }

        return payload;
    }

    public void Adopt()
    {
        string? current = Read();
        if (current is not null)
        {
            _snapshot = Truncate(current);
        }
    }

    public bool AdoptUntilChanged(int timeoutMs = 300, int pollMs = 20)
    {
        if (_snapshot is null)
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

            if (!string.Equals(current, _snapshot, StringComparison.Ordinal))
            {
                _snapshot = Truncate(current);
                return true;
            }

            System.Threading.Thread.Sleep(pollMs);
        }

        return false;
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

    public void Reset() => _snapshot = null;

    private string Truncate(string text) =>
        text.Length > _maxLength ? text[^_maxLength..] : text;
}
