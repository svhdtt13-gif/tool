using System.Runtime.InteropServices;
using InputSync.Core.Capture;

namespace InputSync.Win32;

/// <summary>
/// Resolves printable text for key-down events with ToUnicode and the live
/// keyboard state (Shift/Ctrl/Alt patched from the global async state, plus
/// CapsLock). Called on the capture thread while the physical keys are held.
/// </summary>
public sealed class KeyboardLayoutTranslator : IKeyboardLayoutTranslator
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_CAPITAL = 0x14;

    public string Translate(uint virtualKey, uint scanCode, uint flags)
    {
        try
        {
            byte[] state = new byte[256];
            if (!GetKeyboardState(state))
            {
                return string.Empty;
            }

            PatchModifier(state, VK_SHIFT, 0xA0, 0xA1);
            PatchModifier(state, VK_CONTROL, 0xA2, 0xA3);
            PatchModifier(state, VK_MENU, 0xA4, 0xA5);
            if ((GetKeyState(VK_CAPITAL) & 0x1) != 0)
            {
                state[VK_CAPITAL] = 1;
            }

            char[] buffer = new char[4];
            int length = ToUnicode(virtualKey, scanCode, state, buffer, buffer.Length, 0);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void PatchModifier(byte[] state, int general, int left, int right)
    {
        if ((GetAsyncKeyState(general) & 0x8000) != 0)
        {
            state[general] = 0x80;
            state[left] = 0x80;
            state[right] = 0x80;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] keyState);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicode(
        uint virtualKey,
        uint scanCode,
        byte[] keyState,
        [Out] char[] buffer,
        int bufferSize,
        uint flags);
}
