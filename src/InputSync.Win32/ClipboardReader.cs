using System.Runtime.InteropServices;

namespace InputSync.Win32;

/// <summary>
/// Reads Unicode text from the system clipboard with plain user32 calls
/// (no STA requirement). Used at Ctrl+V time so targets receive exactly
/// what the source intended, regardless of each target's own clipboard.
/// </summary>
public static class ClipboardReader
{
    private const uint CF_UNICODETEXT = 13;

    public static bool TryReadUnicodeText(out string? text)
    {
        text = null;
        try
        {
            if (!OpenClipboard(nint.Zero))
            {
                return false;
            }

            try
            {
                if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
                {
                    return false;
                }

                nint handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == nint.Zero)
                {
                    return false;
                }

                nint pointer = GlobalLock(handle);
                if (pointer == nint.Zero)
                {
                    return false;
                }

                try
                {
                    text = Marshal.PtrToStringUni(pointer);
                }
                finally
                {
                    GlobalUnlock(handle);
                }

                return !string.IsNullOrEmpty(text);
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            text = null;
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern nint GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint handle);
}
