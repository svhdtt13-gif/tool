namespace InputSync.Core.Text;

/// <summary>
/// Coalesces rapid text-sync triggers into one trailing run on a single
/// sequential worker, so the source application has finished processing
/// the input before we read its text. No Task is spawned per keystroke.
/// Triggers arriving while paused are remembered and run once on Resume,
/// so pausing (e.g. around clipboard paste) never loses input.
/// Repeats can never be lost: diffs are state-based, and Flush runs any
/// pending sync inline with mutual exclusion against the worker.
/// </summary>
public sealed class DebouncedTextSync : IDisposable
{
    private readonly Action _sync;
    private readonly int _delayMs;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private long _generation;
    private bool _pending;
    private bool _paused;
    private bool _disposed;

    public DebouncedTextSync(Action sync, int delayMs = 25)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(delayMs);
        _delayMs = delayMs;
        _worker = Task.Run(WorkerLoopAsync);
    }

    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = true;
            _generation++;
        }

        _signal.Set();
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _generation++;
            _pending = false;
        }

        RunSync();
    }

    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            _generation++;
        }
    }

    public void Resume()
    {
        bool trigger;
        lock (_gate)
        {
            _paused = false;
            trigger = _pending;
        }

        if (trigger)
        {
            Trigger();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = false;
            _generation++;
        }

        try
        {
            _lifetime.Cancel();
        }
        catch
        {
        }

        _signal.Set();
        _signal.Dispose();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (!_lifetime.Token.IsCancellationRequested)
            {
                _signal.Wait(_lifetime.Token);
                _signal.Reset();

                long generation;
                lock (_gate)
                {
                    generation = _generation;
                }

                try
                {
                    await Task.Delay(_delayMs, _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                bool run;
                lock (_gate)
                {
                    run = generation == _generation && !_paused && !_disposed;
                    if (run)
                    {
                        _pending = false;
                    }
                }

                if (run)
                {
                    RunSync();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
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
}
