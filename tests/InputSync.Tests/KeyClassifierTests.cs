using InputSync.Core.Text;

namespace InputSync.Tests;

/// <summary>PR#7 regression: every key takes exactly one path — text mirror
/// or control forwarding — never both.</summary>
public sealed class KeyClassifierTests
{
    [Theory]
    [InlineData(0x41)]
    [InlineData(0x5A)]
    [InlineData(0x30)]
    [InlineData(0x39)]
    [InlineData(0x20)]
    [InlineData(0x08)]
    [InlineData(0x0D)]
    [InlineData(0x09)]
    [InlineData(0x2E)]
    [InlineData(0xBE)]
    public void TextAffectingKeys_AreNotNeutral(uint vk) =>
        Assert.False(KeyClassifier.IsTextNeutralKey(vk));

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x1B)]
    [InlineData(0x25)]
    [InlineData(0x26)]
    [InlineData(0x27)]
    [InlineData(0x28)]
    [InlineData(0x70)]
    [InlineData(0x7B)]
    [InlineData(0x24)]
    public void ControlKeys_AreNeutral(uint vk) =>
        Assert.True(KeyClassifier.IsTextNeutralKey(vk));
}
