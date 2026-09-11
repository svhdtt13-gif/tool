using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace InputSync.Core.Capture;

public sealed class LowLevelHooks : IDisposable
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    private readonly object _lifecycleGate = new();
    private readonly ChannelWriter<RawHookEvent> _writer;
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _disposed;

    public LowLevelHooks(ChannelWriter<RawHookEvent> writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public void Install()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleGate)
        {
            if (_keyboardHook != nint.Zero && _mouseHook != nint.Zero)
            {
                return;
            }

            nint module = GetModuleHandle(null);
            nint keyboard = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (keyboard == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Interlocked.Exchange(ref _keyboardHook, keyboard);

            nint mouse = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
            if (mouse == nint.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Uninstall();
                throw new Win32Exception(error);
            }

            Interlocked.Exchange(ref _mouseHook, mouse);
        }
    }

    public void Uninstall()
    {
        nint keyboard = Interlocked.Exchange(ref _keyboardHook, nint.Zero);
        if (keyboard != nint.Zero)
        {
            _ = UnhookWindowsHookEx(keyboard);
        }

        nint mouse = Interlocked.Exchange(ref _mouseHook, nint.Zero);
        if (mouse != nint.Zero)
        {
            _ = UnhookWindowsHookEx(mouse);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Uninstall();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint KeyboardCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                _writer.TryWrite(RawHookEvent.Keyboard((uint)wParam, data));
            }
            catch (Exception)
            {
            }
        }

        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private nint MouseCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                MSLLHOOKSTRUCT data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _writer.TryWrite(RawHookEvent.Mouse((uint)wParam, data));
            }
            catch (Exception)
            {
            }
        }

        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private delegate nint HookProc(int nCode, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(
        int idHook,
        HookProc callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}

public readonly record struct RawHookEvent(
    RawHookEventKind Kind,
    uint Message,
    LowLevelHooks.KBDLLHOOKSTRUCT KeyboardData,
    LowLevelHooks.MSLLHOOKSTRUCT MouseData)
{
    public static RawHookEvent Keyboard(uint message, LowLevelHooks.KBDLLHOOKSTRUCT data) =>
        new(RawHookEventKind.Keyboard, message, data, default);

    public static RawHookEvent Mouse(uint message, LowLevelHooks.MSLLHOOKSTRUCT data) =>
        new(RawHookEventKind.Mouse, message, default, data);
}

public enum RawHookEventKind
{
    Keyboard,
    Mouse
}
