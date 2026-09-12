using System.Runtime.InteropServices;

namespace InputSync.Win32;

/// <summary>
/// Focuses a window and verifies the foreground actually moved there.
/// Never assumes the API return means success: the foreground is read
/// back after a short settle on every attempt.
/// </summary>
public static class FocusHandoff
{
    public static (bool Ok, string Detail) EnsureForeground(
        nint hwnd, int attempts = 3, int settleMs = 80)
    {
        nint before;
        try
        {
            before = GetForegroundWindow();
        }
        catch
        {
            return (false, "could not read foreground window");
        }

        if (hwnd == nint.Zero)
        {
            return (false, "empty target handle");
        }

        if (before == hwnd)
        {
            return (true, "already foreground");
        }

        nint after = before;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                uint current = GetCurrentThreadId();
                GetWindowThreadProcessId(hwnd, out uint target);
                AttachThreadInput(current, target, true);
                try
                {
                    SetForegroundWindow(hwnd);
                }
                finally
                {
                    AttachThreadInput(current, target, false);
                }
            }
            catch (Exception exception) when (exception is not StackOverflowException)
            {
                if (attempt == attempts)
                {
                    return (false, $"handoff exception: {exception.GetType().Name}");
                }

                Thread.Sleep(settleMs);
                continue;
            }

            Thread.Sleep(settleMs);
            try
            {
                after = GetForegroundWindow();
            }
            catch
            {
                return (false, "could not verify foreground window");
            }

            if (after == hwnd)
            {
                return (true, $"focused on attempt {attempt}");
            }
        }

        return (false, $"before=0x{before:X} after=0x{after:X} attempts={attempts}");
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint fromThread, uint toThread, bool attach);
}
