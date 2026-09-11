using InputSync.Core.Controller;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Core.Safety;

namespace InputSync.Tests;

/// <summary>
/// Validates the real engine fan-out (SyncController + state tracker) that
/// RealSyncController drives: per-target delivery, stuck-key release on Stop,
/// and WINDOW_LOST marking without killing other targets.
/// </summary>
public sealed class EngineTests
{
    private sealed class FakeAdapter : ITargetAdapter
    {
        public List<KeyboardEventData> Keys { get; } = [];
        public List<MouseEventData> Mice { get; } = [];

        public bool SendKeyboard(KeyboardEventData eventData)
        {
            Keys.Add(eventData);
            return true;
        }

        public bool SendMouse(MouseEventData eventData)
        {
            Mice.Add(eventData);
            return true;
        }

        public bool IsSupported() => true;
    }

    private static SyncController CreateEngine(FakeAdapter adapter, Func<nint, bool>? isWindow = null)
    {
        var engine = new SyncController(adapter, new InputStateTracker(), isWindow ?? (_ => true));
        engine.SetSource(new nint(100));
        return engine;
    }

    private static void WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(10);
        }

        Assert.True(condition(), "Timed out waiting for engine delivery.");
    }

    [Fact]
    public void Start_QueuesAndFansOut_ToAllActiveTargets()
    {
        var adapter = new FakeAdapter();
        using var engine = CreateEngine(adapter);
        engine.AddTarget(new nint(200));
        engine.AddTarget(new nint(300));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x41, 30, KeyboardAction.Down)));

        WaitFor(() => adapter.Keys.Count >= 2);

        Assert.Equal(2, adapter.Keys.Count);
        Assert.Contains(adapter.Keys, k => k.TargetHwnd == new nint(200) && k.VirtualKey == 0x41);
        Assert.Contains(adapter.Keys, k => k.TargetHwnd == new nint(300) && k.VirtualKey == 0x41);
        engine.Stop();
    }

    [Fact]
    public void Stop_ReleasesPressedKeys_OnEveryTarget()
    {
        var adapter = new FakeAdapter();
        using var engine = CreateEngine(adapter);
        engine.AddTarget(new nint(200));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x41, 30, KeyboardAction.Down)));
        WaitFor(() => adapter.Keys.Count >= 1);

        engine.Stop();

        Assert.Contains(adapter.Keys, k => k.TargetHwnd == new nint(200) && k.Action == KeyboardAction.Up);
    }

    [Fact]
    public void LostTarget_MarkedLost_OthersKeepRunning()
    {
        var adapter = new FakeAdapter();
        var alive = new HashSet<nint> { new(100), new(200) };
        using var engine = CreateEngine(adapter, hwnd => alive.Contains(hwnd));
        engine.AddTarget(new nint(200));
        engine.AddTarget(new nint(300));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x41, 30, KeyboardAction.Down)));
        WaitFor(() => adapter.Keys.Count >= 1);

        Assert.Equal(TargetStatus.WINDOW_LOST, engine.Targets[new nint(300)]);
        Assert.DoesNotContain(adapter.Keys, k => k.TargetHwnd == new nint(300));
        Assert.Contains(adapter.Keys, k => k.TargetHwnd == new nint(200));
        engine.Stop();
    }

    [Fact]
    public void TryQueue_OnlyWhenRunning()
    {
        var adapter = new FakeAdapter();
        using var engine = CreateEngine(adapter);

        Assert.False(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x41, 30, KeyboardAction.Down)));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x41, 30, KeyboardAction.Down)));
        engine.Stop();
    }
}
