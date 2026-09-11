using InputSync.Win32;

namespace InputSync.Tests;

/// <summary>Windows-only interop helpers. Skipped on other OSes.</summary>
public sealed class Win32HelperTests
{
    [Fact]
    public void Translator_ResolvesLetterKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var translator = new KeyboardLayoutTranslator();

        Assert.Equal("a", translator.Translate(0x41, 0x1E, 0));
        Assert.Equal(string.Empty, translator.Translate(0x10, 0x2A, 0));
    }

    [Fact]
    public void Translator_ControlKeys_ProduceNoText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var translator = new KeyboardLayoutTranslator();

        foreach (uint vk in new uint[] { 0x10, 0x11, 0x12, 0x25, 0x26, 0x27, 0x28, 0x70, 0x14 })
        {
            Assert.Equal(string.Empty, translator.Translate(vk, 0, 0));
        }
    }

    [Fact]
    public void Translator_NeverThrows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var translator = new KeyboardLayoutTranslator();

        foreach (uint vk in new uint[] { 0, 1, 0xFF, 0xE0 })
        {
            Assert.NotNull(translator.Translate(vk, 0, 0));
        }
    }

    [Fact]
    public void EndpointResolver_FallsBackToTopLevel()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var resolver = new TargetEndpointResolver();
        var bogus = new nint(0x0BADF00D);

        Assert.Equal(bogus, resolver.Resolve(bogus));
        Assert.Equal(bogus, resolver.Resolve(bogus));
        Assert.Equal(nint.Zero, resolver.Resolve(nint.Zero));
    }
}
