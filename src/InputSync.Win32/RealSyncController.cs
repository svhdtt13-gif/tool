using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using InputSync.Core.Capture;
using InputSync.Core.Controller;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Core.Normalization;
using InputSync.Core.Safety;
using InputSync.Core.Text;

namespace InputSync.Win32;

/// <summary>
/// Production <see cref="ISyncController"/>: wires real window discovery,
/// low-level input capture, the engine queue/dispatcher and the Win32
/// fan-out adapter together. All UI-bound updates are marshalled through
/// the injected invoker (WPF Dispatcher.BeginInvoke); the pump never blocks
/// on the UI thread, so Stop/EmergencyStop can always join it.
/// </summary>
public sealed class RealSyncController : ISyncController, IDisposable
{
    private const int EventLogCapacity = 200;
    private const int UiSyncThrottleMs = 100;

    private readonly Action<Action> _uiInvoker;
    private readonly IKeyboardLayoutTranslator _translator;
    private readonly SyncTargetAdapter _targetAdapter;
    private readonly LatencyTracker _latency = new();
    private readonly SyncController _engine;
    private readonly Stopwatch _uiSyncClock = Stopwatch.StartNew();
    private readonly object _lifecycleGate = new();
    private readonly EventNormalizer _normalizer = new();

    private readonly TargetEndpointResolver _endpointResolver = new();
    private readonly HashSet<uint> _textHeld = [];
    private SourceTextSync? _textSync;
    private InputCapture? _capture;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;
    private Task? _monitorTask;
    private volatile SyncState _state = SyncState.IDLE;
    private volatile bool _disposed;
    private WindowInfo? _source;
    private bool _keyboardEnabled = true;
    private bool _mouseEnabled = true;
    private CoordinateMode _coordinateMode = CoordinateMode.Relative;
    private string _statusText = "READY";
    private long _eventsReceived;
    private long _eventsDropped;
    private long _lastUiSyncMs;

    public RealSyncController(
        bool debugEnabled = false,
        Action<Action>? uiInvoker = null,
        IKeyboardLayoutTranslator? translator = null)
    {
        DebugEnabled = debugEnabled;
        _uiInvoker = uiInvoker ?? (action => action());
        _translator = translator ?? new KeyboardLayoutTranslator();
        _targetAdapter = new SyncTargetAdapter(
            () => _source?.Hwnd ?? nint.Zero,
            () => _coordinateMode,
            _endpointResolver,
            _latency);
        _engine = new SyncController(
            _targetAdapter,
            new InputStateTracker(),
            WindowManager.IsWindowValid);
        RefreshWindows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<WindowInfo> Windows { get; } = [];

    public ObservableCollection<SyncTarget> Targets { get; } = [];

    public ObservableCollection<string> EventLog { get; } = [];

    public SyncMetrics Metrics { get; } = new();

    public bool DebugEnabled { get; }

    public WindowInfo? Source
    {
        get => _source;
        set
        {
            if (_source?.Hwnd == value?.Hwnd)
            {
                return;
            }

            if (State == SyncState.RUNNING || State == SyncState.PAUSED)
            {
                Stop();
            }

            _source = value;
            OnPropertyChanged();
            AddLog(value is null ? "Source cleared." : $"Source selected: {value.Title} (HWND {value.Hwnd}).");
        }
    }

    public bool KeyboardEnabled
    {
        get => _keyboardEnabled;
        set
        {
            if (_keyboardEnabled == value)
            {
                return;
            }

            _keyboardEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool MouseEnabled
    {
        get => _mouseEnabled;
        set
        {
            if (_mouseEnabled == value)
            {
                return;
            }

            _mouseEnabled = value;
            OnPropertyChanged();
        }
    }

    public CoordinateMode CoordinateMode
    {
        get => _coordinateMode;
        set
        {
            if (_coordinateMode == value)
            {
                return;
            }

            _coordinateMode = value;
            OnPropertyChanged();
        }
    }

    public SyncState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            Marshal(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State))));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            Marshal(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText))));
        }
    }

    public void RefreshWindows()
    {
        nint previousSourceHwnd = Source?.Hwnd ?? nint.Zero;
        HashSet<nint> previouslyEnabled = Targets
            .Where(target => target.Enabled)
            .Select(target => target.Window.Hwnd)
            .ToHashSet();

        IReadOnlyList<WindowInfo> discovered;
        try
        {
            discovered = WindowManager.EnumerateWindows();
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            State = SyncState.ERROR;
            StatusText = "WINDOW DISCOVERY ERROR";
            AddLog($"Window discovery failed: {exception.Message}");
            return;
        }

        foreach (SyncTarget target in Targets)
        {
            target.PropertyChanged -= OnTargetChanged;
        }

        Windows.Clear();
        Targets.Clear();

        foreach (WindowInfo window in discovered)
        {
            Windows.Add(window);
        }

        _source = discovered.FirstOrDefault(window => window.Hwnd == previousSourceHwnd)
            ?? discovered.FirstOrDefault();
        OnPropertyChanged(nameof(Source));

        foreach (WindowInfo window in discovered)
        {
            if (window.Hwnd == _source?.Hwnd)
            {
                continue;
            }

            AddTarget(new SyncTarget(window, previouslyEnabled.Contains(window.Hwnd)));
        }

        _engine.RefreshTargets();
        SyncUiState(force: true);
        AddLog($"Window list refreshed: {discovered.Count} real top-level windows discovered.");
    }

    public void Start()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            StopPumpAndCapture();

            if (Source is null || !WindowManager.IsWindowValid(Source.Hwnd))
            {
                State = SyncState.ERROR;
                StatusText = "SOURCE LOST";
                AddLog("Start rejected: source window is unavailable.");
                return;
            }

            _engine.SetSource(Source.Hwnd);
            foreach (SyncTarget target in Targets)
            {
                if (!target.Enabled)
                {
                    continue;
                }

                target.Status = _engine.AddTarget(target.Window.Hwnd)
                    ? TargetStatus.ACTIVE
                    : TargetStatus.WINDOW_LOST;
            }

            try
            {
                _capture ??= new InputCapture(Source.Hwnd, _normalizer, _translator);
                _capture.SourceHwnd = Source.Hwnd;
                _capture.Start();
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                State = SyncState.ERROR;
                StatusText = "CAPTURE ERROR";
                AddLog($"Input capture failed to start: {exception.Message}");
                return;
            }

            Interlocked.Exchange(ref _eventsReceived, 0);
            Interlocked.Exchange(ref _eventsDropped, 0);
            _latency.Reset();
            _textHeld.Clear();
            _textSync = new SourceTextSync(ReadSourceText, EmitTextToTargets);
            _textSync.Sync();

            if (!_engine.Start())
            {
                _capture.Stop();
                FailFromEngine("Start rejected by engine");
                return;
            }

            _pumpCancellation?.Dispose();
            _pumpCancellation = new CancellationTokenSource();
            CancellationToken token = _pumpCancellation.Token;
            _pumpTask = Task.Run(() => PumpLoopAsync(token), CancellationToken.None);
            _monitorTask = Task.Run(() => MonitorLoopAsync(token), CancellationToken.None);

            State = SyncState.RUNNING;
            StatusText = "RUNNING";
            SyncUiState(force: true);
            AddLog($"Synchronization started: source HWND {Source.Hwnd}.");
        }
    }

    public void Pause()
    {
        lock (_lifecycleGate)
        {
            if (State != SyncState.RUNNING || _disposed)
            {
                return;
            }

            _engine.Pause();
            State = SyncState.PAUSED;
            StatusText = "PAUSED";
            SyncUiState(force: true);
            AddLog("Synchronization paused.");
        }
    }

    public void TogglePauseResume()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            if (State == SyncState.RUNNING)
            {
                Pause();
            }
            else if (State == SyncState.PAUSED)
            {
                _engine.Resume();
                State = SyncState.RUNNING;
                StatusText = "RUNNING";
                SyncUiState(force: true);
                AddLog("Synchronization resumed.");
            }
            else
            {
                Start();
            }
        }
    }

    public void Stop()
    {
        Task? pump;
        Task? monitor;
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _pumpCancellation?.Cancel();
            pump = _pumpTask;
            monitor = _monitorTask;
            _pumpTask = null;
            _monitorTask = null;
        }

        JoinWorker(pump);
        JoinWorker(monitor);

        lock (_lifecycleGate)
        {
            _textHeld.Clear();
            _capture?.Stop();
            _engine.Stop();
            State = SyncState.IDLE;
            StatusText = "READY";
            SyncUiState(force: true);
            AddLog("Synchronization stopped; pressed state released on all targets.");
        }
    }

    public void EmergencyStop()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _pumpCancellation?.Cancel();
            _pumpTask = null;
            _monitorTask = null;
        }

        try
        {
            _capture?.Stop();
        }
        catch
        {
        }

        _engine.EmergencyStop();
        State = SyncState.IDLE;
        StatusText = "READY";
        SyncUiState(force: true);
        AddLog("EMERGENCY STOP: capture halted, pressed state released on all targets.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _pumpCancellation?.Cancel();
        }
        catch
        {
        }

        try
        {
            _capture?.Dispose();
        }
        catch
        {
        }

        try
        {
            _engine.Dispose();
        }
        catch
        {
        }

        try
        {
            _pumpCancellation?.Dispose();
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }

    private async Task PumpLoopAsync(CancellationToken cancellationToken)
    {
        InputCapture? capture = _capture;
        if (capture is null)
        {
            return;
        }

        try
        {
            await foreach (NormalizedInputEvent evt in capture.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (State != SyncState.RUNNING)
                {
                    Interlocked.Increment(ref _eventsDropped);
                    continue;
                }

                if ((evt.Type == InputEventType.Keyboard && !KeyboardEnabled)
                    || (evt.Type == InputEventType.Mouse && !MouseEnabled))
                {
                    Interlocked.Increment(ref _eventsDropped);
                    continue;
                }

                _latency.RecordEnqueued(evt.Id, evt.Timestamp);
                Interlocked.Increment(ref _eventsReceived);

                bool queued;
                if (evt.Type == InputEventType.Keyboard)
                {
                    _textSync?.Sync();
                    if (!KeyClassifier.ShouldForwardAsControl(evt.Vk, IsAsyncDown(VK_MENU), IsAsyncDown(VK_CONTROL)))
                    {
                        HandleTextKey(evt);
                        continue;
                    }

                    queued = _engine.TryQueueKeyboard(ToKeyboard(evt));
                }
                else
                {
                    queued = _engine.TryQueueMouse(ToMouse(evt));
                }

                if (!queued)
                {
                    Interlocked.Increment(ref _eventsDropped);
                }

                SyncUiState(force: false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static KeyboardEventData ToKeyboard(NormalizedInputEvent evt)
    {
        KeyboardAction action = string.Equals(evt.Action, nameof(InputAction.KeyUp), StringComparison.OrdinalIgnoreCase)
            ? KeyboardAction.Up
            : KeyboardAction.Down;
        return new KeyboardEventData(
            nint.Zero, evt.Vk, evt.ScanCode, action, evt.Extended, evt.Text ?? string.Empty, evt.Id);
    }

    private static MouseEventData ToMouse(NormalizedInputEvent evt)
    {
        Enum.TryParse<InputAction>(evt.Action, ignoreCase: true, out InputAction action);
        Enum.TryParse<MouseButton>(evt.Button, ignoreCase: true, out MouseButton button);
        MouseAction mouseAction = action switch
        {
            InputAction.MouseMove => MouseAction.Move,
            InputAction.Wheel => MouseAction.Wheel,
            InputAction.LeftDown or InputAction.RightDown or InputAction.MiddleDown or InputAction.XButtonDown => MouseAction.ButtonDown,
            _ => MouseAction.ButtonUp,
        };
        return new MouseEventData(
            nint.Zero, mouseAction, evt.X, evt.Y, button, MouseButtonMask.None, evt.WheelDelta, evt.Id);
    }

    private void HandleTextKey(NormalizedInputEvent evt)
    {
        bool isDown = !string.Equals(evt.Action, nameof(InputAction.KeyUp), StringComparison.OrdinalIgnoreCase);
        if (isDown)
        {
            _textHeld.Add(evt.Vk);
            _textSync?.Sync();
        }
        else
        {
            _textHeld.Remove(evt.Vk);
        }
    }

    private string? ReadSourceText()
    {
        nint source = _source?.Hwnd ?? nint.Zero;
        if (source == nint.Zero || !WindowManager.IsWindowValid(source))
        {
            return null;
        }

        nint endpoint;
        try
        {
            endpoint = _endpointResolver.Resolve(source);
        }
        catch
        {
            return null;
        }

        const int capacity = 30001;
        var buffer = new System.Text.StringBuilder(capacity);
        try
        {
            nint result = SendMessageTimeout(
                endpoint, WM_GETTEXT, (nuint)capacity, buffer, SMTO_ABORTIFHUNG, 100, out _);
            return result == nint.Zero ? null : buffer.ToString();
        }
        catch
        {
            return null;
        }
    }

    private void EmitTextToTargets(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SyncTarget[] snapshot;
        lock (_lifecycleGate)
        {
            snapshot = [.. Targets];
        }

        bool changed = false;
        foreach (SyncTarget target in snapshot)
        {
            if (!target.Enabled)
            {
                continue;
            }

            nint hwnd = target.Window.Hwnd;
            if (!WindowManager.IsWindowValid(hwnd))
            {
                _engine.RemoveTarget(hwnd);
                target.Status = TargetStatus.WINDOW_LOST;
                changed = true;
                continue;
            }

            if (!_targetAdapter.SendText(hwnd, text))
            {
                changed = true;
            }
        }

        if (changed)
        {
            SyncUiState(force: true);
        }
    }

    private void SyncUiState(bool force)
    {
        long nowMs = _uiSyncClock.ElapsedMilliseconds;
        if (!force && nowMs - Interlocked.Read(ref _lastUiSyncMs) < UiSyncThrottleMs)
        {
            return;
        }

        Interlocked.Exchange(ref _lastUiSyncMs, nowMs);

        long received = Interlocked.Read(ref _eventsReceived);
        long dropped = Interlocked.Read(ref _eventsDropped);
        long dispatched = _targetAdapter.EventsDispatched;
        double averageMs = _latency.AverageMs;
        double maxMs = _latency.MaxMs;
        IReadOnlyDictionary<nint, TargetStatus> engineTargets = _engine.Targets;
        SyncState engineState = _engine.State;
        nint? sourceHwnd = _source?.Hwnd;

        Marshal(() =>
        {
            Metrics.EventsReceived = received;
            Metrics.EventsDispatched = dispatched;
            Metrics.EventsDropped = dropped;
            Metrics.AverageLatencyMs = averageMs;
            Metrics.MaxLatencyMs = maxMs;

            foreach (SyncTarget target in Targets)
            {
                if (!target.Enabled)
                {
                    continue;
                }

                if (engineTargets.TryGetValue(target.Window.Hwnd, out TargetStatus status))
                {
                    target.Status = status;
                }
            }

            Metrics.ActiveTargets = CountTargets(TargetStatus.ACTIVE);
            Metrics.LostTargets = CountTargets(TargetStatus.WINDOW_LOST);

            if (_state == SyncState.RUNNING && engineState == SyncState.ERROR)
            {
                FailFromEngine("Engine reported an error");
                return;
            }

            if (_state == SyncState.RUNNING
                && sourceHwnd is { } hwnd
                && !WindowManager.IsWindowValid(hwnd))
            {
                HandleSourceLost();
            }
        });
    }

    private void HandleSourceLost()
    {
        Stop();
        State = SyncState.ERROR;
        StatusText = "SOURCE LOST";
        AddLog("Source window was closed; synchronization stopped safely.");
    }

    private void FailFromEngine(string prefix)
    {
        State = SyncState.ERROR;
        StatusText = _engine.Fault switch
        {
            SyncControllerFault.SOURCE_LOST => "SOURCE LOST",
            SyncControllerFault.ADAPTER_UNSUPPORTED => "ADAPTER UNSUPPORTED",
            SyncControllerFault.DISPATCH_ERROR => "DISPATCH ERROR",
            _ => "ERROR",
        };
        SyncUiState(force: true);
        AddLog($"{prefix}: {StatusText}.");
    }

    private int CountTargets(TargetStatus status)
    {
        int count = 0;
        foreach (SyncTarget target in Targets)
        {
            if (target.Status == status && (status != TargetStatus.ACTIVE || target.Enabled))
            {
                count++;
            }
        }

        return count;
    }

    private void AddTarget(SyncTarget target)
    {
        target.PropertyChanged += OnTargetChanged;
        Targets.Add(target);
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SyncTarget.Enabled) or nameof(SyncTarget.Status)))
        {
            return;
        }

        if (sender is SyncTarget target
            && e.PropertyName == nameof(SyncTarget.Enabled)
            && (State == SyncState.RUNNING || State == SyncState.PAUSED))
        {
            nint hwnd = target.Window.Hwnd;
            if (target.Enabled)
            {
                target.Status = _engine.AddTarget(hwnd) ? TargetStatus.ACTIVE : TargetStatus.WINDOW_LOST;
            }
            else
            {
                _engine.RemoveTarget(hwnd);
            }
        }

        Metrics.ActiveTargets = CountTargets(TargetStatus.ACTIVE);
        Metrics.LostTargets = CountTargets(TargetStatus.WINDOW_LOST);
    }

    private void StopPumpAndCapture()
    {
        _pumpCancellation?.Cancel();
        Task? pump = _pumpTask;
        Task? monitor = _monitorTask;
        _pumpTask = null;
        _monitorTask = null;
        JoinWorker(pump);
        JoinWorker(monitor);
        _textHeld.Clear();
        _capture?.Stop();
    }

    private static void JoinWorker(Task? worker)
    {
        try
        {
            worker?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                if (State == SyncState.RUNNING)
                {
                    _textSync?.Sync();
                    SyncUiState(force: false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void AddLog(string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        string line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        Marshal(() =>
        {
            EventLog.Insert(0, line);
            while (EventLog.Count > EventLogCapacity)
            {
                EventLog.RemoveAt(EventLog.Count - 1);
            }
        });
    }

    private void Marshal(Action action)
    {
        try
        {
            _uiInvoker(action);
        }
        catch
        {
        }
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        Marshal(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private const uint WM_GETTEXT = 0x000D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const int VK_MENU = 0x12;
    private const int VK_CONTROL = 0x11;

    private static bool IsAsyncDown(int virtualKey)
    {
        try
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint SendMessageTimeout(
        nint hwnd,
        uint message,
        nuint wParam,
        System.Text.StringBuilder lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);
}
