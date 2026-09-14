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

    [Theory]
    [InlineData(0u, false)]
    [InlineData(0x01u, false)]
    [InlineData(0x10u, true)]
    [InlineData(0x90u, true)]
    public void IsInjectedKeyboard_ClassifiesHookFlags(uint flags, bool expected) =>
        Assert.Equal(expected, LowLevelHooks.IsInjectedKeyboard(flags));

    [Theory]
    [InlineData(0u, false)]
    [InlineData(0x01u, true)]
    [InlineData(0x02u, true)]
    [InlineData(0x03u, true)]
    public void IsInjectedMouse_ClassifiesHookFlags(uint flags, bool expected) =>
        Assert.Equal(expected, LowLevelHooks.IsInjectedMouse(flags));

    [Fact]
    public void IsSelfEchoKeyboard_RequiresTagMatch()
    {
        nuint tag = LowLevelHooks.SelfExtraInfoTag;
        Assert.True(LowLevelHooks.IsSelfEchoKeyboard(0x10u, tag));
        Assert.False(LowLevelHooks.IsSelfEchoKeyboard(0x10u, 0));
        Assert.False(LowLevelHooks.IsSelfEchoKeyboard(0u, tag));
    }

    [Fact]
    public void IsSelfEchoMouse_RequiresTagMatch()
    {
        nuint tag = LowLevelHooks.SelfExtraInfoTag;
        Assert.True(LowLevelHooks.IsSelfEchoMouse(0x01u, tag));
        Assert.True(LowLevelHooks.IsSelfEchoMouse(0x02u, tag));
        Assert.False(LowLevelHooks.IsSelfEchoMouse(0x01u, 0));
        Assert.False(LowLevelHooks.IsSelfEchoMouse(0u, tag));
    }
}
