using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using InputSync.Core.Controller;
using InputSync.Core.Models;
using InputSync.Win32;

namespace InputSync.UI;

internal sealed class SampleSyncController : ISyncController
{
    private const int EventLogCapacity = 200;
    private WindowInfo? _source;
    private bool _keyboardEnabled = true;
    private bool _mouseEnabled = true;
    private CoordinateMode _coordinateMode = CoordinateMode.Relative;
    private TargetBackend _backendMode = TargetBackend.Broadcast;
    private SyncState _state = SyncState.IDLE;
    private string _statusText = "READY";

    public SampleSyncController(bool debugEnabled)
    {
        DebugEnabled = debugEnabled;
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
        set => Set(ref _source, value);
    }

    public bool KeyboardEnabled
    {
        get => _keyboardEnabled;
        set => Set(ref _keyboardEnabled, value);
    }

    public bool MouseEnabled
    {
        get => _mouseEnabled;
        set => Set(ref _mouseEnabled, value);
    }

    public CoordinateMode CoordinateMode
    {
        get => _coordinateMode;
        set => Set(ref _coordinateMode, value);
    }

    public TargetBackend BackendMode
    {
        get => _backendMode;
        set => Set(ref _backendMode, value);
    }

    public SyncState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public void RefreshWindows()
    {
        nint previousSourceHwnd = Source?.Hwnd ?? 0;
        HashSet<nint> previouslyEnabledTargets = Targets
            .Where(target => target.Enabled)
            .Select(target => target.Window.Hwnd)
            .ToHashSet();

        Windows.Clear();
        Targets.Clear();

        IReadOnlyList<WindowInfo> discovered;
        try
        {
            discovered = WindowManager.EnumerateWindows();
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            Source = null;
            Metrics.ActiveTargets = 0;
            Metrics.LostTargets = 0;
            State = SyncState.ERROR;
            StatusText = "WINDOW DISCOVERY ERROR";
            AddLog($"Window discovery failed: {exception.Message}");
            return;
        }

        foreach (WindowInfo window in discovered)
        {
            Windows.Add(window);
        }

        WindowInfo? restoredSource = discovered.FirstOrDefault(window => window.Hwnd == previousSourceHwnd);
        Source = restoredSource ?? discovered.FirstOrDefault();

        foreach (WindowInfo window in discovered)
        {
            if (window.Hwnd == Source?.Hwnd)
            {
                continue;
            }

            AddTarget(new SyncTarget(window, enabled: previouslyEnabledTargets.Contains(window.Hwnd)));
        }

        UpdateTargetMetrics();
        AddLog($"Window list refreshed: {discovered.Count} real top-level windows discovered.");
    }

    public void Start()
    {
        if (Source is null || !WindowManager.IsWindowValid(Source.Hwnd))
        {
            State = SyncState.ERROR;
            StatusText = "SOURCE LOST";
            AddLog("Start rejected: source window is unavailable.");
            return;
        }

        foreach (SyncTarget target in Targets)
        {
            if (target.Enabled && !WindowManager.IsWindowValid(target.Window.Hwnd))
            {
                target.Status = TargetStatus.WINDOW_LOST;
            }
            else if (target.Enabled)
            {
                target.Status = TargetStatus.ACTIVE;
            }
        }

        UpdateTargetMetrics();
        State = SyncState.RUNNING;
        StatusText = "RUNNING";
        AddLog($"Synchronization started: source HWND {Source.Hwnd}, {Metrics.ActiveTargets} active target(s).");
    }

    public void Pause()
    {
        if (State != SyncState.RUNNING)
        {
            return;
        }

        State = SyncState.PAUSED;
        StatusText = "PAUSED";
        AddLog("Synchronization paused.");
    }

    public void Stop()
    {
        State = SyncState.IDLE;
        StatusText = "READY";
        AddLog("Synchronization stopped; release-all requested.");
    }

    public void EmergencyStop()
    {
        State = SyncState.IDLE;
        StatusText = "READY";
        AddLog("EMERGENCY STOP requested; release-all requested.");
    }

    private void AddTarget(SyncTarget target)
    {
        target.PropertyChanged += OnTargetChanged;
        Targets.Add(target);
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SyncTarget.Enabled) or nameof(SyncTarget.Status))
        {
            UpdateTargetMetrics();
        }
    }

    private void UpdateTargetMetrics()
    {
        Metrics.ActiveTargets = Targets.Count(target => target.Enabled && target.Status == TargetStatus.ACTIVE);
        Metrics.LostTargets = Targets.Count(target => target.Status == TargetStatus.WINDOW_LOST);
    }

    private void AddLog(string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        EventLog.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
        while (EventLog.Count > EventLogCapacity)
        {
            EventLog.RemoveAt(EventLog.Count - 1);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
