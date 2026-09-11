using System.Text.Json.Serialization;
using InputSync.Core.Models;

namespace InputSync.Core.Persistence;

public sealed class SyncConfig
{
    public WindowConfig? Source { get; set; }

    public List<TargetConfig> Targets { get; set; } = [];

    public bool Keyboard { get; set; } = true;

    public bool Mouse { get; set; } = true;

    public CoordinateMode CoordinateMode { get; set; } = CoordinateMode.Relative;

    public HotkeyConfig Hotkeys { get; set; } = new();
}

public class WindowConfig
{
    [JsonIgnore]
    public nint Hwnd { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string WindowClass { get; set; } = string.Empty;

    public string WindowTitle { get; set; } = string.Empty;

    public static WindowConfig FromWindow(WindowInfo window) => new()
    {
        Hwnd = window.Hwnd,
        ProcessName = window.ProcessName,
        WindowClass = window.ClassName,
        WindowTitle = window.Title,
    };
}

public sealed class TargetConfig : WindowConfig
{
    public bool Enabled { get; set; } = true;
}

public sealed class HotkeyConfig
{
    public string Toggle { get; set; } = "F8";

    public string Stop { get; set; } = "F9";

    public string EmergencyStop { get; set; } = "F10";
}
