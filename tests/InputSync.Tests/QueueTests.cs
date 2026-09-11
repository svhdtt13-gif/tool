using System.Diagnostics;
using InputSync.Core.Models;
using InputSync.Core.Queue;

namespace InputSync.Tests;

/// <summary>Slice 5 acceptance: bounded queue never blocks capture and never drops semantic events.</summary>
public sealed class QueueTests
{
    private static NormalizedInputEvent KeyEvent(InputAction action, uint vk = 0x57) => new()
    {
        SourceHwnd = new nint(1),
        Type = InputEventType.Keyboard,
        Action = action.ToString(),
        Vk = vk,
        Timestamp = Stopwatch.GetTimestamp(),
    };

    private static NormalizedInputEvent MouseEvent(InputAction action, int x = 0, int y = 0) => new()
    {
        SourceHwnd = new nint(1),
        Type = InputEventType.Mouse,
        Action = action.ToString(),
        X = x,
        Y = y,
        Timestamp = Stopwatch.GetTimestamp(),
    };

    [Fact]
    public async Task ConsecutiveMouseMoves_Coalesce_DownUpPreserved()
    {
        var queue = new BoundedEventQueue(capacity: 1);

        Assert.True(queue.TryWrite(KeyEvent(InputAction.KeyDown)));
        Assert.True(queue.TryWrite(MouseEvent(InputAction.MouseMove, x: 10, y: 10)));
        Assert.True(queue.TryWrite(MouseEvent(InputAction.MouseMove, x: 20, y: 20)));
        queue.Complete();

        var received = new List<NormalizedInputEvent>();
        await foreach (NormalizedInputEvent evt in queue.ReadAllAsync())
        {
            received.Add(evt);
        }

        Assert.Equal(3, queue.Received);
        Assert.Equal(1, queue.Dropped);
        Assert.Equal(2, received.Count);
        Assert.Contains(received, e => e.Type == InputEventType.Keyboard && e.Vk == 0x57);

        NormalizedInputEvent move = Assert.Single(received, e => e.IsMouseMove);
        Assert.Equal(20, move.X);
        Assert.Equal(20, move.Y);
    }

    [Fact]
    public async Task ButtonDownUp_NeverCoalesced()
    {
        var queue = new BoundedEventQueue(capacity: 1);

        Assert.True(queue.TryWrite(MouseEvent(InputAction.MouseMove)));
        Assert.True(queue.TryWrite(MouseEvent(InputAction.LeftDown)));
        Assert.True(queue.TryWrite(MouseEvent(InputAction.LeftUp)));
        queue.Complete();

        var received = new List<NormalizedInputEvent>();
        await foreach (NormalizedInputEvent evt in queue.ReadAllAsync())
        {
            received.Add(evt);
        }

        Assert.Equal(0, queue.Dropped);
        Assert.Contains(received, e => e.Action == nameof(InputAction.LeftDown));
        Assert.Contains(received, e => e.Action == nameof(InputAction.LeftUp));
    }

    [Fact]
    public void Metrics_StartAtZero()
    {
        var queue = new BoundedEventQueue();

        Assert.Equal(0, queue.Received);
        Assert.Equal(0, queue.Dropped);
        Assert.Equal(TimeSpan.Zero, queue.AverageLatency);
        Assert.Equal(TimeSpan.Zero, queue.MaxLatency);
    }
}
