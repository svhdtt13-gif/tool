using InputSync.Core.Dispatch;
using InputSync.Core.Models;
using InputSync.Win32;

namespace InputSync.Tests;

/// <summary>Focus rotation: each target focused in order, failures isolated,
/// exactly-once delivery per target, down/up ordering preserved.</summary>
public sealed class RotationAdapterTests
{
    private sealed class FakeInner : ITargetAdapter
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

    private static RotatingSendInputAdapter Create(
        FakeInner inner,
        List<nint> focusLog,
        HashSet<nint>? failing = null,
        Action<string>? trace = null) =>
        new(
            hwnd =>
            {
                focusLog.Add(hwnd);
                return failing is not null && failing.Contains(hwnd)
                    ? (false, "test focus failure")
                    : (true, "test focus ok");
            },
            inner,
            () => new nint(1),
            () => CoordinateMode.Relative,
            (data, target) => data with { TargetHwnd = target },
            trace);

    private static KeyboardEventData Key(nint target, uint vk, KeyboardAction action) =>
        new(target, vk, 30, action);

    [Fact]
    public void SendKeyboard_FocusesEachTargetInOrder_ExactlyOnce()
    {
        var inner = new FakeInner();
        var focusLog = new List<nint>();
        var adapter = Create(inner, focusLog);
        var id = Guid.NewGuid();

        Assert.True(adapter.SendKeyboard(Key(new nint(100), 0x41, KeyboardAction.Down) with { CorrelationId = id }));
        Assert.True(adapter.SendKeyboard(Key(new nint(200), 0x41, KeyboardAction.Down) with { CorrelationId = id }));

        Assert.Equal(new[] { new nint(100), new nint(200) }, focusLog);
        Assert.Equal(2, inner.Keys.Count);
        Assert.Equal(2, adapter.Sends);
        Assert.Equal(0, adapter.FocusFailures);
    }

    [Fact]
    public void FocusFailure_SkipsTarget_KeepsOthers()
    {
        var inner = new FakeInner();
        var focusLog = new List<nint>();
        var adapter = Create(inner, focusLog, new HashSet<nint> { new nint(200) });

        Assert.True(adapter.SendKeyboard(Key(new nint(100), 0x41, KeyboardAction.Down)));
        Assert.False(adapter.SendKeyboard(Key(new nint(200), 0x41, KeyboardAction.Down)));
        Assert.True(adapter.SendKeyboard(Key(new nint(300), 0x41, KeyboardAction.Down)));

        Assert.DoesNotContain(inner.Keys, k => k.TargetHwnd == new nint(200));
        Assert.Equal(2, inner.Keys.Count);
        Assert.Equal(1, adapter.FocusFailures);
        Assert.Equal(3, adapter.FocusAttempts);
    }

    [Fact]
    public void DownUp_PreservesOrder_PerTarget()
    {
        var inner = new FakeInner();
        var focusLog = new List<nint>();
        var adapter = Create(inner, focusLog);
        var target = new nint(100);

        adapter.SendKeyboard(Key(target, 0x10, KeyboardAction.Down));
        adapter.SendKeyboard(Key(target, 0x41, KeyboardAction.Down));
        adapter.SendKeyboard(Key(target, 0x41, KeyboardAction.Up));
        adapter.SendKeyboard(Key(target, 0x10, KeyboardAction.Up));

        uint[] order = inner.Keys.Select(k => k.VirtualKey).ToArray();
        Assert.Equal(new uint[] { 0x10, 0x41, 0x41, 0x10 }, order);
    }

    [Fact]
    public void ZeroTarget_ReturnsFalseWithoutFocus()
    {
        var inner = new FakeInner();
        var focusLog = new List<nint>();
        var adapter = Create(inner, focusLog);

        Assert.False(adapter.SendKeyboard(Key(nint.Zero, 0x41, KeyboardAction.Down)));
        Assert.Empty(focusLog);
        Assert.Empty(inner.Keys);
    }

    [Fact]
    public void Trace_ContainsRouteTargetAndResult()
    {
        var inner = new FakeInner();
        var focusLog = new List<nint>();
        var traces = new List<string>();
        var adapter = Create(inner, focusLog, trace: traces.Add);

        adapter.SendKeyboard(Key(new nint(100), 0x41, KeyboardAction.Down));

        Assert.Single(traces);
        Assert.Contains("route #", traces[0]);
        Assert.Contains("0x64", traces[0]);
        Assert.Contains("send=True", traces[0]);
    }
}
