namespace InputSync.Core.Text;

/// <summary>
/// Splits physical keys into two disjoint paths: text-neutral control keys
/// (modifiers, navigation, function keys) forwarded as key down/up, and
/// everything else whose effect is mirrored from observed source text.
/// A key is never sent on both paths, so text cannot be emitted twice.
/// </summary>
public static class KeyClassifier
{
    public static bool IsTextNeutralKey(uint virtualKey) => virtualKey switch
    {
        0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x5D => true,
        0x14 or 0x90 or 0x91 or 0x13 or 0x1B or 0x2C => true,
        >= 0x21 and <= 0x28 => true,
        0x2D => true,
        >= 0x70 and <= 0x87 => true,
        _ => false,
    };

    /// <summary>
    /// Decides whether a key-down/up pair is forwarded as control input.
    /// Neutral keys always forward, except Insert/Delete with Ctrl or Shift
    /// held (cut/copy/paste variants whose effect is mirrored as text).
    /// With Ctrl held, only safe letters (app commands without text effects)
    /// forward; V/X/Z/Y/C go through the clipboard/text-mirror path.
    /// Alt without Ctrl forwards for menu access.
    /// </summary>
    public static bool ShouldForwardAsControl(uint virtualKey, bool altHeld, bool ctrlHeld, bool shiftHeld = false)
    {
        if (altHeld && !ctrlHeld)
        {
            return true;
        }

        if (IsTextNeutralKey(virtualKey))
        {
            return virtualKey is not (0x2D or 0x2E) || (!ctrlHeld && !shiftHeld);
        }

        return ctrlHeld && !altHeld && IsClipboardSafeLetter(virtualKey);
    }

    private static bool IsClipboardSafeLetter(uint virtualKey) =>
        virtualKey is 0x41 or 0x42 or 0x46 or 0x4E or 0x4F or 0x50 or 0x53 or 0x54 or 0x57;
}
