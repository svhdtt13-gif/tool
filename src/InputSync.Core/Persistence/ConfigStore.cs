using System.Text.Json;
using System.Text.Json.Serialization;
using InputSync.Core.Models;

namespace InputSync.Core.Persistence;

public interface IWindowResolver
{
    WindowInfo? Find(string processName, string windowClass, string windowTitle);
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly IWindowResolver? _windowResolver;

    public ConfigStore(IWindowResolver? windowResolver = null)
    {
        _windowResolver = windowResolver;
    }

    public SyncConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        SyncConfig config = JsonSerializer.Deserialize<SyncConfig>(json, SerializerOptions)
            ?? throw new InvalidDataException("The configuration file did not contain a configuration object.");

        Resolve(config.Source);
        foreach (TargetConfig target in config.Targets)
        {
            Resolve(target);
        }

        // Phase 2: monitor unresolved profiles and reconnect when a matching window appears.
        return config;
    }

    public void Save(string path, SyncConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private void Resolve(WindowConfig? profile)
    {
        if (profile is null || _windowResolver is null)
        {
            return;
        }

        WindowInfo? match = _windowResolver.Find(
            profile.ProcessName,
            profile.WindowClass,
            profile.WindowTitle);
        profile.Hwnd = match?.Hwnd ?? nint.Zero;
    }
}
