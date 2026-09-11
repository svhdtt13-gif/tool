namespace InputSync.Core.Text;

/// <summary>
/// Coalesces rapid text-sync triggers into one trailing run, so the source
/// application has finished processing the input before we read its text.
/// Repeats can never be lost: diffs are state-based, and Flush runs any
/// pending sync inline with mutual exclusion against scheduled runs.
/// </summary>
public sealed class DebouncedTextSync : IDisposable
{
    private readonly Action _sync;
    private readonly int _delayMs;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _paused;
    private bool _disposed;

    public DebouncedTextSync(Action sync, int delayMs = 25)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delayMs);
        _delayMs = delayMs;
    }

    public void Trigger()
    {
        CancellationTokenSource? previous = null;
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (_disposed || _paused)
            {
                return;
            }

            previous = _pending;
            _pending = new CancellationTokenSource();
            token = _pending.Token;
            generation = ++_generation;
        }

        CancelSilently(previous);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delayMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            FireIfCurrent(generation);
        }, CancellationToken.None);
    }

    public void Flush()
    {
        CancellationTokenSource? previous = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _pending;
            _pending = null;
            _generation++;
        }

        CancelSilently(previous);
        RunSync();
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            CancellationTokenSource? previous = _pending;
            _pending = null;
            _generation++;
            CancelSilently(previous);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            _paused = false;
        }

        Trigger();
    }

    public void Dispose()
    {
        CancellationTokenSource? previous = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            previous = _pending;
            _pending = null;
        }

        CancelSilently(previous);
        GC.SuppressFinalize(this);
    }

    private void FireIfCurrent(long generation)
    {
        lock (_gate)
        {
            if (_disposed || _paused || generation != _generation)
            {
                return;
            }

            _pending = null;
        }

        RunSync();
    }

    private void RunSync()
    {
        try
        {
            _sync();
        }
        catch
        {
        }
    }

    private static void CancelSilently(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
            source?.Dispose();
        }
        catch
        {
        }
    }
}
