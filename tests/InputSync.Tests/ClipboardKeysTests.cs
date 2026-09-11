using InputSync.Core.Text;

namespace InputSync.Tests;

/// <summary>PR#9 regression: only V/X/Z/Y take the clipboard-mirror path;
/// every other Ctrl combination forwards as control keys.</summary>
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
    public void IsPasteKey_OnlyV()
    {
        Assert.True(ClipboardKeys.IsPasteKey(0x56));
        Assert.False(ClipboardKeys.IsPasteKey(0x58));
    }
}
