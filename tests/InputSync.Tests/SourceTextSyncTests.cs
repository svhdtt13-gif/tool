using InputSync.Win32;

namespace InputSync.Tests;

/// <summary>PR#8 regression: while modifiers are held, snapshots advance
/// silently (Ctrl+A/C/V/X execute on targets themselves), so no duplicate
/// text is ever emitted.</summary>
public sealed class SourceTextSyncTests
{
    [Fact]
    public void Adopt_AdvancesSnapshot_WithoutEmitting()
    {
        string current = "ab";
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => current, emitted.Add);

        Assert.Equal(string.Empty, sync.Sync());

        current = "abPASTED";
        sync.Adopt();

        Assert.Empty(emitted);

        current = "abPASTED!";
        Assert.Equal("!", sync.Sync());
        Assert.Equal(new[] { "!" }, emitted);
    }

    [Fact]
    public void Sync_EmitsObservedDelta_Only()
    {
        string current = "";
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => current, emitted.Add);

        sync.Sync();
        current = "abc";
        sync.Sync();

        Assert.Equal(new[] { "abc" }, emitted);
    }

    [Fact]
    public void ReadFailure_EmitsNothing()
    {
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => throw new InvalidOperationException("unreadable"), emitted.Add);

        Assert.Equal(string.Empty, sync.Sync());
        Assert.Empty(emitted);
    }

    [Fact]
    public void AdoptUntilChanged_AdoptsPasteSilently()
    {
        string current = "ab";
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => current, emitted.Add);
        sync.Sync();

        current = "abPASTED";
        Assert.True(sync.AdoptUntilChanged(500));
        Assert.Empty(emitted);

        current = "abPASTED!";
        Assert.Equal("!", sync.Sync());
        Assert.Equal(new[] { "!" }, emitted);
    }

    [Fact]
    public void AdoptUntilChanged_TimeoutWhenStatic()
    {
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => "steady", emitted.Add);
        sync.Sync();

        Assert.False(sync.AdoptUntilChanged(60, 20));
        Assert.Empty(emitted);
    }

    [Fact]
    public void Trace_ReportsSnapshotDiffEmit()
    {
        string current = "";
        var emitted = new List<string>();
        var traces = new List<string>();
        var sync = new SourceTextSync(() => current, emitted.Add, trace: traces.Add);
        sync.Sync();

        current = "ab";
        sync.Sync();

        Assert.Single(traces);
        Assert.Contains("snaplen=0", traces[0]);
        Assert.Contains("backs=0", traces[0]);
    }

    [Fact]
    public void SyncStable_WaitsForSettledText_BeforeEmitting()
    {
        var reads = new Queue<string>(new[] { "", "a", "ab", "ab", "ab" });
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => reads.Count > 0 ? reads.Dequeue() : "ab", emitted.Add);
        sync.Sync();

        Assert.Equal("ab", sync.SyncStable(pollMs: 1, timeoutMs: 500));
        Assert.Equal(new[] { "ab" }, emitted);
    }

    [Fact]
    public void SyncStable_Timeout_EmitsCurrent()
    {
        int n = 0;
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => $"v{System.Threading.Interlocked.Increment(ref n)}", emitted.Add);
        sync.Sync();

        string result = sync.SyncStable(pollMs: 1, timeoutMs: 60);

        Assert.False(string.IsNullOrEmpty(result));
        Assert.Single(emitted);
    }

    [Fact]
    public async Task ConcurrentSync_IsSerialized()
    {
        string current = "v0";
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => current, emitted.Add);
        sync.Sync();

        var tasks = new Task[8];
        for (int i = 0; i < tasks.Length; i++)
        {
            int n = i;
            tasks[i] = Task.Run(() =>
            {
                current = $"v{n}";
                sync.Sync();
            });
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ConcurrentSync_StaticText_EmitsNothing()
    {
        var emitted = new List<string>();
        var sync = new SourceTextSync(() => "steady", emitted.Add);
        sync.Sync();

        var tasks = new Task[8];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => sync.Sync());
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(emitted);
    }
}
