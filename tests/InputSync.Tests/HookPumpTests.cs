using System.Threading.Channels;
using InputSync.Core.Capture;

namespace InputSync.Tests;

/// <summary>Hook lifecycle: install on a pumping thread, idempotent
/// start/stop, clean dispose. Installs real global hooks briefly.</summary>
public sealed class HookPumpTests
{
    [Fact]
    public void Install_Uninstall_Dispose_NoThrow()
    {
        var channel = Channel.CreateUnbounded<RawHookEvent>();
        using var hooks = new LowLevelHooks(channel.Writer);

        hooks.Install();
        hooks.Install();
        hooks.Uninstall();
        hooks.Uninstall();
    }

    [Fact]
    public void Install_AfterDispose_Throws()
    {
        var channel = Channel.CreateUnbounded<RawHookEvent>();
        var hooks = new LowLevelHooks(channel.Writer);
        hooks.Dispose();

        Assert.Throws<ObjectDisposedException>(() => hooks.Install());
    }
}
