using InputSync.Core.Dispatch;
using InputSync.Core.Models;

namespace InputSync.Tests;

/// <summary>Moves within the window coalesce to the latest position;
/// button and wheel events always flush first, preserving click order.</summary>
public sealed class MoveCoalescerTests
{
    private static MouseEventData Move(int x, int y) =>
        new(nint.Zero, MouseAction.Move, x, y);

    private sealed class Clock
    {
        public long Now = System.Diagnostics.Stopwatch.Frequency * 1000;
        public long Read() => Now;
    }

    [Fact]
    public void FirstMove_SendsImmediately()
    {
        var clock = new Clock();
        var coalescer = new MoveCoalescer(clock.Read);

        Assert.True(coalescer.OfferMove(Move(10, 10), out MouseEventData sent));
        Assert.Equal(10, sent.X);
    }

    [Fact]
    public void RapidMoves_CoalesceToLatest()
    {
        var clock = new Clock();
        var coalescer = new MoveCoalescer(clock.Read);

        Assert.True(coalescer.OfferMove(Move(0, 0), out _));
        clock.Now += 2 * StopwatchFrequencyMs(1);
        Assert.False(coalescer.OfferMove(Move(10, 10), out _));
        Assert.False(coalescer.OfferMove(Move(20, 20), out _));

        clock.Now += 20 * StopwatchFrequencyMs(1);
        Assert.True(coalescer.OfferMove(Move(30, 30), out MouseEventData sent));
        Assert.Equal(20, sent.X);
        Assert.Equal(20, sent.Y);
    }

    [Fact]
    public void Flush_ReturnsPendingMove()
    {
        var clock = new Clock();
        var coalescer = new MoveCoalescer(clock.Read);

        Assert.True(coalescer.OfferMove(Move(0, 0), out _));
        Assert.False(coalescer.OfferMove(Move(5, 5), out _));

        Assert.True(coalescer.Flush(out MouseEventData pending));
        Assert.Equal(5, pending.X);
        Assert.False(coalescer.Flush(out _));
    }

    [Fact]
    public void Clear_DropsPending()
    {
        var clock = new Clock();
        var coalescer = new MoveCoalescer(clock.Read);

        Assert.True(coalescer.OfferMove(Move(0, 0), out _));
        Assert.False(coalescer.OfferMove(Move(5, 5), out _));
        coalescer.Clear();

        Assert.False(coalescer.Flush(out _));
    }

    private static long StopwatchFrequencyMs(long ms) =>
        System.Diagnostics.Stopwatch.Frequency * ms / 1000;
}
