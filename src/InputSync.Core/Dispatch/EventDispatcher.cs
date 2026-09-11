using System.Runtime.InteropServices;
using InputSync.Core.Models;
using InputSync.Core.Queue;

namespace InputSync.Core.Dispatch;

public sealed class EventDispatcher : IAsyncDisposable
{
    private readonly object _lifecycleGate = new();
    private readonly BoundedEventQueue _queue;
    private readonly ITargetAdapter[] _targets;
    private readonly Func<nint, bool> _isWindow;
    private CancellationTokenSource? _dispatchCancellation;
    private Task? _dispatchTask;
    private long _eventsDispatched;
    private int _activeTargets;
    private int _lostTargets;
    private bool _disposed;

    public EventDispatcher(
        BoundedEventQueue queue,
        IEnumerable<ITargetAdapter> targets,
        Func<nint, bool>? windowValidator = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        ArgumentNullException.ThrowIfNull(targets);
        _targets = targets.ToArray();
        _isWindow = windowValidator ?? IsWindow;
    }

    public long EventsDispatched => Interlocked.Read(ref _eventsDispatched);

    public int ActiveTargets => Volatile.Read(ref _activeTargets);

    public int LostTargets => Volatile.Read(ref _lostTargets);

    public void Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleGate)
        {
            if (_dispatchCancellation is not null)
            {
                return;
            }

            _dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = _dispatchCancellation.Token;
            _dispatchTask = Task.Run(() => DispatchLoopAsync(token), CancellationToken.None);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cancellation;
        Task? dispatchTask;

        lock (_lifecycleGate)
        {
            cancellation = _dispatchCancellation;
            dispatchTask = _dispatchTask;
            if (cancellation is null)
            {
                return;
            }

            _dispatchCancellation = null;
            _dispatchTask = null;
            cancellation.Cancel();
        }

        try
        {
            if (dispatchTask is not null)
            {
                await dispatchTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
            Volatile.Write(ref _activeTargets, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (NormalizedInputEvent inputEvent in _queue.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                Dispatch(inputEvent, cancellationToken);
                _queue.RecordLatency(inputEvent);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Dispatch(NormalizedInputEvent inputEvent, CancellationToken cancellationToken)
    {
        int activeTargets = 0;
        int lostTargets = 0;

        foreach (ITargetAdapter target in _targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (target.TargetHwnd == nint.Zero || !_isWindow(target.TargetHwnd))
                {
                    lostTargets++;
                    continue;
                }

                activeTargets++;
                if (!target.IsSupported())
                {
                    continue;
                }

                if (inputEvent.Type == InputEventType.Keyboard)
                {
                    target.SendKeyboard(inputEvent);
                }
                else
                {
                    target.SendMouse(inputEvent);
                }

                Interlocked.Increment(ref _eventsDispatched);
            }
            catch (Exception)
            {
            }
        }

        Volatile.Write(ref _activeTargets, activeTargets);
        Volatile.Write(ref _lostTargets, lostTargets);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
