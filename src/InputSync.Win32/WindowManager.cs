using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using InputSync.Core.Models;

namespace InputSync.Win32;

/// <summary>Provides discovery and inspection operations for top-level Windows windows.</summary>
public static class WindowManager
{
    /// <summary>Enumerates visible top-level windows that have a non-empty title.</summary>
    /// <returns>A snapshot of matching windows identified by their native runtime handles.</returns>
    public static IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                WindowInfo? window = GetWindowInfo(hwnd);
                if (window is not null && !string.IsNullOrWhiteSpace(window.Title))
                {
                    windows.Add(window);
                }

                return true;
            },
            0);

        return windows;
    }

    /// <summary>Gets current metadata for a native window.</summary>
    /// <param name="hwnd">The native window handle, which is used as runtime identity.</param>
    /// <returns>The current window metadata, or <see langword="null"/> when the handle is invalid.</returns>
    public static WindowInfo? GetWindowInfo(nint hwnd)
    {
        if (!IsWindowValid(hwnd))
        {
            return null;
        }

        var title = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, title, title.Capacity);

        var className = new StringBuilder(512);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
        (int width, int height) = GetClientSize(hwnd);

        return new WindowInfo(
            hwnd,
            title.ToString(),
            processId,
            GetProcessName(processId),
            className.ToString(),
            NativeMethods.IsWindowVisible(hwnd),
            width,
            height);
    }

    /// <summary>Determines whether a native window handle currently identifies a window.</summary>
    /// <param name="hwnd">The native window handle to test.</param>
    /// <returns><see langword="true"/> when the handle identifies an existing window.</returns>
    public static bool IsWindowValid(nint hwnd) => hwnd != 0 && NativeMethods.IsWindow(hwnd);

    /// <summary>Gets the current size of a window's client area.</summary>
    /// <param name="hwnd">The native window handle.</param>
    /// <returns>The client width and height in pixels, or zeroes when unavailable.</returns>
    public static (int Width, int Height) GetClientSize(nint hwnd)
    {
        if (!IsWindowValid(hwnd) || !NativeMethods.GetClientRect(hwnd, out NativeMethods.Rect rect))
        {
            return (0, 0);
        }

        return (Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }

    private static string GetProcessName(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return string.Empty;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (Win32Exception)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
    }
}
