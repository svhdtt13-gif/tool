namespace InputSync.Core.Models;

/// <summary>Specifies how mouse coordinates are mapped to target windows.</summary>
public enum CoordinateMode
{
    /// <summary>Scale coordinates relative to each window's client area.</summary>
    Relative,

    /// <summary>Use absolute coordinates without client-area scaling.</summary>
    Absolute,
}
