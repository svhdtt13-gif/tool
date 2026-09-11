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
}
