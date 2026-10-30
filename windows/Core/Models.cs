using System.IO;

namespace TwinMemory.Core;

public class Item
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "FILE";
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string? FileName { get; set; }
    public string? Mime { get; set; }
    public long Size { get; set; }
    public string? Sha256 { get; set; }
    public string? RelPath { get; set; }
    public string Source { get; set; } = "pc";
    public string? FolderId { get; set; }
    public bool Favorite { get; set; }
    public long Created { get; set; }
    public long Updated { get; set; }
    public long? Trashed { get; set; }

    public string Glyph => Kind switch { "TEXT" => "📝", "LINK" => "🔗", "IMAGE" => "🖼️", "VIDEO" => "🎞️", "DOCUMENT" => "📄", _ => "📎" };
    public string KindLabel => S.T(Kind switch { "TEXT" => "نص", "LINK" => "رابط", "IMAGE" => "صورة", "VIDEO" => "فيديو", "DOCUMENT" => "مستند", _ => "ملف" });
    public string SourceLabel => S.T(Source == "phone" ? "الهاتف" : "الكمبيوتر");
    public string SizeText => Size <= 0 ? "" : Size < 1024 ? $"{Size} B" : Size < 1048576 ? $"{Size / 1024.0:0.#} KB" : Size < 1073741824 ? $"{Size / 1048576.0:0.#} MB" : $"{Size / 1073741824.0:0.##} GB";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(Created).LocalDateTime.ToString("g");
    public string Star => Favorite ? "★" : "";
}

public record Folder(string Id, string Name);
public record Choice(string? Id, string Name);

public enum SortBy { Date, Name, Size, Type }

public class Filter
{
    public string[]? Kinds;
    public string? Source;
    public bool FavOnly;
    public bool Trashed;
    public bool UseFolder;
    public string? FolderId;
    public string? Search;
}

public record DeviceInfo(string Id, string Name, string TokenHash, long PairedAt, long LastSeen);

public class Transfer
{
    public string Id { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Direction { get; set; } = "in";
    public string Kind { get; set; } = "FILE";
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Mime { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string State { get; set; } = "receiving";
    public string? ItemId { get; set; }
    public string? Error { get; set; }
    public long Started { get; set; }
    public long Finished { get; set; }

    public string StateLabel => S.T(Direction == "out"
        ? State switch { "done" => "تم — تحقّق الهاتف من سلامة الملف", "failed" => "فشل", _ => "بانتظار الهاتف" }
        : State switch { "done" => "تم — تحقّق الكمبيوتر من سلامة الملف", "failed" => "فشل", _ => "قيد الاستلام" });
    public string DirectionLabel => S.T(Direction == "out" ? "إلى الهاتف" : "من الهاتف");
    public string SizeText => Size <= 0 ? "" : Size < 1048576 ? $"{Size / 1024.0:0.#} KB" : $"{Size / 1048576.0:0.#} MB";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(Started).LocalDateTime.ToString("g");
}

public record SyncEntry(string ItemId, string RelPath, long Size, long MTime, string Sha256, string Title, string? FolderId);
