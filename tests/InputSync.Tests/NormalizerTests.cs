using System.Diagnostics;
using InputSync.Core.Models;
using InputSync.Core.Normalization;

namespace InputSync.Tests;

/// <summary>Slice 4 acceptance: correct coordinate conversion between different window sizes.</summary>
public sealed class NormalizerTests
{
    [Fact]
    public void NormalizeKeyboard_PreservesVkAndScanCode()
    {
        var normalizer = new EventNormalizer();
        long timestamp = Stopwatch.GetTimestamp();

        NormalizedInputEvent evt = normalizer.NormalizeKeyboard(
            new nint(123), InputAction.KeyDown, virtualKey: 0x57, scanCode: 17, flags: 0, timestamp);

        Assert.Equal(InputEventType.Keyboard, evt.Type);
        Assert.Equal(nameof(InputAction.KeyDown), evt.Action);
        Assert.Equal(0x57u, evt.Vk);
        Assert.Equal(17u, evt.ScanCode);
        Assert.Equal(timestamp, evt.Timestamp);
        Assert.Equal(new nint(123), evt.SourceHwnd);
    }

    [Fact]
    public void NormalizeMouse_ProducesRelativeCoordinates()
    {
        var normalizer = new EventNormalizer();

        NormalizedInputEvent evt = normalizer.NormalizeMouse(
            new nint(1), InputAction.MouseMove, 640, 360, 1280, 720, Stopwatch.GetTimestamp());

        Assert.Equal(0.5, evt.NormalizedX);
        Assert.Equal(0.5, evt.NormalizedY);
        Assert.Equal(InputAction.MouseMove.ToString(), evt.Action);
    }

    [Fact]
    public void ToTargetCoords_RelativeMode_ScalesAcrossSizes()
    {
        // Spec example: source 1280x720 at (640,360) -> target 1920x1080 at (960,540).
        var normalizer = new EventNormalizer();
        NormalizedInputEvent evt = normalizer.NormalizeMouse(
            new nint(1), InputAction.MouseMove, 640, 360, 1280, 720, Stopwatch.GetTimestamp());

        (int x, int y) = normalizer.ToTargetCoords(evt, 1920, 1080, CoordinateMode.Relative);

        Assert.Equal(960, x);
        Assert.Equal(540, y);
    }

    [Fact]
    public void ToTargetCoords_AbsoluteMode_PassesThrough()
    {
        var normalizer = new EventNormalizer();
        NormalizedInputEvent evt = normalizer.NormalizeMouse(
            new nint(1), InputAction.MouseMove, 640, 360, 1280, 720, Stopwatch.GetTimestamp());

        (int x, int y) = normalizer.ToTargetCoords(evt, 1920, 1080, CoordinateMode.Absolute);

        Assert.Equal(640, x);
        Assert.Equal(360, y);
    }
}
