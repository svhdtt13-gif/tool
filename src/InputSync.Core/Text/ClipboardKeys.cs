namespace InputSync.Core.Text;

/// <summary>
/// Ctrl+letter combinations whose text effect is mirrored rather than
/// forwarded: V (paste from clipboard snapshot), X/Z/Y (cut/undo/redo
/// observed as text deltas), C (copy snapshot only, never forwarded so
/// targets cannot overwrite the shared clipboard).
/// </summary>
public static class ClipboardKeys
{
    public const uint V = 0x56;
    public const uint X = 0x58;
    public const uint Z = 0x5A;
    public const uint Y = 0x59;
    public const uint C = 0x43;

    public static bool IsPasteKey(uint virtualKey) => virtualKey == V;

    public static bool IsCopyKey(uint virtualKey) => virtualKey == C;

    public static bool IsMirroredCombo(uint virtualKey) =>
        virtualKey is V or X or Z or Y;
}
