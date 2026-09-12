using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;

namespace InputSync.Win32;

public sealed class ForegroundSendInputAdapter : ITargetAdapter
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventXDown = 0x0080;
    private const uint MouseEventXUp = 0x0100;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventAbsolute = 0x8000;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    private readonly ConcurrentDictionary<nint, TargetFailure> _failures = new();
    private long _sendFailures;

    public ForegroundSendInputAdapter(nint targetHwnd = default)
    {
        TargetHwnd = targetHwnd;
    }

    public nint TargetHwnd { get; }

    public long SendFailures => Interlocked.Read(ref _sendFailures);

    public IReadOnlyDictionary<nint, TargetFailure> Failures => _failures;

    public bool IsSupported() => OperatingSystem.IsWindows();

    public bool SendKeyboard(KeyboardEventData eventData)
    {
        try
        {
            if (!ValidateForegroundTarget(eventData.TargetHwnd))
            {
                return false;
            }

            Input input = new()
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = (ushort)eventData.VirtualKey,
                        ScanCode = (ushort)eventData.ScanCode,
                        Flags = (eventData.IsExtended ? KeyEventExtendedKey : 0) |
                            (eventData.Action == KeyboardAction.Up ? KeyEventKeyUp : 0),
                    },
                },
            };
            return Send(eventData.TargetHwnd, input);
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(eventData.TargetHwnd, exception.HResult, exception.Message);
        }
    }

    public bool SendMouse(MouseEventData eventData)
    {
        try
        {
            if (!ValidateForegroundTarget(eventData.TargetHwnd))
            {
                return false;
            }

            MouseInput mouse = BuildMouseInput(eventData);
            Input input = new()
            {
                Type = InputMouse,
                Data = new InputUnion { Mouse = mouse },
            };
            return Send(eventData.TargetHwnd, input);
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(eventData.TargetHwnd, exception.HResult, exception.Message);
        }
    }

    private bool ValidateForegroundTarget(nint target)
    {
        if (!IsSupported())
        {
            return RecordFailure(target, 0, "SendInput is unavailable on this platform");
        }

        if (target == 0 || !IsWindow(target))
        {
            return RecordNativeFailure(target, "Target window is no longer valid");
        }

        if (GetForegroundWindow() != target)
        {
            return RecordFailure(target, 0, "SendInput is foreground-only; background broadcast is rejected");
        }

        return true;
    }

    private bool Send(nint target, Input input)
    {
        try
        {
            Input[] inputs = [input];
            if (SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1)
            {
                _failures.TryRemove(target, out _);
                return true;
            }

            return RecordNativeFailure(target, "SendInput failed");
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(target, exception.HResult, exception.Message);
        }
    }

    private static MouseInput BuildMouseInput(MouseEventData eventData)
    {
        if (eventData.Action == MouseAction.Move)
        {
            Point point = new() { X = eventData.X, Y = eventData.Y };
            if (!ClientToScreen(eventData.TargetHwnd, ref point))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            int width = Math.Max(1, GetSystemMetrics(SmCxScreen) - 1);
            int height = Math.Max(1, GetSystemMetrics(SmCyScreen) - 1);
            return new MouseInput
            {
                X = (int)Math.Clamp((long)point.X * 65535 / width, 0, 65535),
                Y = (int)Math.Clamp((long)point.Y * 65535 / height, 0, 65535),
                Flags = MouseEventMove | MouseEventAbsolute,
            };
        }

        return eventData.Action switch
        {
            MouseAction.Wheel => new MouseInput { MouseData = unchecked((uint)eventData.WheelDelta), Flags = MouseEventWheel },
            MouseAction.ButtonDown => BuildButtonInput(eventData.Button, true),
            MouseAction.ButtonUp => BuildButtonInput(eventData.Button, false),
            _ => throw new ArgumentOutOfRangeException(nameof(eventData)),
        };
    }

    private static MouseInput BuildButtonInput(MouseButton button, bool down) => button switch
    {
        MouseButton.Left => new MouseInput { Flags = down ? MouseEventLeftDown : MouseEventLeftUp },
        MouseButton.Right => new MouseInput { Flags = down ? MouseEventRightDown : MouseEventRightUp },
        MouseButton.Middle => new MouseInput { Flags = down ? MouseEventMiddleDown : MouseEventMiddleUp },
        MouseButton.X1 => new MouseInput { MouseData = 1, Flags = down ? MouseEventXDown : MouseEventXUp },
        MouseButton.X2 => new MouseInput { MouseData = 2, Flags = down ? MouseEventXDown : MouseEventXUp },
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    private bool RecordNativeFailure(nint target, string reason)
    {
        int error = Marshal.GetLastWin32Error();
        string detail = error == 0 ? reason : $"{reason}: {new Win32Exception(error).Message}";
        return RecordFailure(target, error, detail);
    }

    private bool RecordFailure(nint target, int nativeError, string reason)
    {
        _failures[target] = new TargetFailure(target, nativeError, reason, DateTimeOffset.UtcNow);
        Interlocked.Increment(ref _sendFailures);
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", EntryPoint = "IsWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint hWnd, ref Point point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
