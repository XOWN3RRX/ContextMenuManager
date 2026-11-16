using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMenuManager;

internal enum PreviewStyleSetting
{
    MatchWindows,
    Windows11,
    Windows10,
}

/// <summary>Small app preferences, stored as JSON next to the entry list.</summary>
internal sealed class AppSettings
{
    private string _filePath = string.Empty;

    public PreviewStyleSetting PreviewStyle { get; set; } = PreviewStyleSetting.MatchWindows;

    public static AppSettings Load(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, "settings.json");
        AppSettings? settings = null;

        try
        {
            if (File.Exists(path))
            {
                settings = JsonSerializer.Deserialize(File.ReadAllText(path), AppSettingsJsonContext.Default.AppSettings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: use the defaults.
        }

        settings ??= new AppSettings();
        settings._filePath = path;
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings still apply for this session.
        }
    }
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
