using InputSync.Core.Dispatch;
using InputSync.Core.Models;

namespace InputSync.Core.Safety;

public sealed class InputStateTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<nint, HashSet<uint>> _pressedKeys = [];
    private readonly Dictionary<nint, HashSet<MouseButton>> _pressedMouseButtons = [];

    public IReadOnlyDictionary<nint, HashSet<uint>> PressedKeys
    {
        get
        {
            lock (_gate)
            {
                return _pressedKeys.ToDictionary(pair => pair.Key, pair => new HashSet<uint>(pair.Value));
            }
        }
    }

    public IReadOnlyDictionary<nint, HashSet<MouseButton>> PressedMouseButtons
    {
        get
        {
            lock (_gate)
            {
                return _pressedMouseButtons.ToDictionary(pair => pair.Key, pair => new HashSet<MouseButton>(pair.Value));
            }
        }
    }

    public void TrackDown(nint target, uint virtualKey)
    {
        lock (_gate)
        {
            GetOrCreate(_pressedKeys, target).Add(virtualKey);
        }
    }

    public void TrackUp(nint target, uint virtualKey)
    {
        lock (_gate)
        {
            RemoveAndPrune(_pressedKeys, target, virtualKey);
        }
    }

    public void TrackDown(nint target, MouseButton button)
    {
        if (button == MouseButton.None)
        {
            return;
        }

        lock (_gate)
        {
            GetOrCreate(_pressedMouseButtons, target).Add(button);
        }
    }

    public void TrackUp(nint target, MouseButton button)
    {
        lock (_gate)
        {
            RemoveAndPrune(_pressedMouseButtons, target, button);
        }
    }

    public void ReleaseAll(nint target, ITargetAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        uint[] keys;
        MouseButton[] buttons;
        lock (_gate)
        {
            keys = _pressedKeys.Remove(target, out HashSet<uint>? pressedKeys) ? [.. pressedKeys] : [];
            buttons = _pressedMouseButtons.Remove(target, out HashSet<MouseButton>? pressedButtons)
                ? [.. pressedButtons]
                : [];
        }

        foreach (uint key in keys)
        {
            TryRelease(() => adapter.SendKeyboard(
                new KeyboardEventData(target, key, ScanCode: 0, KeyboardAction.Up)));
        }

        MouseButtonMask remaining = ToMask(buttons);
        foreach (MouseButton button in buttons)
        {
            remaining &= ~ToMask(button);
            TryRelease(() => adapter.SendMouse(
                new MouseEventData(target, MouseAction.ButtonUp, 0, 0, button, remaining)));
        }
    }

    public void Reset(nint target)
    {
        lock (_gate)
        {
            _pressedKeys.Remove(target);
            _pressedMouseButtons.Remove(target);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pressedKeys.Clear();
            _pressedMouseButtons.Clear();
        }
    }

    private static HashSet<TValue> GetOrCreate<TValue>(
        Dictionary<nint, HashSet<TValue>> states,
        nint target)
        where TValue : notnull
    {
        if (!states.TryGetValue(target, out HashSet<TValue>? values))
        {
            values = [];
            states[target] = values;
        }

        return values;
    }

    private static void RemoveAndPrune<TValue>(
        Dictionary<nint, HashSet<TValue>> states,
        nint target,
        TValue value)
        where TValue : notnull
    {
        if (states.TryGetValue(target, out HashSet<TValue>? values) &&
            values.Remove(value) &&
            values.Count == 0)
        {
            states.Remove(target);
        }
    }

    private static MouseButtonMask ToMask(IEnumerable<MouseButton> buttons)
    {
        MouseButtonMask mask = MouseButtonMask.None;
        foreach (MouseButton button in buttons)
        {
            mask |= ToMask(button);
        }

        return mask;
    }

    private static MouseButtonMask ToMask(MouseButton button) => button switch
    {
        MouseButton.Left => MouseButtonMask.Left,
        MouseButton.Right => MouseButtonMask.Right,
        MouseButton.Middle => MouseButtonMask.Middle,
        MouseButton.X1 => MouseButtonMask.X1,
        MouseButton.X2 => MouseButtonMask.X2,
        _ => MouseButtonMask.None,
    };

    private static void TryRelease(Func<bool> release)
    {
        try
        {
            _ = release();
        }
        catch
        {
        }
    }
}
