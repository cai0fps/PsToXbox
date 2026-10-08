using System.Text.Json;

namespace PsToXbox.Services;

public sealed class Settings
{
    public double DeadzoneLeft { get; set; } = 0.05;     // 0..0.5
    public double DeadzoneRight { get; set; } = 0.05;
    public bool Rumble { get; set; } = true;
    public bool AutoEnable { get; set; } = false;        // ativar conversão ao conectar
    public bool HideOriginal { get; set; } = false;      // usar HidHide

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PsToXbox", "settings.json");

    public static Settings Current { get; } = Load();

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
