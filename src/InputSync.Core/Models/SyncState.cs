namespace InputSync.Core.Models;

/// <summary>Describes the lifecycle state of input synchronization.</summary>
public enum SyncState
{
    /// <summary>Synchronization has not started.</summary>
    IDLE,

    /// <summary>Synchronization is processing input.</summary>
    RUNNING,

    /// <summary>Synchronization is temporarily paused.</summary>
    PAUSED,

    /// <summary>Synchronization is shutting down.</summary>
    STOPPING,

    /// <summary>Synchronization stopped because of an error.</summary>
    ERROR,
}
