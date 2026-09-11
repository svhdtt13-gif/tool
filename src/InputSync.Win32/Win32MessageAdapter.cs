using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;

namespace InputSync.Win32;

public sealed record TargetFailure(nint TargetHwnd, int NativeError, string Reason, DateTimeOffset Timestamp);

public sealed class Win32MessageAdapter : ITargetAdapter
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmXButtonUp = 0x020C;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint TimeoutMilliseconds = 25;

    private readonly ConcurrentDictionary<nint, TargetFailure> _failures = new();

    public Win32MessageAdapter(nint targetHwnd = default)
    {
        TargetHwnd = targetHwnd;
    }

    public nint TargetHwnd { get; }

    public IReadOnlyDictionary<nint, TargetFailure> Failures => _failures;

    public bool IsSupported() => OperatingSystem.IsWindows();

    public bool SendKeyboard(KeyboardEventData eventData)
    {
        try
        {
            uint message = eventData.Action == KeyboardAction.Down ? WmKeyDown : WmKeyUp;
            nint lParam = BuildKeyboardLParam(eventData);
            return Post(eventData.TargetHwnd, message, (nuint)eventData.VirtualKey, lParam);
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
            (uint message, nuint wParam) = BuildMouseMessage(eventData);
            return Post(eventData.TargetHwnd, message, wParam, BuildMouseLParam(eventData.X, eventData.Y));
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(eventData.TargetHwnd, exception.HResult, exception.Message);
        }
    }

    public bool SendKeyboardWithTimeout(KeyboardEventData eventData)
    {
        try
        {
            uint message = eventData.Action == KeyboardAction.Down ? WmKeyDown : WmKeyUp;
            return SendWithTimeout(
                eventData.TargetHwnd,
                message,
                (nuint)eventData.VirtualKey,
                BuildKeyboardLParam(eventData));
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(eventData.TargetHwnd, exception.HResult, exception.Message);
        }
    }

    public bool SendMouseWithTimeout(MouseEventData eventData)
    {
        try
        {
            (uint message, nuint wParam) = BuildMouseMessage(eventData);
            return SendWithTimeout(
                eventData.TargetHwnd,
                message,
                wParam,
                BuildMouseLParam(eventData.X, eventData.Y));
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(eventData.TargetHwnd, exception.HResult, exception.Message);
        }
    }

    public static nint BuildKeyboardLParam(KeyboardEventData eventData)
    {
        uint value = 1u | ((eventData.ScanCode & 0xFFu) << 16);
        if (eventData.IsExtended)
        {
            value |= 1u << 24;
        }

        if (eventData.Action == KeyboardAction.Up)
        {
            value |= (1u << 30) | (1u << 31);
        }

        return unchecked((nint)(int)value);
    }

    public static nint BuildMouseLParam(int x, int y)
    {
        uint packed = (ushort)x | ((uint)(ushort)y << 16);
        return unchecked((nint)(int)packed);
    }

    private bool Post(nint target, uint message, nuint wParam, nint lParam)
    {
        if (!ValidateTarget(target))
        {
            return false;
        }

        try
        {
            if (PostMessage(target, message, wParam, lParam))
            {
                _failures.TryRemove(target, out _);
                return true;
            }

            return RecordNativeFailure(target, "PostMessage failed");
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(target, exception.HResult, exception.Message);
        }
    }

    private bool SendWithTimeout(nint target, uint message, nuint wParam, nint lParam)
    {
        if (!ValidateTarget(target))
        {
            return false;
        }

        try
        {
            nint result = SendMessageTimeout(
                target,
                message,
                wParam,
                lParam,
                SmtoAbortIfHung,
                TimeoutMilliseconds,
                out _);
            if (result != 0)
            {
                _failures.TryRemove(target, out _);
                return true;
            }

            return RecordNativeFailure(target, "SendMessageTimeout failed or timed out");
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            return RecordFailure(target, exception.HResult, exception.Message);
        }
    }

    private bool ValidateTarget(nint target)
    {
        if (!IsSupported())
        {
            return RecordFailure(target, 0, "Win32 messaging is unavailable on this platform");
        }

        if (target == 0 || !IsWindow(target))
        {
            return RecordNativeFailure(target, "Target window is no longer valid");
        }

        return true;
    }

    private bool RecordNativeFailure(nint target, string reason)
    {
        int error = Marshal.GetLastWin32Error();
        string detail = error == 0 ? reason : $"{reason}: {new Win32Exception(error).Message}";
        return RecordFailure(target, error, detail);
    }

    private bool RecordFailure(nint target, int nativeError, string reason)
    {
        _failures[target] = new TargetFailure(target, nativeError, reason, DateTimeOffset.UtcNow);
        return false;
    }

    private static (uint Message, nuint WParam) BuildMouseMessage(MouseEventData eventData)
    {
        uint lowWord = (uint)eventData.PressedButtons & 0xFFFFu;
        return eventData.Action switch
        {
            MouseAction.Move => (WmMouseMove, lowWord),
            MouseAction.Wheel => (WmMouseWheel, lowWord | ((uint)(ushort)eventData.WheelDelta << 16)),
            MouseAction.ButtonDown => eventData.Button switch
            {
                MouseButton.Left => (WmLButtonDown, lowWord | (uint)MouseButtonMask.Left),
                MouseButton.Right => (WmRButtonDown, lowWord | (uint)MouseButtonMask.Right),
                MouseButton.Middle => (WmMButtonDown, lowWord | (uint)MouseButtonMask.Middle),
                MouseButton.X1 => (WmXButtonDown, lowWord | (1u << 16)),
                MouseButton.X2 => (WmXButtonDown, lowWord | (2u << 16)),
                _ => throw new ArgumentOutOfRangeException(nameof(eventData), "ButtonDown requires a mouse button"),
            },
            MouseAction.ButtonUp => eventData.Button switch
            {
                MouseButton.Left => (WmLButtonUp, lowWord & ~(uint)MouseButtonMask.Left),
                MouseButton.Right => (WmRButtonUp, lowWord & ~(uint)MouseButtonMask.Right),
                MouseButton.Middle => (WmMButtonUp, lowWord & ~(uint)MouseButtonMask.Middle),
                MouseButton.X1 => (WmXButtonUp, lowWord | (1u << 16)),
                MouseButton.X2 => (WmXButtonUp, lowWord | (2u << 16)),
                _ => throw new ArgumentOutOfRangeException(nameof(eventData), "ButtonUp requires a mouse button"),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(eventData)),
        };
    }

    [DllImport("user32.dll", EntryPoint = "IsWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern nint SendMessageTimeout(
        nint hWnd,
        uint msg,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nuint result);
}
