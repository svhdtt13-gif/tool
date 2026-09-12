using System.Runtime.InteropServices;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;

namespace InputSync.Win32;

/// <summary>
/// Fans one source event out to N game clients by focusing each valid
/// target in turn and injecting real input through the inner adapter
/// (normally <see cref="ForegroundSendInputAdapter"/>). A target that
/// cannot be focused is skipped for that event without touching the
/// others. All decisions are injectable for unit tests.
/// </summary>
public sealed class RotatingSendInputAdapter : ITargetAdapter
{
    private readonly Func<nint, (bool Ok, string Detail)> _ensureForeground;
    private readonly ITargetAdapter _inner;
    private readonly Func<MouseEventData, nint, MouseEventData> _translateMouse;
    private readonly Action<string>? _trace;
    private long _focusAttempts;
    private long _focusFailures;
    private long _sends;
    private long _sendFailures;

    public RotatingSendInputAdapter(
        Func<nint, (bool Ok, string Detail)> ensureForeground,
        ITargetAdapter inner,
        Func<nint> getSource,
        Func<CoordinateMode> getMode,
        Func<MouseEventData, nint, MouseEventData>? translateMouse = null,
        Action<string>? trace = null)
    {
        _ensureForeground = ensureForeground ?? throw new ArgumentNullException(nameof(ensureForeground));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(getSource);
        ArgumentNullException.ThrowIfNull(getMode);
        _translateMouse = translateMouse ?? ((data, target) => TranslateRelative(getSource(), target, getMode(), data));
        _trace = trace;
    }

    public long FocusAttempts => Interlocked.Read(ref _focusAttempts);

    public long FocusFailures => Interlocked.Read(ref _focusFailures);

    public long Sends => Interlocked.Read(ref _sends);

    public long SendFailures => Interlocked.Read(ref _sendFailures);

    public bool IsSupported() => _inner.IsSupported();

    public bool SendKeyboard(KeyboardEventData eventData) =>
        SendToTarget(
            eventData.TargetHwnd,
            eventData.CorrelationId,
            $"kbd {eventData.Action} vk={eventData.VirtualKey}",
            () => _inner.SendKeyboard(eventData));

    public bool SendMouse(MouseEventData eventData)
    {
        MouseEventData translated;
        try
        {
            translated = _translateMouse(eventData, eventData.TargetHwnd);
        }
        catch
        {
            return false;
        }

        return SendToTarget(
            translated.TargetHwnd,
            eventData.CorrelationId,
            $"mouse {translated.Action}/{translated.Button} ({translated.X},{translated.Y})",
            () => _inner.SendMouse(translated));
    }

    public static MouseEventData TranslateRelative(
        nint source, nint target, CoordinateMode mode, MouseEventData data)
    {
        if (mode == CoordinateMode.Absolute
            || source == nint.Zero
            || !GetClientRect(source, out RECT sourceRect)
            || !GetClientRect(target, out RECT targetRect))
        {
            return data;
        }

        int sourceWidth = sourceRect.Right - sourceRect.Left;
        int sourceHeight = sourceRect.Bottom - sourceRect.Top;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return data;
        }

        int targetWidth = targetRect.Right - targetRect.Left;
        int targetHeight = targetRect.Bottom - targetRect.Top;
        int x = (int)Math.Round((double)data.X / sourceWidth * targetWidth);
        int y = (int)Math.Round((double)data.Y / sourceHeight * targetHeight);
        return data with { X = x, Y = y };
    }

    private bool SendToTarget(nint target, Guid correlationId, string what, Func<bool> send)
    {
        if (target == nint.Zero)
        {
            return false;
        }

        (bool ok, string detail) = TryEnsureForeground(target);
        Interlocked.Increment(ref _focusAttempts);
        if (!ok)
        {
            Interlocked.Increment(ref _focusFailures);
            Trace(correlationId, what, target, $"focus=FAIL({detail})");
            return false;
        }

        bool sent;
        try
        {
            sent = send();
        }
        catch
        {
            sent = false;
        }

        if (sent)
        {
            Interlocked.Increment(ref _sends);
        }
        else
        {
            Interlocked.Increment(ref _sendFailures);
        }

        Trace(correlationId, what, target, $"focus=ok send={sent}");
        return sent;
    }

    private (bool Ok, string Detail) TryEnsureForeground(nint target)
    {
        try
        {
            return _ensureForeground(target);
        }
        catch
        {
            return (false, "focus callback threw");
        }
    }

    private void Trace(Guid correlationId, string what, nint target, string outcome)
    {
        try
        {
            string line = $"route #{correlationId:N} {what} -> 0x{target:X} {outcome}";
            _trace?.Invoke(line.Length > 200 ? line[..200] : line);
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out RECT rect);
}
