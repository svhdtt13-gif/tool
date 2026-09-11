using System.Diagnostics;

namespace InputSync.Core.Dispatch;

/// <summary>
/// Correlates capture timestamps with adapter send completions to measure
/// end-to-end input latency without touching the engine queue protocol.
/// Thread-safe; drops oldest pending entries past capacity.
/// </summary>
public sealed class LatencyTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, long> _pending = [];
    private readonly Queue<Guid> _order = new();
    private readonly int _maxPending;
    private long _samples;
    private double _totalMs;
    private double _maxMs;

    public LatencyTracker(int maxPending = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPending);
        _maxPending = maxPending;
    }

    public long SampleCount
    {
        get { lock (_gate) { return _samples; } }
    }

    public double AverageMs
    {
        get { lock (_gate) { return _samples == 0 ? 0 : _totalMs / _samples; } }
    }

    public double MaxMs
    {
        get { lock (_gate) { return _maxMs; } }
    }

    public void RecordEnqueued(Guid correlationId, long timestampTicks)
    {
        if (correlationId == Guid.Empty || timestampTicks <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_pending.ContainsKey(correlationId))
            {
                return;
            }

            while (_pending.Count >= _maxPending && _order.TryDequeue(out Guid oldest))
            {
                _pending.Remove(oldest);
            }

            _pending[correlationId] = timestampTicks;
            _order.Enqueue(correlationId);
        }
    }

    public void RecordSent(Guid correlationId)
    {
        long timestampTicks;
        lock (_gate)
        {
            if (correlationId == Guid.Empty || !_pending.TryGetValue(correlationId, out timestampTicks))
            {
                return;
            }

            _pending.Remove(correlationId);
        }

        double milliseconds = TicksToMs(Math.Max(0, Stopwatch.GetTimestamp() - timestampTicks));
        lock (_gate)
        {
            _samples++;
            _totalMs += milliseconds;
            if (milliseconds > _maxMs)
            {
                _maxMs = milliseconds;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _order.Clear();
            _samples = 0;
            _totalMs = 0;
            _maxMs = 0;
        }
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
