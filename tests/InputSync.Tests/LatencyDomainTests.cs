using System.Diagnostics;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Win32;

namespace InputSync.Tests;

/// <summary>PR#7 regression: latency uses one Stopwatch domain (realistic ms,
/// avg &lt;= max) and invalid targets fail safely without throwing.</summary>
public sealed class LatencyDomainTests
{
    [Fact]
    public void StopwatchTimestamps_ProduceRealisticMs_AvgBelowMax()
    {
        var tracker = new LatencyTracker();

        for (int i = 0; i < 5; i++)
        {
            Guid id = Guid.NewGuid();
            tracker.RecordEnqueued(id, Stopwatch.GetTimestamp());
            Thread.Sleep(2);
            tracker.RecordSent(id);
        }

        Assert.Equal(5, tracker.SampleCount);
        Assert.True(tracker.AverageMs < 60_000, $"Average {tracker.AverageMs} ms is not realistic.");
        Assert.True(tracker.AverageMs <= tracker.MaxMs);
    }

    [Fact]
    public void Adapter_OnInvalidTarget_ReturnsFalseWithoutThrowing()
    {
        var adapter = new SyncTargetAdapter(() => nint.Zero, () => CoordinateMode.Relative);
        var bogus = new nint(0x0BADF00D);

        Assert.False(adapter.SendKeyboard(new KeyboardEventData(bogus, 0x41, 30, KeyboardAction.Down)));
        Assert.False(adapter.SendMouse(new MouseEventData(bogus, MouseAction.Move, 10, 10)));
        Assert.False(adapter.SendText(bogus, "abc"));
        Assert.False(adapter.SendText(bogus, string.Empty));
    }
}
