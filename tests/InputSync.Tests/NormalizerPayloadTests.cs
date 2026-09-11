using System.Diagnostics;
using InputSync.Core.Models;
using InputSync.Core.Normalization;

namespace InputSync.Tests;

/// <summary>Extended flag and printable-text payload flow through normalization.</summary>
public sealed class NormalizerPayloadTests
{
    [Fact]
    public void NormalizeKeyboard_SetsExtendedFromHookFlags_AndText()
    {
        var normalizer = new EventNormalizer();

        NormalizedInputEvent evt = normalizer.NormalizeKeyboard(
            new nint(1), InputAction.KeyDown, 0x41, 30, flags: 0x1, Stopwatch.GetTimestamp(), text: "A");

        Assert.True(evt.Extended);
        Assert.Equal("A", evt.Text);
        Assert.Equal(0x41u, evt.Vk);
    }

    [Fact]
    public void NormalizeKeyboard_Defaults_EmptyText_NotExtended()
    {
        var normalizer = new EventNormalizer();

        NormalizedInputEvent evt = normalizer.NormalizeKeyboard(
            new nint(1), InputAction.KeyDown, 0x41, 30, flags: 0, Stopwatch.GetTimestamp());

        Assert.False(evt.Extended);
        Assert.Equal(string.Empty, evt.Text);
    }

    [Fact]
    public void EventData_PreservesTextAndCorrelation_ThroughWith()
    {
        Guid id = Guid.NewGuid();
        var original = new KeyboardEventData(nint.Zero, 0x41, 30, KeyboardAction.Down, false, "A", id);

        KeyboardEventData routed = original with { TargetHwnd = new nint(200) };

        Assert.Equal("A", routed.Text);
        Assert.Equal(id, routed.CorrelationId);
        Assert.Equal(new nint(200), routed.TargetHwnd);
    }
}
