using InputSync.Core.Controller;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Core.Safety;
using InputSync.Core.Text;

namespace InputSync.Tests;

/// <summary>PR#8 regression: modifier and utility keys keep order, release
/// cleanly, and never leak into the text path.</summary>
public sealed class ModifierKeyTests
{
    private sealed class FakeAdapter : ITargetAdapter
    {
        public List<KeyboardEventData> Keys { get; } = [];

        public bool SendKeyboard(KeyboardEventData eventData)
        {
            Keys.Add(eventData);
            return true;
        }

        public bool SendMouse(MouseEventData eventData) => true;

        public bool IsSupported() => true;
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
    public void ModifierDown_KeyDown_KeyUp_ModifierUp_KeepOrder()
    {
        var adapter = new FakeAdapter();
        using var engine = new SyncController(adapter, new InputStateTracker(), _ => true);
        engine.SetSource(new nint(100));
        engine.AddTarget(new nint(200));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x11, 29, KeyboardAction.Down)));
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x43, 46, KeyboardAction.Down)));
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x43, 46, KeyboardAction.Up)));
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x11, 29, KeyboardAction.Up)));

        WaitFor(() => adapter.Keys.Count >= 4);

        uint[] order = adapter.Keys.Take(4).Select(k => k.VirtualKey).ToArray();
        Assert.Equal(new uint[] { 0x11, 0x43, 0x43, 0x11 }, order);
        Assert.Equal(KeyboardAction.Down, adapter.Keys[0].Action);
        Assert.Equal(KeyboardAction.Down, adapter.Keys[1].Action);
        Assert.Equal(KeyboardAction.Up, adapter.Keys[2].Action);
        Assert.Equal(KeyboardAction.Up, adapter.Keys[3].Action);
        engine.Stop();
    }

    [Fact]
    public void EmergencyStop_ReleasesHeldModifier()
    {
        var adapter = new FakeAdapter();
        using var engine = new SyncController(adapter, new InputStateTracker(), _ => true);
        engine.SetSource(new nint(100));
        engine.AddTarget(new nint(200));

        Assert.True(engine.Start());
        Assert.True(engine.TryQueueKeyboard(new KeyboardEventData(nint.Zero, 0x10, 42, KeyboardAction.Down)));
        WaitFor(() => adapter.Keys.Count >= 1);

        engine.EmergencyStop();

        Assert.Contains(
            adapter.Keys,
            k => k.TargetHwnd == new nint(200) && k.VirtualKey == 0x10 && k.Action == KeyboardAction.Up);
    }

    [Theory]
    [InlineData(0x41, false, false, false)]
    [InlineData(0x41, true, false, true)]
    [InlineData(0x41, true, true, true)]
    [InlineData(0x41, false, true, true)]
    [InlineData(0x10, false, false, true)]
    [InlineData(0x25, true, true, true)]
    [InlineData(0x70, false, false, true)]
    public void ShouldForwardAsControl_ModifierHeld_ForwardsLetter(
        uint vk, bool alt, bool ctrl, bool expected) =>
        Assert.Equal(expected, KeyClassifier.ShouldForwardAsControl(vk, alt, ctrl));

    [Fact]
    public void HeldKeyRepeat_AppendsEachTime()
    {
        TextEdit first = TextDiffer.Compute("", "a");
        TextEdit second = TextDiffer.Compute("a", "aa");
        TextEdit third = TextDiffer.Compute("aa", "aaa");

        Assert.Equal("a", first.Inserted);
        Assert.Equal("a", second.Inserted);
        Assert.Equal("a", third.Inserted);
    }
}
