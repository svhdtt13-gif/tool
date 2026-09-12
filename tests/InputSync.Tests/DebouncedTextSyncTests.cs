using InputSync.Core.Text;

namespace InputSync.Tests;

/// <summary>Debounce fires once per burst after the application processed
/// the input; Flush runs pending work inline exactly once.</summary>
public sealed class DebouncedTextSyncTests
{
    private static void WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(5);
        }

        Assert.True(condition(), "Timed out waiting for debounced run.");
    }

    [Fact]
    public void RapidTriggers_CoalesceIntoOneRun()
    {
        int runs = 0;
        using var debouncer = new DebouncedTextSync(() => Interlocked.Increment(ref runs), delayMs: 30);

        for (int i = 0; i < 10; i++)
        {
            debouncer.Trigger();
        }

        WaitFor(() => Volatile.Read(ref runs) >= 1);
        Thread.Sleep(100);

        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public void Flush_RunsPendingInline_ExactlyOnce()
    {
        int runs = 0;
        using var debouncer = new DebouncedTextSync(() => Interlocked.Increment(ref runs), delayMs: 5000);

        debouncer.Trigger();
        debouncer.Trigger();
        debouncer.Flush();
        Thread.Sleep(50);

        Assert.Equal(1, Volatile.Read(ref runs));
    }

    [Fact]
    public void Dispose_CancelsPendingRun()
    {
        int runs = 0;
        var debouncer = new DebouncedTextSync(() => Interlocked.Increment(ref runs), delayMs: 20);
        debouncer.Trigger();
        debouncer.Dispose();
        Thread.Sleep(100);

        Assert.Equal(0, Volatile.Read(ref runs));
    }
}
