namespace InputSync.Core.Models;

/// <summary>
/// Describes a top-level native window. The handle is its runtime identity;
/// the title is display metadata and must not be used as a key.
/// </summary>
public sealed record WindowInfo
{
    /// <summary>Initializes a native window description.</summary>
    /// <param name="hwnd">The native window handle.</param>
    /// <param name="title">The current window title.</param>
    /// <param name="processId">The owning process identifier.</param>
    /// <param name="processName">The owning process name, or an empty string when unavailable.</param>
    /// <param name="className">The native window class name, or an empty string when unavailable.</param>
    /// <param name="visible">Whether the window is visible.</param>
    /// <param name="clientWidth">The client-area width in pixels.</param>
    /// <param name="clientHeight">The client-area height in pixels.</param>
    public WindowInfo(
        nint hwnd,
        string title,
        uint processId,
        string processName,
        string className,
        bool visible,
        int clientWidth,
        int clientHeight)
    {
        Hwnd = hwnd;
        Title = title;
        ProcessId = processId;
        ProcessName = processName;
        ClassName = className;
        Visible = visible;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
    }

    /// <summary>Gets the native window handle.</summary>
    public nint Hwnd { get; }

    /// <summary>Gets the current window title.</summary>
    public string Title { get; }

    /// <summary>Gets the identifier of the process that owns the window.</summary>
    public uint ProcessId { get; }

    /// <summary>Gets the owning process name, or an empty string when unavailable.</summary>
    public string ProcessName { get; }

    /// <summary>Gets the native window class name, or an empty string when unavailable.</summary>
    public string ClassName { get; }

    /// <summary>Gets a value indicating whether the window is visible.</summary>
    public bool Visible { get; }

    /// <summary>Gets the client-area width in pixels.</summary>
    public int ClientWidth { get; }

    /// <summary>Gets the client-area height in pixels.</summary>
    public int ClientHeight { get; }
}
