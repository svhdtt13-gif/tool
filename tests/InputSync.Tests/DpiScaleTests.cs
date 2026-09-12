using InputSync.Core.Normalization;

namespace InputSync.Tests;

/// <summary>Physical/logical conversion at common Windows scales.</summary>
public sealed class DpiScaleTests
{
    [Theory]
    [InlineData(1920, 96, 1920)]
    [InlineData(1920, 120, 1536)]
    [InlineData(1920, 144, 1280)]
    [InlineData(1920, 168, 1097)]
    [InlineData(0, 144, 0)]
    public void ToLogical_ScalesByDpi(int physical, int dpi, int expected) =>
        Assert.Equal(expected, DpiScale.ToLogical(physical, dpi));

    [Theory]
    [InlineData(1536, 120, 1920)]
    [InlineData(1280, 144, 1920)]
    [InlineData(960, 120, 1200)]
    public void ToPhysical_ScalesByDpi(int logical, int dpi, int expected) =>
        Assert.Equal(expected, DpiScale.ToPhysical(logical, dpi));

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        foreach (int dpi in new[] { 96, 120, 144, 168, 192 })
        {
            int logical = DpiScale.ToLogical(1920, dpi);
            Assert.Equal(1920, DpiScale.ToPhysical(logical, dpi));
        }
    }

    [Fact]
    public void InvalidDpi_PassesThrough()
    {
        Assert.Equal(500, DpiScale.ToLogical(500, 0));
        Assert.Equal(500, DpiScale.ToPhysical(500, -1));
    }
}
