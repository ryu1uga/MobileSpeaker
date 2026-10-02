using System.Text.Json;

namespace MobileSpeaker.Core;

/// <summary>
/// Preferencias guardadas en %APPDATA%\MobileSpeaker\settings.json.
/// </summary>
public sealed class AppSettings
{
    public const int DefaultPort = 8765;

    public int Port { get; set; } = DefaultPort;

    /// <summary>Activar el puente automaticamente al abrir el programa.</summary>
    public bool AutoStartBridge { get; set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobileSpeaker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    if (settings.Port < 1 || settings.Port > 65535)
                        settings.Port = DefaultPort;
                    return settings;
                }
            }
        }
        catch
        {
            // Archivo corrupto o ilegible: se usan los valores por defecto.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudieron guardar las preferencias: {ex.Message}");
        }
    }
}
