using System.Diagnostics;
using InputSync.Core.Capture;
using InputSync.Core.Controller;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Core.Safety;

namespace InputSync.Tests;

public sealed class GameRotationEngineTests
{
    private sealed class FakeSender : ITargetAdapter
    {
        public List<KeyboardEventData> Keys { get; } = [];

        public List<MouseEventData> Mice { get; } = [];

        public HashSet<nint>? FailingTargets { get; set; }

        public bool SendKeyboard(KeyboardEventData eventData)
        {
            Keys.Add(eventData);
            return FailingTargets?.Contains(eventData.TargetHwnd) != true;
        }

        public bool SendMouse(MouseEventData eventData)
        {
            Mice.Add(eventData);
            return FailingTargets?.Contains(eventData.TargetHwnd) != true;
        }

        public bool IsSupported() => true;
    }

    private static GameRotationEngine CreateEngine(
        FakeSender sender,
        List<nint> focusLog,
        HashSet<nint>? failingFocus = null,
        HashSet<nint>? failingSend = null,
        Action<string>? trace = null,
        Func<MouseEventData, nint, MouseEventData>? translate = null,
        Func<nint, bool>? isWindow = null)
    {
        sender.FailingTargets = failingSend;
        var engine = new GameRotationEngine(
            sender,
            new InputStateTracker(),
            isWindow ?? (_ => true),
            hwnd =>
            {
                focusLog.Add(hwnd);
                return failingFocus?.Contains(hwnd) == true
                    ? (false, "test focus failure")
                    : (true, "test focus ok");
            },
            translate ?? ((data, target) => data with { TargetHwnd = target }),
            trace);
        engine.SetSource(new nint(1));
        return engine;
    }

    private static void WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(10);
        }

        Assert.True(condition(), "Timed out waiting for rotation engine delivery.");
    }

    private static KeyboardEventData Key(nint dummy, uint virtualKey, KeyboardAction action) =>
        new(dummy, virtualKey, 30, action);

    private static MouseEventData Mouse(
        MouseAction action,
        MouseButton button = MouseButton.None,
        int wheelDelta = 0) =>
        new(nint.Zero, action, 10, 20, button, WheelDelta: wheelDelta);

    [Fact]
    public void RotationOrder_ABC_ExactlyOnce()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        var traces = new List<string>();
        using var engine = CreateEngine(sender, focusLog, trace: traces.Add);
        engine.AddTarget(new nint(100));
        engine.AddTarget(new nint(200));
        engine.AddTarget(new nint(300));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x31, KeyboardAction.Down)));
        WaitFor(() => engine.Sends >= 3 && traces.Count >= 3);

        Assert.Equal(new nint[] { new(100), new(200), new(300) }, focusLog);
        Assert.Equal(3, sender.Keys.Count);
        Assert.Equal(3, sender.Keys.Select(key => key.TargetHwnd).Distinct().Count());
        Assert.Equal(3, engine.EventsDispatched);
        Assert.All(traces, trace =>
        {
            Assert.Contains("route #", trace);
            Assert.Contains("send=True", trace);
        });
        Assert.Contains(traces, trace => trace.Contains("0x64"));
        Assert.Contains(traces, trace => trace.Contains("0xC8"));
        Assert.Contains(traces, trace => trace.Contains("0x12C"));
    }

    [Fact]
    public void FocusFailure_SkipsTarget_KeepsOthers()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        using var engine = CreateEngine(sender, focusLog, new HashSet<nint> { new(200) });
        engine.AddTarget(new nint(100));
        engine.AddTarget(new nint(200));
        engine.AddTarget(new nint(300));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down)));
        WaitFor(() => engine.FocusAttempts >= 3);

        Assert.Equal(new nint[] { new(100), new(200), new(300) }, focusLog);
        Assert.Contains(sender.Keys, key => key.TargetHwnd == new nint(100));
        Assert.DoesNotContain(sender.Keys, key => key.TargetHwnd == new nint(200));
        Assert.Contains(sender.Keys, key => key.TargetHwnd == new nint(300));
        Assert.Equal(1, engine.FocusFailures);
        Assert.Equal(3, engine.FocusAttempts);
        Assert.Equal(2, engine.EventsDispatched);
    }

    [Fact]
    public void DownUp_PreservesOrder_PerTarget()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        using var engine = CreateEngine(sender, focusLog);
        engine.AddTarget(new nint(100));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x10, KeyboardAction.Down)));
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down)));
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Up)));
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x10, KeyboardAction.Up)));
        WaitFor(() => sender.Keys.Count >= 4);

        Assert.Equal(new uint[] { 0x10, 0x41, 0x41, 0x10 }, sender.Keys.Select(key => key.VirtualKey));
        Assert.Equal(
            new[] { KeyboardAction.Down, KeyboardAction.Down, KeyboardAction.Up, KeyboardAction.Up },
            sender.Keys.Select(key => key.Action));
    }

    [Fact]
    public void Click_Wheel_Preserved_NotCoalesced()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        using var engine = CreateEngine(sender, focusLog);
        engine.AddTarget(new nint(100));
        engine.AddTarget(new nint(200));
        engine.AddTarget(new nint(300));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueMouse(Mouse(MouseAction.ButtonDown, MouseButton.Left)));
        Assert.True(engine.TryQueueMouse(Mouse(MouseAction.ButtonUp, MouseButton.Left)));
        Assert.True(engine.TryQueueMouse(Mouse(MouseAction.Wheel, wheelDelta: 120)));
        WaitFor(() => sender.Mice.Count >= 9);

        foreach (nint target in new nint[] { new(100), new(200), new(300) })
        {
            MouseEventData[] events = sender.Mice.Where(mouse => mouse.TargetHwnd == target).ToArray();
            Assert.Equal(3, events.Length);
            Assert.Equal(
                new[] { MouseAction.ButtonDown, MouseAction.ButtonUp, MouseAction.Wheel },
                events.Select(mouse => mouse.Action));
            Assert.Equal(MouseButton.Left, events[0].Button);
            Assert.Equal(MouseButton.Left, events[1].Button);
            Assert.Equal(120, events[2].WheelDelta);
        }
    }

    [Fact]
    public void InjectedInput_NeverReachesRotationQueue()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        using var engine = CreateEngine(sender, focusLog);
        Assert.True(engine.Start());

        Assert.True(LowLevelHooks.IsInjectedKeyboard(0x10));
        Assert.True(LowLevelHooks.IsInjectedMouse(0x01));
        Assert.True(LowLevelHooks.IsInjectedMouse(0x02));

        // Hook callbacks drop injected events before channel write and still call CallNextHookEx.
        // GameRotationEngine therefore only accepts normalized events that survived this filter.
        if (!LowLevelHooks.IsInjectedKeyboard(0x10))
        {
            engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down));
        }

        if (!LowLevelHooks.IsInjectedMouse(0x01))
        {
            engine.TryQueueMouse(Mouse(MouseAction.ButtonDown, MouseButton.Left));
        }

        Assert.Equal(0, engine.EventsReceived);
        Assert.Empty(sender.Keys);
        Assert.Empty(sender.Mice);
    }

    [Fact]
    public void EmergencyStop_MidRotation_NoDeadlock()
    {
        var sender = new FakeSender();
        var focusEntered = new ManualResetEventSlim();
        using var engine = new GameRotationEngine(
            sender,
            new InputStateTracker(),
            _ => true,
            hwnd =>
            {
                focusEntered.Set();
                Thread.Sleep(200);
                return (true, $"test focus ok {hwnd}");
            });
        engine.SetSource(new nint(1));
        engine.AddTarget(new nint(100));
        engine.AddTarget(new nint(200));
        engine.AddTarget(new nint(300));

        Assert.True(engine.Start());
        for (uint virtualKey = 0x31; virtualKey <= 0x35; virtualKey++)
        {
            Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, virtualKey, KeyboardAction.Down)));
        }

        Assert.True(focusEntered.Wait(TimeSpan.FromSeconds(1)), "Focus simulation was not entered.");
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = Record.Exception(engine.EmergencyStop);
        stopwatch.Stop();

        Assert.Null(exception);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, "EmergencyStop did not return promptly.");
        Assert.Equal(SyncState.IDLE, engine.State);
        Assert.False(engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down)));
    }

    [Fact]
    public void ZeroTarget_Safe()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        using var engine = CreateEngine(sender, focusLog);

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down)));
        WaitFor(() => engine.EventsReceived == 1);
        engine.Stop();

        Assert.Equal(SyncState.IDLE, engine.State);
        Assert.Equal(0, engine.Sends);
        Assert.Empty(sender.Keys);
        Assert.Empty(focusLog);
    }

    [Fact]
    public void LostTarget_Skipped_OthersContinue()
    {
        var sender = new FakeSender();
        var focusLog = new List<nint>();
        var alive = new HashSet<nint> { new(1), new(100), new(200) };
        using var engine = CreateEngine(sender, focusLog, isWindow: alive.Contains);
        Assert.True(engine.AddTarget(new nint(100)));
        Assert.True(engine.AddTarget(new nint(200)));
        alive.Remove(new nint(200));

        engine.RefreshTargets();
        Assert.Equal(TargetStatus.WINDOW_LOST, engine.Targets[new nint(200)]);
        Assert.Equal(1, engine.LostTargets);

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down)));
        WaitFor(() => sender.Keys.Count >= 1);

        Assert.Contains(sender.Keys, key => key.TargetHwnd == new nint(100));
        Assert.DoesNotContain(sender.Keys, key => key.TargetHwnd == new nint(200));
        Assert.Equal(1, engine.EventsDispatched);
    }
}
