using InputSync.Core.Models;

namespace InputSync.Core.Dispatch;

public interface ITargetAdapter
{
    nint TargetHwnd => nint.Zero;

    bool SendKeyboard(KeyboardEventData eventData);

    bool SendMouse(MouseEventData eventData);

    bool IsSupported();

    void SendKeyboard(NormalizedInputEvent inputEvent) =>
        SendKeyboard(new KeyboardEventData(
            TargetHwnd,
            inputEvent.Vk,
            inputEvent.ScanCode,
            IsUp(inputEvent.Action) ? KeyboardAction.Up : KeyboardAction.Down,
            inputEvent.Extended));

    void SendMouse(NormalizedInputEvent inputEvent)
    {
        MouseButton button = ParseButton(inputEvent.Button);
        MouseAction action = ParseMouseAction(inputEvent.Action);
        _ = SendMouse(new MouseEventData(
            TargetHwnd,
            action,
            inputEvent.X,
            inputEvent.Y,
            button,
            MouseButtonMask.None,
            inputEvent.WheelDelta));
    }

    private static bool IsUp(string action) =>
        action.EndsWith("up", StringComparison.OrdinalIgnoreCase);

    private static MouseAction ParseMouseAction(string action)
    {
        if (action.Contains("wheel", StringComparison.OrdinalIgnoreCase))
        {
            return MouseAction.Wheel;
        }

        if (action.EndsWith("down", StringComparison.OrdinalIgnoreCase))
        {
            return MouseAction.ButtonDown;
        }

        if (action.EndsWith("up", StringComparison.OrdinalIgnoreCase))
        {
            return MouseAction.ButtonUp;
        }

        return MouseAction.Move;
    }

    private static MouseButton ParseButton(string button) => button.ToLowerInvariant() switch
    {
        "left" => MouseButton.Left,
        "right" => MouseButton.Right,
        "middle" => MouseButton.Middle,
        "x1" => MouseButton.X1,
        "x2" => MouseButton.X2,
        _ => MouseButton.None,
    };
}
