namespace InputSync.Core.Models;

/// <summary>Describes whether an input synchronization target can receive events.</summary>
public enum TargetStatus
{
    /// <summary>The target is active and available.</summary>
    ACTIVE,

    /// <summary>The target has been disabled.</summary>
    DISABLED,

    /// <summary>The target window is no longer valid.</summary>
    WINDOW_LOST,

    /// <summary>The synchronization source is no longer valid.</summary>
    SOURCE_LOST,
}

/// <summary>Tracks the runtime status of a target window by its native handle.</summary>
public sealed class TargetState
{
    /// <summary>Initializes state for a target window.</summary>
    /// <param name="hwnd">The target's native window handle.</param>
    /// <param name="status">The initial target status.</param>
    public TargetState(nint hwnd, TargetStatus status = TargetStatus.ACTIVE)
    {
        Hwnd = hwnd;
        Status = status;
    }

    /// <summary>Gets the target's native window handle and runtime identity.</summary>
    public nint Hwnd { get; }

    /// <summary>Gets or sets the target's current availability status.</summary>
    public TargetStatus Status { get; set; }
}
