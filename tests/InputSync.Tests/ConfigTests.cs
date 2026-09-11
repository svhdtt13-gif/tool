using InputSync.Core.Models;
using InputSync.Core.Persistence;

namespace InputSync.Tests;

/// <summary>Slice 9 acceptance: config save/load roundtrip, HWND stays runtime-only.</summary>
public sealed class ConfigTests
{
    [Fact]
    public void SaveLoad_RoundtripsProfile()
    {
        var config = new SyncConfig
        {
            Keyboard = true,
            Mouse = true,
            CoordinateMode = CoordinateMode.Relative,
            Source = new WindowConfig
            {
                ProcessName = "game.exe",
                WindowClass = "GameWindow",
                WindowTitle = "Game A",
            },
            Targets =
            [
                new TargetConfig { ProcessName = "game.exe", WindowTitle = "Game B", Enabled = true },
            ],
            Hotkeys = new HotkeyConfig { Toggle = "F8", Stop = "F9", EmergencyStop = "F10" },
        };

        var store = new ConfigStore();
        string path = Path.Combine(Path.GetTempPath(), $"inputsync-{Guid.NewGuid():N}.json");
        try
        {
            store.Save(path, config);
            SyncConfig loaded = store.Load(path);

            Assert.Equal("game.exe", loaded.Source!.ProcessName);
            Assert.Equal("Game A", loaded.Source.WindowTitle);
            Assert.Single(loaded.Targets);
            Assert.Equal("Game B", loaded.Targets[0].WindowTitle);
            Assert.Equal(CoordinateMode.Relative, loaded.CoordinateMode);
            Assert.Equal("F10", loaded.Hotkeys.EmergencyStop);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hwnd_IsRuntimeOnly_NotPersisted()
    {
        var config = new SyncConfig
        {
            Source = new WindowConfig { Hwnd = new nint(123456), ProcessName = "game.exe" },
        };

        var store = new ConfigStore();
        string path = Path.Combine(Path.GetTempPath(), $"inputsync-{Guid.NewGuid():N}.json");
        try
        {
            store.Save(path, config);

            string json = File.ReadAllText(path);
            Assert.DoesNotContain("123456", json);

            SyncConfig loaded = store.Load(path);
            Assert.Equal(nint.Zero, loaded.Source!.Hwnd);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
