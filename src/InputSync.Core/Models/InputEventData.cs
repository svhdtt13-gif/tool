namespace InputSync.Core.Models;

public enum KeyboardAction
{
    Down,
    Up,
}

public enum MouseAction
{
    Move,
    ButtonDown,
    ButtonUp,
    Wheel,
}

public enum MouseButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

[Flags]
public enum MouseButtonMask : uint
{
    None = 0,
    Left = 0x0001,
    Right = 0x0002,
    Shift = 0x0004,
    Control = 0x0008,
    Middle = 0x0010,
    X1 = 0x0020,
    X2 = 0x0040,
}

public readonly record struct KeyboardEventData(
    nint TargetHwnd,
    uint VirtualKey,
    uint ScanCode,
    KeyboardAction Action,
    bool IsExtended = false);

public readonly record struct MouseEventData(
    nint TargetHwnd,
    MouseAction Action,
    int X,
    int Y,
    MouseButton Button = MouseButton.None,
    MouseButtonMask PressedButtons = MouseButtonMask.None,
    int WheelDelta = 0);
