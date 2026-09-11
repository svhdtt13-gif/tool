using System.Runtime.InteropServices;
using System.Threading.Channels;
using InputSync.Core.Models;
using InputSync.Core.Normalization;

namespace InputSync.Core.Capture;

public sealed class InputCapture : IDisposable
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_XBUTTONDOWN = 0x020B;
    private const uint WM_XBUTTONUP = 0x020C;
    private const ushort XBUTTON1 = 0x0001;

    private readonly object _lifecycleGate = new();
    private readonly Channel<RawHookEvent> _rawEvents;
    private readonly Channel<NormalizedInputEvent> _events;
    private readonly EventNormalizer _normalizer;
    private readonly IKeyboardLayoutTranslator? _translator;
    private readonly LowLevelHooks _hooks;
    private CancellationTokenSource? _captureCancellation;
    private Task? _processingTask;
    private nint _sourceHwnd;
    private bool _disposed;

    public InputCapture(
        nint sourceHwnd,
        EventNormalizer? normalizer = null,
        IKeyboardLayoutTranslator? translator = null)
    {
        if (sourceHwnd == nint.Zero)
        {
            throw new ArgumentException("A source window handle is required.", nameof(sourceHwnd));
        }

        _sourceHwnd = sourceHwnd;
        _normalizer = normalizer ?? new EventNormalizer();
        _translator = translator;
        _rawEvents = Channel.CreateUnbounded<RawHookEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _events = Channel.CreateUnbounded<NormalizedInputEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        _hooks = new LowLevelHooks(_rawEvents.Writer);
    }

    public ChannelReader<NormalizedInputEvent> Reader => _events.Reader;

    public nint SourceHwnd
    {
        get => Interlocked.CompareExchange(ref _sourceHwnd, nint.Zero, nint.Zero);
        set
        {
            if (value == nint.Zero)
            {
                throw new ArgumentException("A source window handle is required.", nameof(value));
            }

            Interlocked.Exchange(ref _sourceHwnd, value);
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lifecycleGate)
        {
            if (_captureCancellation is not null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            try
            {
                _hooks.Install();
                _captureCancellation = cancellation;
                _processingTask = Task.Run(() => ProcessEventsAsync(cancellation.Token));
            }
            catch
            {
                cancellation.Dispose();
                _hooks.Uninstall();
                throw;
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task? processingTask;

        lock (_lifecycleGate)
        {
            cancellation = _captureCancellation;
            processingTask = _processingTask;
            if (cancellation is null)
            {
                return;
            }

            _captureCancellation = null;
            _processingTask = null;
            _hooks.Uninstall();
            cancellation.Cancel();
        }

        try
        {
            processingTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _hooks.Dispose();
        _rawEvents.Writer.TryComplete();
        _events.Writer.TryComplete();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (RawHookEvent rawEvent in _rawEvents.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            nint sourceHwnd = SourceHwnd;
            nint foregroundHwnd = GetForegroundWindow();
            if (foregroundHwnd != sourceHwnd && !IsChild(sourceHwnd, foregroundHwnd))
            {
                continue;
            }

            NormalizedInputEvent? normalized = rawEvent.Kind == RawHookEventKind.Keyboard
                ? NormalizeKeyboard(sourceHwnd, rawEvent)
                : NormalizeMouse(sourceHwnd, rawEvent);

            if (normalized is not null)
            {
                _events.Writer.TryWrite(normalized);
            }
        }
    }

    private NormalizedInputEvent? NormalizeKeyboard(nint sourceHwnd, RawHookEvent rawEvent)
    {
        InputAction? action = rawEvent.Message switch
        {
            WM_KEYDOWN or WM_SYSKEYDOWN => InputAction.KeyDown,
            WM_KEYUP or WM_SYSKEYUP => InputAction.KeyUp,
            _ => null
        };

        return action is null
            ? null
            : _normalizer.NormalizeKeyboard(
                sourceHwnd,
                action.Value,
                rawEvent.KeyboardData.VkCode,
                rawEvent.KeyboardData.ScanCode,
                rawEvent.KeyboardData.Flags,
                rawEvent.KeyboardData.Time,
                TranslateText(action.Value, rawEvent));
    }

    private NormalizedInputEvent? NormalizeMouse(nint sourceHwnd, RawHookEvent rawEvent)
    {
        (InputAction Action, MouseButton Button)? mapping = rawEvent.Message switch
        {
            WM_MOUSEMOVE => (InputAction.MouseMove, MouseButton.None),
            WM_LBUTTONDOWN => (InputAction.LeftDown, MouseButton.Left),
            WM_LBUTTONUP => (InputAction.LeftUp, MouseButton.Left),
            WM_RBUTTONDOWN => (InputAction.RightDown, MouseButton.Right),
            WM_RBUTTONUP => (InputAction.RightUp, MouseButton.Right),
            WM_MBUTTONDOWN => (InputAction.MiddleDown, MouseButton.Middle),
            WM_MBUTTONUP => (InputAction.MiddleUp, MouseButton.Middle),
            WM_MOUSEWHEEL => (InputAction.Wheel, MouseButton.None),
            WM_XBUTTONDOWN => (InputAction.XButtonDown, GetXButton(rawEvent.MouseData.MouseData)),
            WM_XBUTTONUP => (InputAction.XButtonUp, GetXButton(rawEvent.MouseData.MouseData)),
            _ => null
        };

        if (mapping is null || !GetClientRect(sourceHwnd, out RECT clientRect))
        {
            return null;
        }

        LowLevelHooks.POINT point = rawEvent.MouseData.Point;
        if (!ScreenToClient(sourceHwnd, ref point))
        {
            return null;
        }

        int wheelDelta = rawEvent.Message == WM_MOUSEWHEEL
            ? (short)(rawEvent.MouseData.MouseData >> 16)
            : 0;

        return _normalizer.NormalizeMouse(
            sourceHwnd,
            mapping.Value.Action,
            point.X,
            point.Y,
            clientRect.Right - clientRect.Left,
            clientRect.Bottom - clientRect.Top,
            rawEvent.MouseData.Time,
            mapping.Value.Button,
            wheelDelta);
    }

    private static MouseButton GetXButton(uint mouseData) =>
        (ushort)(mouseData >> 16) == XBUTTON1 ? MouseButton.X1 : MouseButton.X2;

    private string TranslateText(InputAction action, RawHookEvent rawEvent)
    {
        if (action != InputAction.KeyDown || _translator is null)
        {
            return string.Empty;
        }

        try
        {
            return _translator.Translate(
                rawEvent.KeyboardData.VkCode,
                rawEvent.KeyboardData.ScanCode,
                rawEvent.KeyboardData.Flags) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
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
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref LowLevelHooks.POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out RECT rectangle);
}
