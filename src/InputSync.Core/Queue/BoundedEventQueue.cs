using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using InputSync.Core.Models;

namespace InputSync.Core.Queue;

public sealed class BoundedEventQueue
{
    public const int DefaultCapacity = 1000;

    private readonly object _overflowGate = new();
    private readonly Channel<NormalizedInputEvent> _channel;
    private readonly LinkedList<NormalizedInputEvent> _protectedOverflow = new();
    private long _received;
    private long _dropped;
    private long _latencySamples;
    private long _totalLatencyTicks;
    private long _maxLatencyTicks;
    private bool _completed;

    public BoundedEventQueue(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _channel = Channel.CreateBounded<NormalizedInputEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public int Capacity { get; }

    public long Received => Interlocked.Read(ref _received);

    public long Dropped => Interlocked.Read(ref _dropped);

    public TimeSpan AverageLatency
    {
        get
        {
            long samples = Interlocked.Read(ref _latencySamples);
            long ticks = Interlocked.Read(ref _totalLatencyTicks);
            return samples == 0
                ? TimeSpan.Zero
                : StopwatchTicksToTimeSpan((double)ticks / samples);
        }
    }

    public TimeSpan MaxLatency => StopwatchTicksToTimeSpan(Interlocked.Read(ref _maxLatencyTicks));

    public bool TryWrite(NormalizedInputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        Interlocked.Increment(ref _received);

        NormalizedInputEvent queuedEvent = inputEvent with
        {
            QueueTimestamp = Stopwatch.GetTimestamp()
        };

        lock (_overflowGate)
        {
            if (_completed)
            {
                return false;
            }

            if (_protectedOverflow.Count == 0 && _channel.Writer.TryWrite(queuedEvent))
            {
                return true;
            }

            if (queuedEvent.IsMouseMove &&
                _protectedOverflow.Last is { Value.IsMouseMove: true } lastMouseMove)
            {
                lastMouseMove.Value = queuedEvent;
                Interlocked.Increment(ref _dropped);
                return true;
            }

            _protectedOverflow.AddLast(queuedEvent);
            return true;
        }
    }

    public void Complete(Exception? error = null)
    {
        lock (_overflowGate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _channel.Writer.TryComplete(error);
        }
    }

    public async IAsyncEnumerable<NormalizedInputEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            while (_channel.Reader.TryRead(out NormalizedInputEvent? inputEvent))
            {
                yield return inputEvent;
            }

            NormalizedInputEvent? overflowEvent = null;
            lock (_overflowGate)
            {
                if (_protectedOverflow.First is { } first)
                {
                    overflowEvent = first.Value;
                    _protectedOverflow.RemoveFirst();
                }
            }

            if (overflowEvent is not null)
            {
                yield return overflowEvent;
                continue;
            }

            if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }
        }
    }

    public void RecordLatency(NormalizedInputEvent inputEvent)
    {
        if (inputEvent.QueueTimestamp <= 0)
        {
            return;
        }

        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - inputEvent.QueueTimestamp);
        Interlocked.Increment(ref _latencySamples);
        Interlocked.Add(ref _totalLatencyTicks, elapsed);

        long currentMax = Interlocked.Read(ref _maxLatencyTicks);
        while (elapsed > currentMax)
        {
            long observed = Interlocked.CompareExchange(ref _maxLatencyTicks, elapsed, currentMax);
            if (observed == currentMax)
            {
                break;
            }

            currentMax = observed;
        }
    }

    private static TimeSpan StopwatchTicksToTimeSpan(double ticks) =>
        TimeSpan.FromSeconds(ticks / Stopwatch.Frequency);
}
