using System.Diagnostics;
using InputSync.Core.Dispatch;

namespace InputSync.Tests;

/// <summary>LatencyTracker correlates capture timestamps with send completions.</summary>
public sealed class LatencyTrackerTests
{
    [Fact]
    public void EnqueueThenSend_ProducesSample()
    {
        var tracker = new LatencyTracker();
        Guid id = Guid.NewGuid();

        tracker.RecordEnqueued(id, Stopwatch.GetTimestamp());
        Thread.Sleep(5);
        tracker.RecordSent(id);

        Assert.Equal(1, tracker.SampleCount);
        Assert.True(tracker.AverageMs >= 0);
        Assert.True(tracker.MaxMs >= tracker.AverageMs);
    }

    [Fact]
    public void UnknownOrEmptyId_Ignored()
    {
        var tracker = new LatencyTracker();

        tracker.RecordSent(Guid.NewGuid());
        tracker.RecordSent(Guid.Empty);
        tracker.RecordEnqueued(Guid.Empty, Stopwatch.GetTimestamp());

        Assert.Equal(0, tracker.SampleCount);
        Assert.Equal(0, tracker.AverageMs);
        Assert.Equal(0, tracker.MaxMs);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var tracker = new LatencyTracker();
        Guid id = Guid.NewGuid();

        tracker.RecordEnqueued(id, Stopwatch.GetTimestamp());
        tracker.RecordSent(id);
        tracker.Reset();

        Assert.Equal(0, tracker.SampleCount);
        Assert.Equal(0, tracker.AverageMs);
        Assert.Equal(0, tracker.MaxMs);
    }
}
