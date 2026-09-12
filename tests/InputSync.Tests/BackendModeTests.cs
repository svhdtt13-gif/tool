using InputSync.Core.Models;
using InputSync.Win32;

namespace InputSync.Tests;

/// <summary>Backend routing defaults and safe failure without a source.
/// No hooks are installed: Start rejects before capture when Source is null.</summary>
public sealed class BackendModeTests
{
    [Fact]
    public void BackendMode_DefaultsToBroadcast()
    {
        using var controller = new RealSyncController();

        Assert.Equal(TargetBackend.Broadcast, controller.BackendMode);
    }

    [Fact]
    public void BackendMode_Roundtrips()
    {
        using var controller = new RealSyncController();

        controller.BackendMode = TargetBackend.Foreground;
        Assert.Equal(TargetBackend.Foreground, controller.BackendMode);

        controller.BackendMode = TargetBackend.Broadcast;
        Assert.Equal(TargetBackend.Broadcast, controller.BackendMode);
    }

    [Theory]
    [InlineData(TargetBackend.Broadcast)]
    [InlineData(TargetBackend.Foreground)]
    public void Start_WithoutSource_ErrorsNeverCaptures(TargetBackend backend)
    {
        using var controller = new RealSyncController();
        controller.Source = null;
        controller.BackendMode = backend;

        controller.Start();

        Assert.Equal(SyncState.ERROR, controller.State);
        Assert.Equal("SOURCE LOST", controller.StatusText);
    }
}
