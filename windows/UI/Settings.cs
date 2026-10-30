using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TwinMemory;

public static class Settings
{
    static string _path = "";
    public static string Language { get; private set; } = "ar";

    public static void Load(string dir)
    {
        _path = Path.Combine(dir, "settings.json");
        try
        {
            if (File.Exists(_path))
            {
                using var d = JsonDocument.Parse(File.ReadAllText(_path));
                if (d.RootElement.TryGetProperty("language", out var l)) Language = l.GetString() ?? "ar";
            }
        }
        catch { }
        Apply();
    }

    static void Apply() => S.Ar = Language switch
    {
        "en" => false,
        "auto" => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar",
        _ => true,
    };

    public static void SaveLanguage(string lang)
    {
        Language = lang;
        File.WriteAllText(_path, JsonSerializer.Serialize(new { language = lang }));
        Apply();
    }
}
