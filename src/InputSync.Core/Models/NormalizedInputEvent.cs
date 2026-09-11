using System.Diagnostics;

namespace InputSync.Core.Models;

/// <summary>Identifies the device category that produced an input event.</summary>
public enum InputEventType
{
    /// <summary>The event represents keyboard input.</summary>
    Keyboard,

    /// <summary>The event represents mouse input.</summary>
    Mouse,
}

public enum InputAction
{
    KeyDown,
    KeyUp,
    MouseMove,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    XButtonDown,
    XButtonUp,
    Wheel,
}

/// <summary>
/// Represents a platform-neutral input event with keyboard and mouse payload fields.
/// Fields that do not apply to the selected <see cref="Type"/> retain their defaults.
/// </summary>
public sealed record NormalizedInputEvent
{
    /// <summary>Gets the unique event identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Gets the event timestamp in <see cref="Stopwatch"/> ticks.</summary>
    public long Timestamp { get; init; } = Stopwatch.GetTimestamp();

    public long QueueTimestamp { get; init; }

    /// <summary>Gets the native handle of the source window.</summary>
    public nint SourceHwnd { get; init; }

    /// <summary>Gets the input device category.</summary>
    public InputEventType Type { get; init; }

    /// <summary>Gets the normalized action name.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets the keyboard virtual-key code.</summary>
    public uint Vk { get; init; }

    /// <summary>Gets the keyboard hardware scan code.</summary>
    public uint ScanCode { get; init; }

    /// <summary>Gets the platform input flags.</summary>
    public uint Flags { get; init; }

    /// <summary>Gets whether the keyboard event uses an extended key.</summary>
    public bool Extended { get; init; }

    /// <summary>Gets the mouse X coordinate.</summary>
    public int X { get; init; }

    /// <summary>Gets the mouse Y coordinate.</summary>
    public int Y { get; init; }

    /// <summary>Gets the raw screen coordinates before client mapping, for trace contrast.</summary>
    public int RawX { get; init; }

    /// <summary>Gets the raw screen coordinates before client mapping, for trace contrast.</summary>
    public int RawY { get; init; }

    /// <summary>Gets the horizontal coordinate normalized to the source client area.</summary>
    public double NormalizedX { get; init; }

    /// <summary>Gets the vertical coordinate normalized to the source client area.</summary>
    public double NormalizedY { get; init; }

    /// <summary>Gets the mouse button name, or an empty string when not applicable.</summary>
    public string Button { get; init; } = string.Empty;

    /// <summary>Gets the signed mouse wheel delta.</summary>
    public int WheelDelta { get; init; }

    /// <summary>
    /// Gets the printable text produced by a key-down event (resolved with the
    /// active keyboard layout at capture time). Empty for non-text keys.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets the coordinate-space identifier for the mouse coordinates.</summary>
    public string CoordinateSpace { get; init; } = string.Empty;

    public bool IsMouseMove => Type == InputEventType.Mouse &&
        string.Equals(Action, nameof(InputAction.MouseMove), StringComparison.OrdinalIgnoreCase);
}
