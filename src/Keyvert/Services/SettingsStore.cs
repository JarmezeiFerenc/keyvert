using System.Text.Json;
using Keyvert.Core;

namespace Keyvert.Services;

public sealed class AppSettings
{
    public string ToggleKey { get; set; } = "F12";
    public bool SuppressMappedKeys { get; set; } = true;
    public bool EnableOnStartup { get; set; }
    public bool ToggleBeep { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    public string? LastProfile { get; set; }
}

public sealed class SettingsStore(string filePath, AppLog log)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), Options) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warn($"Could not read settings, using defaults: {ex.Message}");
            settings = new();
        }

        if (!KeyNames.TryParse(settings.ToggleKey, out _, out var error))
        {
            log.Warn($"Invalid toggle key in settings ({error}), using F12.");
            settings.ToggleKey = "F12";
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            FileUtil.WriteAllTextAtomic(filePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("Could not save settings.", ex);
        }
    }
}

public static class FileUtil
{
    /// <summary>Writes to a temporary file first, so a crash mid-write can't leave a truncated file.</summary>
    public static void WriteAllTextAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
