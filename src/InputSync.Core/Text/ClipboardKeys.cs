namespace InputSync.Core.Text;

/// <summary>
/// Ctrl+letter combinations whose text effect is mirrored rather than
/// forwarded: V (paste from live clipboard), X/Z/Y (cut/undo/redo observed
/// as text deltas). All other Ctrl combinations (A/C/B/S/...) forward as
/// control keys because they change no text by themselves.
/// </summary>
public static class ClipboardKeys
{
    public const uint V = 0x56;
    public const uint X = 0x58;
    public const uint Z = 0x5A;
    public const uint Y = 0x59;

    public static bool IsPasteKey(uint virtualKey) => virtualKey == V;

    public static bool IsMirroredCombo(uint virtualKey) =>
        virtualKey is V or X or Z or Y;
}
