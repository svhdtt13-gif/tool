namespace InputSync.Core.Models;

/// <summary>
/// Which injection backend drives the targets.
/// Broadcast posts Win32 messages to every target in the background.
/// Foreground injects real input into the single foreground window.
/// GameRotation focuses each game client in turn and injects real input;
/// it is sequential focus rotation, not simultaneous background input.
/// </summary>
public enum TargetBackend
{
    Broadcast,
    Foreground,
    GameRotation,
}
