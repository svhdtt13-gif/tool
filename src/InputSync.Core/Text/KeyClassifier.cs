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
    /// Neutral keys always forward. With Ctrl or Alt held, every key forwards
    /// so shortcuts (Ctrl+A/C/V/X, Alt+menu) execute on targets from shared
    /// system state; the text mirror adopts snapshots silently in that case
    /// and emits nothing, so no duplicate emission is possible.
    /// </summary>
    public static bool ShouldForwardAsControl(uint virtualKey, bool altHeld, bool ctrlHeld) =>
        IsTextNeutralKey(virtualKey) || altHeld || ctrlHeld;
}
