namespace InputSync.Core.Models;

/// <summary>
/// Which injection backend drives the targets.
/// Broadcast posts Win32 messages to every target in the background.
/// Foreground injects real input into the single foreground window.
/// </summary>
public enum TargetBackend
{
    Broadcast,
    Foreground,
}
