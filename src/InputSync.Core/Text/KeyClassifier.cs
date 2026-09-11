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
}
