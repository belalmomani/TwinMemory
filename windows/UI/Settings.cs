using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TwinMemory;

public static class Settings
{
    class Data
    {
        public string Language { get; set; } = "ar";
        public string? SyncRoot { get; set; }
        public string[]? Cats { get; set; }
    }

    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static Data _d = new();
    static string _path = "";

    public static string Language => _d.Language;
    public static string SyncRoot => _d.SyncRoot!;
    /// <summary>Texts, Links, Images, Files folder names. Chosen once (in the language at first run) so changing language never renames folders.</summary>
    public static string[] Cats => _d.Cats!;

    public static void Load(string dir)
    {
        _path = Path.Combine(dir, "settings.json");
        try { if (File.Exists(_path)) _d = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), Opts) ?? new(); } catch { _d = new(); }
        Apply();
        var changed = false;
        if (_d.Cats == null || _d.Cats.Length != 4) { _d.Cats = new[] { S.T("النصوص"), S.T("الروابط"), S.T("الصور"), S.T("الملفات") }; changed = true; }
        if (string.IsNullOrWhiteSpace(_d.SyncRoot))
        {
            _d.SyncRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TwinMemory");
            changed = true;
        }
        if (changed) Save();
    }

    static void Apply() => S.Ar = _d.Language switch
    {
        "en" => false,
        "auto" => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar",
        _ => true,
    };

    static void Save() { try { File.WriteAllText(_path, JsonSerializer.Serialize(_d, Opts)); } catch { } }

    public static void SaveLanguage(string lang) { _d.Language = lang; Save(); Apply(); }
}
