using System.Diagnostics;
using System.Threading.Channels;
using InputSync.Core.Controller;
using InputSync.Core.Models;
using InputSync.Core.Safety;

namespace InputSync.Core.Dispatch;

public sealed class GameRotationEngine : IDisposable
{
    private readonly object _stateGate = new();
    private readonly object _releaseGate = new();
    private readonly ITargetAdapter _sender;
    private readonly InputStateTracker _tracker;
    private readonly Func<nint, bool> _isWindow;
    private readonly Func<nint, (bool Ok, string Detail)> _ensureForeground;
    private readonly Func<MouseEventData, nint, MouseEventData>? _translateMouse;
    private readonly Func<nint>? _getForeground;
    private readonly Action<string>? _trace;
    private readonly Channel<QueuedInput> _channel;
    private readonly List<nint> _targetOrder = [];
    private readonly Dictionary<nint, TargetStatus> _targets = [];
    private readonly HashSet<nint> _releasingTargets = [];
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private SyncState _state = SyncState.IDLE;
    private SyncControllerFault _fault = SyncControllerFault.NONE;
    private nint _sourceHwnd;
    private bool _disposed;
    private long _eventsReceived;
    private long _eventsDispatched;
    private long _dispatchFailures;
    private long _lostTargets;
    private long _focusAttempts;
    private long _focusFailures;
    private long _sends;
    private long _sendFailures;
    private long _latencySamples;
    private long _totalLatencyTicks;
    private long _maxLatencyTicks;
    private long _generation;

    public GameRotationEngine(
        ITargetAdapter sender,
        InputStateTracker tracker,
        Func<nint, bool> isWindow,
        Func<nint, (bool Ok, string Detail)> ensureForeground,
        Func<MouseEventData, nint, MouseEventData>? translateMouse = null,
        Action<string>? trace = null,
        Func<nint>? getForeground = null,
        int queueCapacity = 2048)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _isWindow = isWindow ?? throw new ArgumentNullException(nameof(isWindow));
        _ensureForeground = ensureForeground ?? throw new ArgumentNullException(nameof(ensureForeground));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueCapacity);

        _getForeground = getForeground;

        _translateMouse = translateMouse;
        _trace = trace;
        _channel = Channel.CreateBounded<QueuedInput>(new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public SyncState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public SyncControllerFault Fault
    {
        get
        {
            lock (_stateGate)
            {
                return _fault;
            }
        }
    }

    public nint SourceHwnd
    {
        get
        {
            lock (_stateGate)
            {
                return _sourceHwnd;
            }
        }
    }

    public IReadOnlyDictionary<nint, TargetStatus> Targets
    {
        get
        {
            lock (_stateGate)
            {
                var snapshot = new Dictionary<nint, TargetStatus>(_targets.Count);
                foreach (nint target in _targetOrder)
                {
                    snapshot[target] = _targets[target];
                }

                return snapshot;
            }
        }
    }

    public long EventsReceived => Interlocked.Read(ref _eventsReceived);

    public long EventsDispatched => Interlocked.Read(ref _eventsDispatched);

    public long DispatchFailures => Interlocked.Read(ref _dispatchFailures);

    public long LostTargets => Interlocked.Read(ref _lostTargets);

    public long FocusAttempts => Interlocked.Read(ref _focusAttempts);

    public long FocusFailures => Interlocked.Read(ref _focusFailures);

    public long Sends => Interlocked.Read(ref _sends);

    public long SendFailures => Interlocked.Read(ref _sendFailures);

    public double AvgLatencyMs
    {
        get
        {
            long samples = Interlocked.Read(ref _latencySamples);
            return samples == 0
                ? 0
                : Interlocked.Read(ref _totalLatencyTicks) * 1000.0 / Stopwatch.Frequency / samples;
        }
    }

    public double MaxLatencyMs =>
        Interlocked.Read(ref _maxLatencyTicks) * 1000.0 / Stopwatch.Frequency;

    public long Generation => Interlocked.Read(ref _generation);

    public void SetSource(nint sourceHwnd)
    {
        Stop();
        lock (_stateGate)
        {
            ThrowIfDisposed();
            _sourceHwnd = sourceHwnd;
            _fault = SyncControllerFault.NONE;
        }
    }

    public bool AddTarget(nint targetHwnd)
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            if (targetHwnd == nint.Zero || targetHwnd == _sourceHwnd)
            {
                return false;
            }

            TargetStatus status = SafeIsWindow(targetHwnd)
                ? TargetStatus.ACTIVE
                : TargetStatus.WINDOW_LOST;
            if (!_targets.ContainsKey(targetHwnd))
            {
                _targetOrder.Add(targetHwnd);
            }

            _targets[targetHwnd] = status;
            return status == TargetStatus.ACTIVE;
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

            _targetOrder.Remove(targetHwnd);
        }

        ReleaseTarget(targetHwnd, isEmergency: false);
        return true;
    }

    public void RefreshTargets()
    {
        nint source;
        nint[] targets;
        lock (_stateGate)
        {
            source = _sourceHwnd;
            targets = [.. _targetOrder];
        }

        if (source != nint.Zero && !SafeIsWindow(source))
        {
            lock (_stateGate)
            {
                if (_sourceHwnd == source)
                {
                    _sourceHwnd = nint.Zero;
                }
            }

            StopCore(SyncControllerFault.SOURCE_LOST);
            return;
        }

        foreach (nint target in targets)
        {
            if (!SafeIsWindow(target))
            {
                MarkLost(target);
                continue;
            }

            lock (_stateGate)
            {
                if (_targets.ContainsKey(target))
                {
                    _targets[target] = TargetStatus.ACTIVE;
                }
            }
        }
    }

    public bool Start()
    {
        lock (_stateGate)
        {
            ThrowIfDisposed();
            if (_state is SyncState.RUNNING or SyncState.PAUSED or SyncState.STOPPING)
            {
                return false;
            }

            if (!SafeIsSupported())
            {
                _state = SyncState.ERROR;
                _fault = SyncControllerFault.ADAPTER_UNSUPPORTED;
                return false;
            }

            if (_sourceHwnd == nint.Zero || !SafeIsWindow(_sourceHwnd))
            {
                _sourceHwnd = nint.Zero;
                _state = SyncState.ERROR;
                _fault = SyncControllerFault.SOURCE_LOST;
                return false;
            }

            ClearQueue();
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _cancellation.Token;
            long generation = Interlocked.Increment(ref _generation);
            _fault = SyncControllerFault.NONE;
            _state = SyncState.RUNNING;
            _worker = Task.Run(() => DispatchLoopAsync(cancellationToken, generation));
            return true;
        }
    }

    public bool Pause()
    {
        lock (_stateGate)
        {
            if (_state != SyncState.RUNNING)
            {
                return false;
            }

            _state = SyncState.PAUSED;
            return true;
        }
    }

    public bool Resume()
    {
        lock (_stateGate)
        {
            if (_state != SyncState.PAUSED)
            {
                return false;
            }

            _state = SyncState.RUNNING;
            return true;
        }
    }

    public void Stop() => StopCore(SyncControllerFault.NONE);

    public void EmergencyStop() => StopCore(SyncControllerFault.NONE, isEmergency: true);

    public bool TryQueueKeyboard(KeyboardEventData eventData)
    {
        if (!IsRunning())
        {
            return false;
        }

        Guid correlationId = eventData.CorrelationId == Guid.Empty
            ? Guid.NewGuid()
            : eventData.CorrelationId;
        bool queued = _channel.Writer.TryWrite(QueuedInput.FromKeyboard(
            eventData,
            Stopwatch.GetTimestamp(),
            correlationId));
        if (queued)
        {
            Interlocked.Increment(ref _eventsReceived);
        }

        return queued;
    }

    public bool TryQueueMouse(MouseEventData eventData)
    {
        if (!IsRunning())
        {
            return false;
        }

        Guid correlationId = eventData.CorrelationId == Guid.Empty
            ? Guid.NewGuid()
            : eventData.CorrelationId;
        bool queued = _channel.Writer.TryWrite(QueuedInput.FromMouse(
            eventData,
            Stopwatch.GetTimestamp(),
            correlationId));
        if (queued)
        {
            Interlocked.Increment(ref _eventsReceived);
        }

        return queued;
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }
        }

        EmergencyStop();
        lock (_stateGate)
        {
            _disposed = true;
            _cancellation?.Dispose();
            _cancellation = null;
            _worker = null;
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken, long workerGeneration)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out QueuedInput input))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long eventGeneration = workerGeneration;
                    if (eventGeneration != Generation || !IsRunning())
                    {
                        continue;
                    }

                    List<nint> snapshot = SnapshotActiveTargets();
                    nint eventSource = ReadSource();
                    DispatchEvent(input, snapshot, eventSource, cancellationToken, eventGeneration);
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

    private void DispatchEvent(
        QueuedInput input,
        List<nint> targets,
        nint source,
        CancellationToken cancellationToken,
        long generation)
    {
        bool anySent = false;
        foreach (nint target in targets)
        {
            if (cancellationToken.IsCancellationRequested || generation != Generation)
            {
                break;
            }

            if (!SafeIsWindow(target))
            {
                MarkLost(target);
                Interlocked.Increment(ref _dispatchFailures);
                Trace(input.CorrelationId, Describe(input), target, "focus=FAIL(window lost)");
                RecordLatency(input.EnqueueTicks);
                continue;
            }

            Interlocked.Increment(ref _focusAttempts);
            (bool focused, string detail) = SafeEnsureForeground(target);
            if (!focused)
            {
                Interlocked.Increment(ref _focusFailures);
                Interlocked.Increment(ref _dispatchFailures);
                Trace(input.CorrelationId, Describe(input), target, $"focus=FAIL({detail})");
                RecordLatency(input.EnqueueTicks);
                continue;
            }

            if (cancellationToken.IsCancellationRequested || generation != Generation)
            {
                break;
            }

            bool sent;
            string what;
            if (input.Keyboard is KeyboardEventData keyboard)
            {
                KeyboardEventData routed = keyboard with
                {
                    TargetHwnd = target,
                    CorrelationId = input.CorrelationId,
                };
                what = Describe(routed);
                sent = SafeSendKeyboard(routed);
            }
            else if (input.Mouse is MouseEventData mouse)
            {
                MouseEventData routed = mouse with
                {
                    TargetHwnd = target,
                    CorrelationId = input.CorrelationId,
                };
                try
                {
                    routed = _translateMouse?.Invoke(routed, target) ?? routed;
                    routed = routed with
                    {
                        TargetHwnd = target,
                        CorrelationId = input.CorrelationId,
                    };
                }
                catch
                {
                    Interlocked.Increment(ref _sendFailures);
                    Trace(input.CorrelationId, Describe(routed), target, "focus=ok send=False");
                    RecordLatency(input.EnqueueTicks);
                    continue;
                }

                what = Describe(routed);
                sent = SafeSendMouse(routed);
            }
            else
            {
                Interlocked.Increment(ref _dispatchFailures);
                Trace(input.CorrelationId, "unknown", target, "focus=ok send=False");
                RecordLatency(input.EnqueueTicks);
                continue;
            }

            if (sent)
            {
                anySent = true;
                Interlocked.Increment(ref _sends);
                Interlocked.Increment(ref _eventsDispatched);
                TrackSuccessfulSend(target, input);
                if (cancellationToken.IsCancellationRequested || generation != Generation)
                {
                    ReleaseTarget(target, isEmergency: true);
                }
            }
            else
            {
                Interlocked.Increment(ref _sendFailures);
                Interlocked.Increment(ref _dispatchFailures);
                if (!SafeIsWindow(target))
                {
                    MarkLost(target);
                }
            }

            Trace(input.CorrelationId, what, target, $"focus=ok send={sent}");
            RecordLatency(input.EnqueueTicks);
        }

        if (targets.Count > 0 && anySent)
        {
            ReturnFocusToSource(source, targets, cancellationToken, generation);
        }
    }

    private nint ReadSource()
    {
        lock (_stateGate)
        {
            return _sourceHwnd;
        }
    }

    private void ReturnFocusToSource(
        nint source,
        IReadOnlyList<nint> rotationTargets,
        CancellationToken cancellationToken,
        long generation)
    {
        if (source == nint.Zero
            || cancellationToken.IsCancellationRequested
            || generation != Generation
            || !SafeIsWindow(source))
        {
            return;
        }

        if (_getForeground is not null)
        {
            nint foreground;
            try
            {
                foreground = _getForeground();
            }
            catch
            {
                return;
            }

            bool ours = foreground == source;
            if (!ours)
            {
                foreach (nint target in rotationTargets)
                {
                    if (foreground == target)
                    {
                        ours = true;
                        break;
                    }
                }
            }

            if (!ours)
            {
                return;
            }
        }

        SafeEnsureForeground(source);
    }

    private List<nint> SnapshotActiveTargets()
    {
        lock (_stateGate)
        {
            var snapshot = new List<nint>(_targetOrder.Count);
            foreach (nint target in _targetOrder)
            {
                if (_targets.TryGetValue(target, out TargetStatus status) && status == TargetStatus.ACTIVE)
                {
                    snapshot.Add(target);
                }
            }

            return snapshot;
        }
    }

    private void TrackSuccessfulSend(nint target, QueuedInput input)
    {
        if (input.Keyboard is KeyboardEventData keyboard)
        {
            if (keyboard.Action == KeyboardAction.Down)
            {
                _tracker.TrackDown(target, keyboard.VirtualKey);
            }
            else
            {
                _tracker.TrackUp(target, keyboard.VirtualKey);
            }
        }
        else if (input.Mouse is MouseEventData mouse)
        {
            if (mouse.Action == MouseAction.ButtonDown)
            {
                _tracker.TrackDown(target, mouse.Button);
            }
            else if (mouse.Action == MouseAction.ButtonUp)
            {
                _tracker.TrackUp(target, mouse.Button);
            }
        }
    }

    private void MarkLost(nint target)
    {
        bool changed = false;
        lock (_stateGate)
        {
            if (_targets.TryGetValue(target, out TargetStatus status) && status != TargetStatus.WINDOW_LOST)
            {
                _targets[target] = TargetStatus.WINDOW_LOST;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        Interlocked.Increment(ref _lostTargets);
        ReleaseTarget(target, isEmergency: false);
    }

    private void StopCore(SyncControllerFault fault, bool isEmergency = false)
    {
        nint[] targets;
        nint source;
        lock (_stateGate)
        {
            if (_state == SyncState.STOPPING)
            {
                return;
            }

            _state = SyncState.STOPPING;
            Interlocked.Increment(ref _generation);
            _cancellation?.Cancel();
            ClearQueue();
            targets = [.. _targetOrder];
            source = _sourceHwnd;
        }

        foreach (nint target in targets)
        {
            ReleaseTarget(target, isEmergency);
        }

        ReturnFocusToSource(source, targets, CancellationToken.None, Generation);

        lock (_stateGate)
        {
            ClearQueue();
            _fault = fault;
            _state = fault == SyncControllerFault.NONE
                ? SyncState.IDLE
                : SyncState.ERROR;
            _worker = null;
        }
    }

    private void ReleaseTarget(nint target, bool isEmergency)
    {
        bool lockTaken = false;
        try
        {
            if (isEmergency)
            {
                Monitor.TryEnter(_releaseGate, ref lockTaken);
            }
            else
            {
                Monitor.Enter(_releaseGate, ref lockTaken);
            }

            if (!lockTaken || !_releasingTargets.Add(target))
            {
                return;
            }
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_releaseGate);
            }
        }

        try
        {
            _tracker.ReleaseAll(target, _sender);
        }
        catch
        {
        }
        finally
        {
            lock (_releaseGate)
            {
                _releasingTargets.Remove(target);
            }
        }
    }

    private bool IsRunning()
    {
        lock (_stateGate)
        {
            return _state == SyncState.RUNNING;
        }
    }

    private bool SafeIsSupported()
    {
        try
        {
            return _sender.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    private bool SafeIsWindow(nint target)
    {
        try
        {
            return _isWindow(target);
        }
        catch
        {
            return false;
        }
    }

    private (bool Ok, string Detail) SafeEnsureForeground(nint target)
    {
        try
        {
            return _ensureForeground(target);
        }
        catch
        {
            return (false, "focus callback threw");
        }
    }

    private bool SafeSendKeyboard(KeyboardEventData eventData)
    {
        try
        {
            return _sender.SendKeyboard(eventData);
        }
        catch
        {
            return false;
        }
    }

    private bool SafeSendMouse(MouseEventData eventData)
    {
        try
        {
            return _sender.SendMouse(eventData);
        }
        catch
        {
            return false;
        }
    }

    private void Trace(Guid correlationId, string what, nint target, string outcome)
    {
        try
        {
            string line = $"route #{correlationId:N} {what} -> 0x{target:X} {outcome}";
            _trace?.Invoke(line.Length > 200 ? line[..200] : line);
        }
        catch
        {
        }
    }

    private void RecordLatency(long enqueueTicks)
    {
        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - enqueueTicks);
        Interlocked.Add(ref _totalLatencyTicks, elapsed);
        Interlocked.Increment(ref _latencySamples);

        long maximum = Interlocked.Read(ref _maxLatencyTicks);
        while (elapsed > maximum)
        {
            long observed = Interlocked.CompareExchange(ref _maxLatencyTicks, elapsed, maximum);
            if (observed == maximum)
            {
                break;
            }

            maximum = observed;
        }
    }

    private void ClearQueue()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string Describe(QueuedInput input) => input.Keyboard is KeyboardEventData keyboard
        ? Describe(keyboard)
        : input.Mouse is MouseEventData mouse
            ? Describe(mouse)
            : "unknown";

    private static string Describe(KeyboardEventData keyboard) =>
        $"kbd {keyboard.Action} vk={keyboard.VirtualKey}";

    private static string Describe(MouseEventData mouse) =>
        $"mouse {mouse.Action}/{mouse.Button} ({mouse.X},{mouse.Y})";

    private readonly record struct QueuedInput(
        KeyboardEventData? Keyboard,
        MouseEventData? Mouse,
        long EnqueueTicks,
        Guid CorrelationId)
    {
        public static QueuedInput FromKeyboard(
            KeyboardEventData value,
            long enqueueTicks,
            Guid correlationId) => new(value, null, enqueueTicks, correlationId);

        public static QueuedInput FromMouse(
            MouseEventData value,
            long enqueueTicks,
            Guid correlationId) => new(null, value, enqueueTicks, correlationId);
    }
}
