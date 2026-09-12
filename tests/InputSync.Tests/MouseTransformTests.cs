using InputSync.Core.Models;
using InputSync.Core.Normalization;

namespace InputSync.Tests;

/// <summary>Mouse coordinates scale between top-level client areas only;
/// child (edit) sizes must never enter the transform. Ratios are
/// DPI-agnostic by construction.</summary>
public sealed class MouseTransformTests
{
    private static readonly EventNormalizer Normalizer = new();

    [Fact]
    public void Relative_CenterMapsToCenter_AcrossSizes()
    {
        (int x, int y) = Normalizer.ToTargetCoords(0.5, 0.5, 1920, 1080);

        Assert.Equal(960, x);
        Assert.Equal(540, y);
    }

    [Fact]
    public void Relative_OriginMapsToOrigin()
    {
        (int x, int y) = Normalizer.ToTargetCoords(0.0, 0.0, 1920, 1080);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Relative_AsymmetricSizes()
    {
        (int x, int y) = Normalizer.ToTargetCoords(640.0 / 1280, 360.0 / 720, 800, 600);

        Assert.Equal(400, x);
        Assert.Equal(300, y);
    }

    [Fact]
    public void Absolute_PassesThrough()
    {
        var evt = Normalizer.NormalizeMouse(
            new nint(1), InputAction.MouseMove, 640, 360, 1280, 720,
            System.Diagnostics.Stopwatch.GetTimestamp());

        (int x, int y) = Normalizer.ToTargetCoords(evt, 1920, 1080, CoordinateMode.Absolute);

        Assert.Equal(640, x);
        Assert.Equal(360, y);
    }
}
