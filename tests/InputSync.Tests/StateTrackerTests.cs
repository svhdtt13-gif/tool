using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Core.Safety;

namespace InputSync.Tests;

/// <summary>Slice 7 acceptance: STOP at any moment releases stuck keys/buttons per target.</summary>
public sealed class StateTrackerTests
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

    [Fact]
    public void ReleaseAll_SendsKeyUpAndClearsState()
    {
        var tracker = new InputStateTracker();
        var adapter = new FakeAdapter();
        var target = new nint(42);

        tracker.TrackDown(target, 0x57u);
        tracker.TrackDown(target, MouseButton.Left);
        tracker.ReleaseAll(target, adapter);

        KeyboardEventData keyUp = Assert.Single(adapter.Keys);
        Assert.Equal(0x57u, keyUp.VirtualKey);
        Assert.Equal(KeyboardAction.Up, keyUp.Action);

        MouseEventData buttonUp = Assert.Single(adapter.Mice);
        Assert.Equal(MouseButton.Left, buttonUp.Button);
        Assert.Equal(MouseAction.ButtonUp, buttonUp.Action);

        Assert.Empty(tracker.PressedKeys);
        Assert.Empty(tracker.PressedMouseButtons);
    }

    [Fact]
    public void TrackUp_RemovesSingleKey_KeepsOthers()
    {
        var tracker = new InputStateTracker();
        var target = new nint(7);

        tracker.TrackDown(target, 0x41u);
        tracker.TrackDown(target, 0x42u);
        tracker.TrackUp(target, 0x41u);

        Assert.Single(tracker.PressedKeys[target]);
        Assert.Contains(0x42u, tracker.PressedKeys[target]);
    }

    [Fact]
    public void ReleaseAll_IsolatedPerTarget()
    {
        var tracker = new InputStateTracker();
        var adapter = new FakeAdapter();
        var targetA = new nint(1);
        var targetB = new nint(2);

        tracker.TrackDown(targetA, 0x41u);
        tracker.TrackDown(targetB, 0x42u);
        tracker.ReleaseAll(targetA, adapter);

        Assert.Single(adapter.Keys);
        Assert.True(tracker.PressedKeys.ContainsKey(targetB));
    }
}
