using InputSync.Core.Text;

namespace InputSync.Tests;

/// <summary>Only V/X/Z/Y take the clipboard-mirror path; C snapshots the
/// clipboard without forwarding; every other Ctrl combination forwards.</summary>
public sealed class ClipboardKeysTests
{
    [Theory]
    [InlineData(0x56, true)]
    [InlineData(0x58, true)]
    [InlineData(0x5A, true)]
    [InlineData(0x59, true)]
    [InlineData(0x41, false)]
    [InlineData(0x43, false)]
    [InlineData(0x42, false)]
    [InlineData(0x53, false)]
    [InlineData(0x70, false)]
    [InlineData(0x0D, false)]
    public void IsMirroredCombo_OnlyVXZY(uint vk, bool expected) =>
        Assert.Equal(expected, ClipboardKeys.IsMirroredCombo(vk));

    [Fact]
    public void CopyAndPasteKeys_Classified()
    {
        Assert.True(ClipboardKeys.IsCopyKey(0x43));
        Assert.False(ClipboardKeys.IsCopyKey(0x56));
        Assert.True(ClipboardKeys.IsPasteKey(0x56));
        Assert.False(ClipboardKeys.IsPasteKey(0x43));
    }
}
