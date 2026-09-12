using System.Collections.ObjectModel;
using System.ComponentModel;
using InputSync.Core.Models;

namespace InputSync.Core.Controller;

public interface ISyncController : INotifyPropertyChanged
{
    ObservableCollection<WindowInfo> Windows { get; }

    ObservableCollection<SyncTarget> Targets { get; }

    ObservableCollection<string> EventLog { get; }

    WindowInfo? Source { get; set; }

    bool KeyboardEnabled { get; set; }

    bool MouseEnabled { get; set; }

    CoordinateMode CoordinateMode { get; set; }

    SyncState State { get; }

    string StatusText { get; }

    SyncMetrics Metrics { get; }

    bool DebugEnabled { get; }

    void RefreshWindows();

    void Start();

    void Pause();

    void Stop();

    void EmergencyStop();
}

public sealed class SyncTarget : INotifyPropertyChanged
{
    private bool _enabled;
    private TargetStatus _status;

    public SyncTarget(WindowInfo window, bool enabled = true)
    {
        Window = window;
        _enabled = enabled;
        _status = enabled ? TargetStatus.ACTIVE : TargetStatus.DISABLED;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WindowInfo Window { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            Status = value ? TargetStatus.ACTIVE : TargetStatus.DISABLED;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        }
    }

    public TargetStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }
}

public sealed class SyncMetrics : INotifyPropertyChanged
{
    private long _eventsReceived;
    private long _eventsDispatched;
    private long _eventsDropped;
    private long _filteredEvents;
    private int _activeTargets;
    private int _lostTargets;
    private double _averageLatencyMs;
    private double _maxLatencyMs;
    private string _mouseTrace = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long EventsReceived { get => _eventsReceived; set => Set(ref _eventsReceived, value, nameof(EventsReceived)); }

    public long EventsDispatched { get => _eventsDispatched; set => Set(ref _eventsDispatched, value, nameof(EventsDispatched)); }

    public long EventsDropped { get => _eventsDropped; set => Set(ref _eventsDropped, value, nameof(EventsDropped)); }

    public long FilteredEvents { get => _filteredEvents; set => Set(ref _filteredEvents, value, nameof(FilteredEvents)); }

    public int ActiveTargets { get => _activeTargets; set => Set(ref _activeTargets, value, nameof(ActiveTargets)); }

    public int LostTargets { get => _lostTargets; set => Set(ref _lostTargets, value, nameof(LostTargets)); }

    public double AverageLatencyMs { get => _averageLatencyMs; set => Set(ref _averageLatencyMs, value, nameof(AverageLatencyMs)); }

    public double MaxLatencyMs { get => _maxLatencyMs; set => Set(ref _maxLatencyMs, value, nameof(MaxLatencyMs)); }

    public string MouseTrace { get => _mouseTrace; set => Set(ref _mouseTrace, value, nameof(MouseTrace)); }

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
