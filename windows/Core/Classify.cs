using System.Text.RegularExpressions;

namespace TwinMemory.Core;

public static class Classify
{
    static readonly HashSet<string> DocExt = new() { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "csv", "rtf", "odt", "ods", "odp" };
    static readonly HashSet<string> Risky = new() { "exe", "bat", "cmd", "com", "msi", "ps1", "vbs", "js", "jse", "wsf", "scr", "lnk", "reg", "jar", "hta", "pif" };

    public static string KindFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (ext is "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "heic") return "IMAGE";
        if (ext is "mp4" or "mkv" or "avi" or "mov" or "webm" or "wmv") return "VIDEO";
        if (DocExt.Contains(ext)) return "DOCUMENT";
        return "FILE";
    }

    public static bool IsRisky(string fileName) => Risky.Contains(Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant());

    public static bool IsSingleUrl(string s)
    {
        var t = s.Trim();
        return t.Length > 8 && (t.StartsWith("http://") || t.StartsWith("https://")) && !t.Any(char.IsWhiteSpace);
    }

    public static string HostOf(string url)
    {
        var t = url.Trim();
        var i = t.IndexOf("://", StringComparison.Ordinal);
        if (i >= 0) t = t[(i + 3)..];
        return t.Split('/', '?', '#')[0];
    }

    public static string TitleFor(string text)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 60 ? line[..60] : line;
    }

    public static string SanitizeFileName(string? name)
    {
        var cleaned = Regex.Replace(name ?? "", "[\\\\/\\u0000-\\u001F:*?\"<>|]", "_").Trim().TrimStart('.');
        if (cleaned.Length > 120) cleaned = cleaned[..120];
        return cleaned.Length == 0 ? "file" : cleaned;
    }

    public static string? LikePattern(string q)
    {
        var t = q.Trim();
        if (t.Length == 0) return null;
        return "%" + t.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    }
}
