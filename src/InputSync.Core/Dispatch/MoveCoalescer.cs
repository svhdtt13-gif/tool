using System.Diagnostics;
using InputSync.Core.Models;

namespace InputSync.Core.Dispatch;

/// <summary>
/// Coalesces rapid MouseMove events so the dispatcher is never backlogged
/// by cursor motion. Only moves coalesce: button and wheel events always
/// flush any pending move first, preserving click order. Single-threaded
/// use (pump loop); deterministic clock injection for tests.
/// </summary>
public sealed class MoveCoalescer
{
    private readonly Func<long> _clock;
    private readonly long _windowTicks;
    private MouseEventData? _pending;
    private long _lastSentTicks;

    public MoveCoalescer(Func<long>? clock = null, int windowMs = 8)
    {
        _clock = clock ?? Stopwatch.GetTimestamp;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowMs);
        _windowTicks = windowMs * Stopwatch.Frequency / 1000;
    }

    public bool OfferMove(MouseEventData move, out MouseEventData toSend)
    {
        long now = _clock();
        if (now - _lastSentTicks >= _windowTicks)
        {
            toSend = _pending ?? move;
            _pending = null;
            _lastSentTicks = now;
            return true;
        }

        _pending = move;
        toSend = default;
        return false;
    }

    public bool Flush(out MouseEventData pending)
    {
        if (_pending is { } move)
        {
            _pending = null;
            _lastSentTicks = _clock();
            pending = move;
            return true;
        }

        pending = default;
        return false;
    }

    public void Clear() => _pending = null;
}
