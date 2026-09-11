using System.Runtime.InteropServices;
using InputSync.Core.Dispatch;
using InputSync.Core.Models;

namespace InputSync.Win32;

/// <summary>
/// Fan-out adapter used by the engine: translates source-client coordinates
/// per target (relative/absolute), resolves the real input endpoint
/// (edit child or top-level window), and posts WM_KEYDOWN/WM_CHAR/WM_KEYUP
/// plus mouse messages. Never throws out of Send methods.
/// </summary>
public sealed class SyncTargetAdapter : ITargetAdapter
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_CHAR = 0x0102;

    private readonly Func<nint> _getSourceHwnd;
    private readonly Func<CoordinateMode> _getCoordinateMode;
    private readonly TargetEndpointResolver _resolver;
    private readonly LatencyTracker _latency;
    private readonly Action<string>? _trace;
    private readonly Win32MessageAdapter _inner = new();
    private long _eventsDispatched;
    private long _sendFailures;

    public SyncTargetAdapter(
        Func<nint> sourceHwnd,
        Func<CoordinateMode> coordinateMode,
        TargetEndpointResolver? resolver = null,
        LatencyTracker? latency = null,
        Action<string>? trace = null)
    {
        _getSourceHwnd = sourceHwnd ?? throw new ArgumentNullException(nameof(sourceHwnd));
        _getCoordinateMode = coordinateMode ?? throw new ArgumentNullException(nameof(coordinateMode));
        _resolver = resolver ?? new TargetEndpointResolver();
        _latency = latency ?? new LatencyTracker();
        _trace = trace;
    }

    public LatencyTracker Latency => _latency;

    public long EventsDispatched => Interlocked.Read(ref _eventsDispatched);

    public long SendFailures => Interlocked.Read(ref _sendFailures);

    public bool IsSupported() => _inner.IsSupported();

    public bool SendKeyboard(KeyboardEventData eventData)
    {
        try
        {
            if (!IsWindow(eventData.TargetHwnd))
            {
                return Fail(eventData.CorrelationId);
            }

            nint endpoint = _resolver.Resolve(eventData.TargetHwnd);
            var routed = eventData with { TargetHwnd = endpoint };
            if (!_inner.SendKeyboard(routed))
            {
                return Fail(eventData.CorrelationId);
            }

            if (eventData.Action == KeyboardAction.Down && !string.IsNullOrEmpty(eventData.Text))
            {
                nint charLParam = BuildCharLParam(eventData);
                foreach (char ch in eventData.Text)
                {
                    PostMessage(endpoint, WM_CHAR, (nuint)ch, charLParam);
                }
            }

            return Succeed(eventData.CorrelationId);
        }
        catch
        {
            return Fail(eventData.CorrelationId);
        }
    }

    public bool SendMouse(MouseEventData eventData)
    {
        try
        {
            if (!IsWindow(eventData.TargetHwnd))
            {
                return Fail(eventData.CorrelationId);
            }

            if (!TryTranslateTopLevel(eventData, out int x, out int y, out string trace))
            {
                return Fail(eventData.CorrelationId);
            }

            try
            {
                _trace?.Invoke(trace);
            }
            catch
            {
            }

            var routed = eventData with { X = x, Y = y };
            return _inner.SendMouse(routed) ? Succeed(eventData.CorrelationId) : Fail(eventData.CorrelationId);
        }
        catch
        {
            return Fail(eventData.CorrelationId);
        }
    }

    public bool SendText(nint targetHwnd, string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text) || !IsWindow(targetHwnd))
            {
                return false;
            }

            nint endpoint = _resolver.Resolve(targetHwnd);
            nint charLParam = unchecked((nint)1);
            bool posted = true;
            foreach (char ch in text)
            {
                posted = PostMessage(endpoint, WM_CHAR, (nuint)ch, charLParam) && posted;
            }

            if (posted)
            {
                Interlocked.Increment(ref _eventsDispatched);
            }
            else
            {
                Interlocked.Increment(ref _sendFailures);
            }

            return posted;
        }
        catch
        {
            Interlocked.Increment(ref _sendFailures);
            return false;
        }
    }

    private bool TryTranslateTopLevel(MouseEventData eventData, out int x, out int y, out string trace)
    {
        trace = string.Empty;
        x = 0;
        y = 0;

        nint source = _getSourceHwnd();
        if (source == nint.Zero || !GetClientRect(source, out RECT sourceRect))
        {
            return false;
        }

        if (!GetClientRect(eventData.TargetHwnd, out RECT targetRect))
        {
            return false;
        }

        int sourceWidth = sourceRect.Right - sourceRect.Left;
        int sourceHeight = sourceRect.Bottom - sourceRect.Top;
        int targetWidth = targetRect.Right - targetRect.Left;
        int targetHeight = targetRect.Bottom - targetRect.Top;
        double normalizedX = sourceWidth > 0 ? (double)eventData.X / sourceWidth : 0;
        double normalizedY = sourceHeight > 0 ? (double)eventData.Y / sourceHeight : 0;

        if (_getCoordinateMode() == CoordinateMode.Absolute)
        {
            x = eventData.X;
            y = eventData.Y;
        }
        else
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                return false;
            }

            x = (int)Math.Round(normalizedX * targetWidth);
            y = (int)Math.Round(normalizedY * targetHeight);
        }

        trace = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"mouse {eventData.Action}/{eventData.Button} raw=({eventData.RawX},{eventData.RawY}) src=({eventData.X},{eventData.Y}) norm=({normalizedX:F4},{normalizedY:F4}) tgt={targetWidth}x{targetHeight} -> ({x},{y})");
        return true;
    }

    private static nint BuildCharLParam(KeyboardEventData eventData)
    {
        uint value = 1u | ((eventData.ScanCode & 0xFFu) << 16);
        if (eventData.IsExtended)
        {
            value |= 1u << 24;
        }

        return unchecked((nint)(int)value);
    }

    private bool Succeed(Guid correlationId)
    {
        _latency.RecordSent(correlationId);
        Interlocked.Increment(ref _eventsDispatched);
        return true;
    }

    private bool Fail(Guid correlationId)
    {
        Interlocked.Increment(ref _sendFailures);
        return false;
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
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hwnd, out RECT rect);
}
