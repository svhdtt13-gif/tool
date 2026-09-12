using System.Runtime.InteropServices;
using System.Text;

namespace InputSync.Win32;

/// <summary>
/// Maps a top-level target window to the child control that actually consumes
/// input (first visible, enabled child whose class contains "edit",
/// e.g. Notepad's edit control). Falls back to the top-level window itself
/// for games and apps that consume messages directly. Results are cached and
/// re-validated on every resolve.
/// </summary>
public sealed class TargetEndpointResolver
{
    private readonly object _gate = new();
    private readonly Dictionary<nint, nint> _cache = new();

    private delegate bool EnumChildProc(nint hwnd, nint param);

    private const int ChildSkipInvisible = 0x0001;
    private const int ChildSkipDisabled = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public nint Resolve(nint topLevel)
    {
        if (topLevel == nint.Zero || !IsWindow(topLevel))
        {
            return topLevel;
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(topLevel, out nint cached) && IsWindow(cached))
            {
                return cached;
            }

            nint endpoint = FindEditChild(topLevel);
            _cache[topLevel] = endpoint;
            return endpoint;
        }
    }

    public (nint Child, int X, int Y) ResolveAtPoint(nint topLevel, int x, int y)
    {
        if (topLevel == nint.Zero || !IsWindow(topLevel))
        {
            return (topLevel, x, y);
        }

        try
        {
            POINT point = new() { X = x, Y = y };
            nint child = ChildWindowFromPointEx(topLevel, point, ChildSkipInvisible | ChildSkipDisabled);
            if (child == nint.Zero || child == topLevel || !IsWindow(child))
            {
                return (topLevel, x, y);
            }

            POINT mapped = new() { X = x, Y = y };
            if (MapWindowPoints(topLevel, child, ref mapped, 1) == 0)
            {
                return (topLevel, x, y);
            }

            return (child, mapped.X, mapped.Y);
        }
        catch
        {
            return (topLevel, x, y);
        }
    }

    private static nint FindEditChild(nint topLevel)
    {
        nint found = nint.Zero;
        EnumChildProc callback = (hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && IsWindowEnabled(hwnd) && IsEditClass(hwnd))
            {
                found = hwnd;
                return false;
            }

            return true;
        };

        try
        {
            EnumChildWindows(topLevel, callback, nint.Zero);
        }
        catch
        {
        }
        finally
        {
            GC.KeepAlive(callback);
        }

        return found != nint.Zero ? found : topLevel;
    }

    private static bool IsEditClass(nint hwnd)
    {
        var className = new StringBuilder(256);
        try
        {
            if (GetClassName(hwnd, className, className.Capacity) == 0)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return className.ToString().Contains("edit", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumChildProc callback, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern nint ChildWindowFromPointEx(nint parent, POINT point, int flags);

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(nint from, nint to, ref POINT point, uint count);
}
