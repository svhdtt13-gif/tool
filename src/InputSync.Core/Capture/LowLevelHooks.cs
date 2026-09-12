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
    private readonly ManualResetEventSlim _pumpReady = new(false);
    private nint _keyboardHook;
    private nint _mouseHook;
    private Thread? _pumpThread;
    private uint _pumpThreadId;
    private Exception? _pumpError;
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
            if (_pumpThread is not null)
            {
                return;
            }

            _pumpReady.Reset();
            _pumpError = null;
            var thread = new Thread(PumpThreadMain)
            {
                IsBackground = true,
                Name = "InputSync hook pump",
            };
            thread.Start();
            _pumpThread = thread;
        }

        if (!_pumpReady.Wait(TimeSpan.FromSeconds(10)))
        {
            Uninstall();
            throw new TimeoutException("Global hooks did not install within 10 seconds.");
        }

        Exception? error;
        lock (_lifecycleGate)
        {
            error = _pumpError;
        }

        if (error is not null)
        {
            Uninstall();
            throw new Win32Exception("Global hook installation failed.", error);
        }
    }

    public void Uninstall()
    {
        Thread? pump;
        lock (_lifecycleGate)
        {
            pump = Interlocked.Exchange(ref _pumpThread, null);
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

            try
            {
                if (_pumpThreadId != 0)
                {
                    PostThreadMessage(_pumpThreadId, WM_QUIT, nuint.Zero, nint.Zero);
                }
            }
            catch
            {
            }

            _pumpThreadId = 0;
        }

        try
        {
            pump?.Join(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
    }

    private void PumpThreadMain()
    {
        try
        {
            _pumpThreadId = GetCurrentThreadId();
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
                nint installed = Interlocked.Exchange(ref _keyboardHook, nint.Zero);
                if (installed != nint.Zero)
                {
                    _ = UnhookWindowsHookEx(installed);
                }

                throw new Win32Exception(error);
            }

            Interlocked.Exchange(ref _mouseHook, mouse);
        }
        catch (Exception exception)
        {
            lock (_lifecycleGate)
            {
                _pumpError = exception;
            }

            _pumpReady.Set();
            return;
        }

        _pumpReady.Set();

        try
        {
            while (GetMessage(out MSG message, nint.Zero, 0, 0))
            {
                _ = message;
            }
        }
        catch
        {
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
        try
        {
            _pumpReady.Dispose();
        }
        catch
        {
        }

        GC.SuppressFinalize(this);
    }

    private nint KeyboardCallback(int nCode, nuint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (!IsInjectedKeyboard(data.Flags))
                {
                    _writer.TryWrite(RawHookEvent.Keyboard((uint)wParam, data));
                }
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
                if (!IsInjectedMouse(data.Flags))
                {
                    _writer.TryWrite(RawHookEvent.Mouse((uint)wParam, data));
                }
            }
            catch (Exception)
            {
            }
        }

        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    public static bool IsInjectedKeyboard(uint flags) => (flags & LLKHF_INJECTED) != 0;

    public static bool IsInjectedMouse(uint flags) => (flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;

    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;
    private const uint LLMHF_LOWER_IL_INJECTED = 0x02;

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

    private const uint WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMessage(out MSG message, nint hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

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
