namespace TwinMemory.Core;

/// <summary>Moves or copies a whole folder (with sub-folders and items) inside the app. Copy duplicates stored files (uses more space).</summary>
public class FolderOps
{
    readonly Database _db;
    readonly FileStore _store;
    public FolderOps(Database db, FileStore store) { _db = db; _store = store; }

    /// <summary>The folder itself plus all its descendants.</summary>
    public static HashSet<string> Subtree(IList<Folder> all, string rootId)
    {
        var set = new HashSet<string> { rootId };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var f in all) if (f.ParentId != null && set.Contains(f.ParentId) && set.Add(f.Id)) grew = true;
        }
        return set;
    }

    static string UniqueName(IEnumerable<Folder> siblings, string name)
    {
        var names = new HashSet<string>(siblings.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name)) return name;
        for (int i = 2; ; i++) { var n = $"{name} ({i})"; if (!names.Contains(n)) return n; }
    }

    /// <summary>Returns null on success, "cycle" when the target is the folder itself or inside it. Name clashes get " (2)"; nothing is overwritten.</summary>
    public string? Move(string id, string? newParent)
    {
        var all = _db.Folders();
        var f = all.FirstOrDefault(x => x.Id == id);
        if (f == null) return "missing";
        var sub = Subtree(all, id);
        if (newParent != null && sub.Contains(newParent)) return "cycle";
        if ((f.ParentId ?? "") == (newParent ?? "")) return null;
        var name = UniqueName(all.Where(x => x.ParentId == newParent && x.Id != id), f.Name);
        _db.SetFolderParent(id, newParent, name);
        _db.TouchItemsInFolders(sub); // so the new location propagates to the other device
        return null;
    }

    /// <summary>Returns the number of items copied.</summary>
    public int Copy(string id, string? newParent)
    {
        var all = _db.Folders();
        var src = all.FirstOrDefault(x => x.Id == id);
        if (src == null) return 0;
        int n = 0;
        void Rec(Folder f, string? parent)
        {
            var newId = _db.CreateFolderId(UniqueName(_db.Folders().Where(x => x.ParentId == parent), f.Name), parent);
            foreach (var it in _db.Query(new Filter { UseFolder = true, FolderId = f.Id })) if (CopyItem(it, newId)) n++;
            foreach (var child in all.Where(x => x.ParentId == f.Id)) Rec(child, newId);
        }
        Rec(src, newParent);
        return n;
    }

    bool CopyItem(Item it, string folderId)
    {
        var newId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? rel = null;
        if (it.RelPath != null) { rel = _store.Duplicate(it.RelPath, newId); if (rel == null) return false; }
        _db.Insert(new Item
        {
            Id = newId, Kind = it.Kind, Title = it.Title, Text = it.Text, FileName = it.FileName, Mime = it.Mime, Size = it.Size, Sha256 = it.Sha256,
            RelPath = rel, Source = "pc", FolderId = folderId, Created = now, Updated = now,
        });
        return true;
    }
}
