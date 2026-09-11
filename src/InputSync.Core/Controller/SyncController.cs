using System.Threading.Channels;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Core.Safety;

namespace InputSync.Core.Controller;

public enum SyncControllerFault
{
    NONE,
    SOURCE_LOST,
    ADAPTER_UNSUPPORTED,
    DISPATCH_ERROR,
}

public enum SyncHotkeyAction
{
    Toggle,
    Stop,
    EmergencyStop,
}

public sealed class SyncController : IDisposable
{
    private readonly object _stateGate = new();
    private readonly object _dispatchGate = new();
    private readonly ITargetAdapter _adapter;
    private readonly InputStateTracker _stateTracker;
    private readonly Func<nint, bool> _isWindow;
    private readonly Channel<QueuedInput> _channel;
    private readonly Dictionary<nint, TargetStatus> _targets = [];
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private bool _disposed;

    public SyncController(
        ITargetAdapter adapter,
        InputStateTracker stateTracker,
        Func<nint, bool> isWindow,
        IDictionary<SyncHotkeyAction, ConsoleKey>? hotkeys = null,
        int queueCapacity = 2048)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(stateTracker);
        ArgumentNullException.ThrowIfNull(isWindow);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);

        _adapter = adapter;
        _stateTracker = stateTracker;
        _isWindow = isWindow;
        Hotkeys = hotkeys is null
            ? new Dictionary<SyncHotkeyAction, ConsoleKey>
            {
                [SyncHotkeyAction.Toggle] = ConsoleKey.F8,
                [SyncHotkeyAction.Stop] = ConsoleKey.F9,
                [SyncHotkeyAction.EmergencyStop] = ConsoleKey.F10,
            }
            : new Dictionary<SyncHotkeyAction, ConsoleKey>(hotkeys);
        _channel = Channel.CreateBounded<QueuedInput>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public SyncState State { get; private set; } = SyncState.IDLE;

    public SyncControllerFault Fault { get; private set; } = SyncControllerFault.NONE;

    public nint SourceHwnd { get; private set; }

    public Dictionary<SyncHotkeyAction, ConsoleKey> Hotkeys { get; }

    public IReadOnlyDictionary<nint, TargetStatus> Targets
    {
        get
        {
            lock (_stateGate)
            {
                return new Dictionary<nint, TargetStatus>(_targets);
            }
        }
    }

    public bool Start()
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            if (State is SyncState.RUNNING or SyncState.PAUSED)
            {
                return false;
            }

            if (!_adapter.IsSupported())
            {
                State = SyncState.ERROR;
                Fault = SyncControllerFault.ADAPTER_UNSUPPORTED;
                return false;
            }

            if (SourceHwnd == 0 || !_isWindow(SourceHwnd))
            {
                SourceHwnd = 0;
                State = SyncState.ERROR;
                Fault = SyncControllerFault.SOURCE_LOST;
                return false;
            }

            ClearQueue();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            State = SyncState.RUNNING;
            Fault = SyncControllerFault.NONE;
            _worker = Task.Run(() => DispatchLoopAsync(_cancellation.Token));
            return true;
        }
    }

    public bool Pause()
    {
        lock (_stateGate)
        {
            if (State != SyncState.RUNNING)
            {
                return false;
            }

            State = SyncState.PAUSED;
            return true;
        }
    }

    public bool Resume()
    {
        lock (_stateGate)
        {
            if (State != SyncState.PAUSED)
            {
                return false;
            }

            State = SyncState.RUNNING;
            return true;
        }
    }

    public void Stop() => StopCore(SyncControllerFault.NONE);

    public void EmergencyStop() => StopCore(SyncControllerFault.NONE);

    public void SetSource(nint sourceHwnd)
    {
        Stop();
        lock (_stateGate)
        {
            SourceHwnd = sourceHwnd;
            Fault = SyncControllerFault.NONE;
        }
    }

    public bool AddTarget(nint targetHwnd)
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            if (targetHwnd == 0 || targetHwnd == SourceHwnd)
            {
                return false;
            }

            _targets[targetHwnd] = _isWindow(targetHwnd)
                ? TargetStatus.ACTIVE
                : TargetStatus.WINDOW_LOST;
            return _targets[targetHwnd] == TargetStatus.ACTIVE;
        }
    }

    public bool RemoveTarget(nint targetHwnd)
    {
        lock (_stateGate)
        {
            if (!_targets.Remove(targetHwnd))
            {
                return false;
            }
        }

        lock (_dispatchGate)
        {
            _stateTracker.ReleaseAll(targetHwnd, _adapter);
        }

        return true;
    }

    public void RefreshTargets()
    {
        nint source;
        nint[] targets;
        lock (_stateGate)
        {
            source = SourceHwnd;
            targets = [.. _targets.Keys];
        }

        if (source != 0 && !_isWindow(source))
        {
            lock (_stateGate)
            {
                SourceHwnd = 0;
            }

            StopCore(SyncControllerFault.SOURCE_LOST);
            return;
        }

        foreach (nint target in targets)
        {
            bool valid = _isWindow(target);
            lock (_stateGate)
            {
                if (_targets.ContainsKey(target))
                {
                    _targets[target] = valid ? TargetStatus.ACTIVE : TargetStatus.WINDOW_LOST;
                }
            }

            if (!valid)
            {
                lock (_dispatchGate)
                {
                    _stateTracker.ReleaseAll(target, _adapter);
                }
            }
        }
    }

    public bool TryQueueKeyboard(KeyboardEventData eventData) =>
        IsRunning() && _channel.Writer.TryWrite(QueuedInput.FromKeyboard(eventData));

    public bool TryQueueMouse(MouseEventData eventData) =>
        IsRunning() && _channel.Writer.TryWrite(QueuedInput.FromMouse(eventData));

    public bool HandleHotkey(ConsoleKey key)
    {
        if (Hotkeys.TryGetValue(SyncHotkeyAction.EmergencyStop, out ConsoleKey emergency) && key == emergency)
        {
            EmergencyStop();
            return true;
        }

        if (Hotkeys.TryGetValue(SyncHotkeyAction.Stop, out ConsoleKey stop) && key == stop)
        {
            Stop();
            return true;
        }

        if (Hotkeys.TryGetValue(SyncHotkeyAction.Toggle, out ConsoleKey toggle) && key == toggle)
        {
            if (State == SyncState.RUNNING)
            {
                Pause();
            }
            else if (State == SyncState.PAUSED)
            {
                Resume();
            }
            else
            {
                Start();
            }

            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        EmergencyStop();
        _disposed = true;
        _cancellation?.Dispose();
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out QueuedInput input))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsRunning())
                    {
                        continue;
                    }

                    Dispatch(input);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            StopCore(SyncControllerFault.DISPATCH_ERROR);
        }
    }

    private void Dispatch(QueuedInput input)
    {
        KeyValuePair<nint, TargetStatus>[] targets;
        lock (_stateGate)
        {
            targets = [.. _targets];
        }

        foreach ((nint target, TargetStatus targetState) in targets)
        {
            if (targetState != TargetStatus.ACTIVE)
            {
                continue;
            }

            if (!_isWindow(target))
            {
                MarkTargetLost(target);
                continue;
            }

            lock (_dispatchGate)
            {
                try
                {
                    if (input.Keyboard is KeyboardEventData keyboard)
                    {
                        TrackKeyboard(target, keyboard with { TargetHwnd = target });
                    }
                    else if (input.Mouse is MouseEventData mouse)
                    {
                        TrackMouse(target, mouse with { TargetHwnd = target });
                    }
                }
                catch
                {
                    if (!_isWindow(target))
                    {
                        MarkTargetLost(target);
                    }
                }
            }
        }
    }

    private void TrackKeyboard(nint target, KeyboardEventData eventData)
    {
        if (!_adapter.SendKeyboard(eventData))
        {
            if (!_isWindow(target))
            {
                MarkTargetLost(target);
            }

            return;
        }

        if (eventData.Action == KeyboardAction.Down)
        {
            _stateTracker.TrackDown(target, eventData.VirtualKey);
        }
        else
        {
            _stateTracker.TrackUp(target, eventData.VirtualKey);
        }
    }

    private void TrackMouse(nint target, MouseEventData eventData)
    {
        if (!_adapter.SendMouse(eventData))
        {
            if (!_isWindow(target))
            {
                MarkTargetLost(target);
            }

            return;
        }

        if (eventData.Action == MouseAction.ButtonDown)
        {
            _stateTracker.TrackDown(target, eventData.Button);
        }
        else if (eventData.Action == MouseAction.ButtonUp)
        {
            _stateTracker.TrackUp(target, eventData.Button);
        }
    }

    private void MarkTargetLost(nint target)
    {
        lock (_stateGate)
        {
            if (_targets.ContainsKey(target))
            {
                _targets[target] = TargetStatus.WINDOW_LOST;
            }
        }

        lock (_dispatchGate)
        {
            _stateTracker.ReleaseAll(target, _adapter);
        }
    }

    private void StopCore(SyncControllerFault fault)
    {
        nint[] targets;
        lock (_stateGate)
        {
            if (State == SyncState.STOPPING)
            {
                return;
            }

            State = SyncState.STOPPING;
            _cancellation?.Cancel();
            ClearQueue();
            targets = [.. _targets.Keys];
        }

        lock (_dispatchGate)
        {
            foreach (nint target in targets)
            {
                _stateTracker.ReleaseAll(target, _adapter);
            }
        }

        lock (_stateGate)
        {
            ClearQueue();
            Fault = fault;
            State = fault == SyncControllerFault.NONE
                ? SyncState.IDLE
                : SyncState.ERROR;
        }
    }

    private bool IsRunning()
    {
        lock (_stateGate)
        {
            return State == SyncState.RUNNING;
        }
    }

    private void ClearQueue()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct QueuedInput(KeyboardEventData? Keyboard, MouseEventData? Mouse)
    {
        public static QueuedInput FromKeyboard(KeyboardEventData value) => new(value, null);

        public static QueuedInput FromMouse(MouseEventData value) => new(null, value);
    }
}
