using InputSync.Core.Models;

namespace InputSync.Core.Normalization;

public sealed class EventNormalizer
{
    public NormalizedInputEvent NormalizeKeyboard(
        nint sourceHwnd,
        InputAction action,
        uint virtualKey,
        uint scanCode,
        uint flags,
        long timestamp,
        string text = "") =>
        new()
        {
            SourceHwnd = sourceHwnd,
            Type = InputEventType.Keyboard,
            Action = action.ToString(),
            Vk = virtualKey,
            ScanCode = scanCode,
            Flags = flags,
            Extended = (flags & 0x1u) != 0u,
            Text = text ?? string.Empty,
            Timestamp = timestamp
        };

    public NormalizedInputEvent NormalizeMouse(
        nint sourceHwnd,
        InputAction action,
        int sourceClientX,
        int sourceClientY,
        int sourceWidth,
        int sourceHeight,
        long timestamp,
        MouseButton button = MouseButton.None,
        int wheelDelta = 0)
    {
        double normalizedX = sourceWidth > 0 ? (double)sourceClientX / sourceWidth : 0d;
        double normalizedY = sourceHeight > 0 ? (double)sourceClientY / sourceHeight : 0d;

        return new NormalizedInputEvent
        {
            SourceHwnd = sourceHwnd,
            Type = InputEventType.Mouse,
            Action = action.ToString(),
            X = sourceClientX,
            Y = sourceClientY,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
            Button = button.ToString(),
            WheelDelta = wheelDelta,
            Timestamp = timestamp
        };
    }

    public (int X, int Y) ToTargetCoords(
        NormalizedInputEvent inputEvent,
        int targetWidth,
        int targetHeight,
        CoordinateMode mode) =>
        mode == CoordinateMode.Absolute
            ? (inputEvent.X, inputEvent.Y)
            : ToTargetCoords(inputEvent.NormalizedX, inputEvent.NormalizedY, targetWidth, targetHeight);

    public (int X, int Y) ToTargetCoords(double normalizedX, double normalizedY, int targetWidth, int targetHeight) =>
        ((int)Math.Round(normalizedX * targetWidth), (int)Math.Round(normalizedY * targetHeight));
}
