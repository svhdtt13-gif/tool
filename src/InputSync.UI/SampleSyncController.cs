using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using InputSync.Core.Controller;
using InputSync.Core.Models;

namespace InputSync.UI;

internal sealed class SampleSyncController : ISyncController
{
    private const int EventLogCapacity = 200;
    private WindowInfo? _source;
    private bool _keyboardEnabled = true;
    private bool _mouseEnabled = true;
    private CoordinateMode _coordinateMode = CoordinateMode.Relative;
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
        Windows.Clear();
        Targets.Clear();

        WindowInfo[] samples =
        [
            CreateWindow(0x1001, "Notepad - Source", 8101, "notepad", "Notepad", 1280, 720),
            CreateWindow(0x1002, "Notepad - Target A", 8102, "notepad", "Notepad", 960, 540),
            CreateWindow(0x1003, "Notepad - Target B", 8103, "notepad", "Notepad", 1920, 1080),
            CreateWindow(0x1004, "Notepad - Closed", 8104, "notepad", "Notepad", 1280, 720),
        ];

        foreach (WindowInfo window in samples)
        {
            Windows.Add(window);
        }

        Source = samples[0];
        AddTarget(new SyncTarget(samples[1]));
        AddTarget(new SyncTarget(samples[2], enabled: false));
        SyncTarget lost = new(samples[3]);
        lost.Status = TargetStatus.WINDOW_LOST;
        AddTarget(lost);
        UpdateTargetMetrics();
        AddLog("Window list refreshed (sample discovery data)." );
    }

    public void Start()
    {
        if (Source is null)
        {
            State = SyncState.ERROR;
            StatusText = "SOURCE LOST";
            AddLog("Start rejected: source window is unavailable.");
            return;
        }

        State = SyncState.RUNNING;
        StatusText = "RUNNING";
        AddLog("Synchronization started.");
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

    private static WindowInfo CreateWindow(
        long hwnd,
        string title,
        uint processId,
        string processName,
        string className,
        int width,
        int height) => new((nint)hwnd, title, processId, processName, className, true, width, height);

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
