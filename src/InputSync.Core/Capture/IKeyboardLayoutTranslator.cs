namespace InputSync.Core.Capture;

/// <summary>
/// Translates a low-level keyboard hook event into printable text.
/// Implemented in the Win32 layer (ToUnicode); core code stays platform-neutral.
/// </summary>
public interface IKeyboardLayoutTranslator
{
    /// <summary>
    /// Returns the printable text for a key-down event, or empty when the key
    /// produces no text (modifiers, navigation keys, dead-key state).
    /// Must never throw.
    /// </summary>
    string Translate(uint virtualKey, uint scanCode, uint flags);
}
