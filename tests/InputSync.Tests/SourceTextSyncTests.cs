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
}
