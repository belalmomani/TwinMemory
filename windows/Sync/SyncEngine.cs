using System.IO;
using System.Security.Cryptography;
using System.Text;
using TwinMemory.Core;

namespace TwinMemory.Sync;

/// <summary>
/// Two-way mirror between the app store and a normal Windows folder (Documents\TwinMemory) that Explorer shows.
/// Safety rules: never overwrite an existing file (names get " (2)"); files you edit in Explorer replace app content only after the
/// previous version is saved under %LOCALAPPDATA%\TwinMemory\versions; a file you delete in Explorer moves its item to the app's Trash
/// (recoverable), and mass disappearance (folder missing/moved) deletes nothing; our own writes are recorded so they never echo back.
/// This is an ordinary folder, not a virtual Explorer drive (see PHASE5_EXPLORER_EVAL.md).
/// </summary>
public class SyncEngine : IDisposable
{
    const long MaxFile = 4L * 1024 * 1024 * 1024;
    static readonly UTF8Encoding Utf8 = new(false);

    readonly Database _db;
    readonly FileStore _store;
    readonly string _dataDir;
    readonly string[] _cats;
    readonly SemaphoreSlim _gate = new(1, 1);
    FileSystemWatcher? _watcher;
    System.Threading.Timer? _debounce, _tick;
    string _lastMsg = "";

    public string Root { get; }
    public event Action? Changed;
    public event Action<string>? Message;

    public SyncEngine(Database db, FileStore store, string dataDir)
    {
        _db = db; _store = store; _dataDir = dataDir;
        Root = Settings.SyncRoot;
        _cats = Settings.Cats;
    }

    public void Start()
    {
        try
        {
            Directory.CreateDirectory(Root);
            foreach (var c in _cats) Directory.CreateDirectory(Path.Combine(Root, c));
            _watcher = new FileSystemWatcher(Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, _) => Trigger();
            _watcher.Changed += (_, _) => Trigger();
            _watcher.Deleted += (_, _) => Trigger();
            _watcher.Renamed += (_, _) => Trigger();
            _watcher.Error += (_, _) => Trigger();
        }
        catch (Exception ex) { Say(S.T("تعذّرت مزامنة المجلد") + ": " + ex.Message); }
        _tick = new System.Threading.Timer(_ => Trigger(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        Trigger();
    }

    public void Trigger(int ms = 1500)
    {
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => { _ = Task.Run(RunAsync); }, null, ms, Timeout.Infinite);
    }

    async Task RunAsync()
    {
        if (!await _gate.WaitAsync(0)) { Trigger(2000); return; }
        try
        {
            var r = await Task.Run(SyncOnce);
            if (r.Changed) Changed?.Invoke();
            if (r.Retry) Trigger(3000); // some files were still being written or locked
        }
        catch (Exception ex) { Say(S.T("تعذّرت مزامنة المجلد") + ": " + ex.Message); }
        finally { _gate.Release(); }
    }

    void Say(string m) { if (m == _lastMsg) return; _lastMsg = m; Message?.Invoke(m); }

    // ---------- helpers ----------
    string Rel(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');

    static string? TryHash(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static string? ReadText(FileInfo fi)
    {
        try
        {
            if (fi.Length > 1_000_000) return null;
            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Utf8, true);
            return sr.ReadToEnd();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static string ParseUrl(string? raw)
    {
        if (raw == null) return "";
        foreach (var line in raw.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return l[4..].Trim();
        }
        return "";
    }

    static string Safe(string name)
    {
        var n = Classify.SanitizeFileName(name).TrimEnd('.', ' ');
        if (n.Length == 0) n = "file";
        var stem = Path.GetFileNameWithoutExtension(n).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))) n = "_" + n;
        return n;
    }

    static string Unique(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(name); var ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            path = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    string CatDir(string kind) => kind switch { "TEXT" => _cats[0], "LINK" => _cats[1], "IMAGE" => _cats[2], _ => _cats[3] };

    static string ExportName(Item it)
    {
        var t = Safe(it.Title);
        return it.Kind switch
        {
            "TEXT" => Safe(t + ".txt"),
            "LINK" => Safe(t + ".url"),
            _ => Path.GetExtension(t).Length == 0 && !string.IsNullOrEmpty(it.FileName) && Path.GetExtension(it.FileName).Length > 0 ? Safe(t + Path.GetExtension(it.FileName)) : t,
        };
    }

    static string TitleFromFile(Item it, string fileName) =>
        it.Kind is "TEXT" or "LINK" ? Path.GetFileNameWithoutExtension(fileName) : fileName;

    static bool Ignore(FileInfo fi)
    {
        var n = fi.Name;
        if (n.StartsWith("~$") || n.StartsWith(".")) return true;
        if (fi.Extension.ToLowerInvariant() is ".tmp" or ".part" or ".crdownload" or ".partial") return true;
        if (n.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || n.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
        if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return true;
        return fi.Length > MaxFile;
    }

    record Result(bool Changed, bool Retry);

    // ---------- one sync pass ----------
    Result SyncOnce()
    {
        bool changed = false, retry = false;
        var rootWasMissing = !Directory.Exists(Root);
        Directory.CreateDirectory(Root);
        foreach (var c in _cats) Directory.CreateDirectory(Path.Combine(Root, c));

        var items = _db.Query(new Filter()).ToDictionary(i => i.Id); // active (non-trashed) items
        var folderByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nameByFolder = new Dictionary<string, string>();
        foreach (var f in _db.Folders()) { var sn = Safe(f.Name); if (folderByName.TryAdd(sn, f.Id)) nameByFolder[f.Id] = sn; }

        // sub-folders created in Explorer become folders in the app
        foreach (var d in Directory.EnumerateDirectories(Root))
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith(".") || _cats.Contains(name, StringComparer.OrdinalIgnoreCase) || folderByName.ContainsKey(name)) continue;
            var id = _db.CreateFolderId(name);
            folderByName[name] = id; nameByFolder[id] = name; changed = true;
        }

        string? FolderOf(string rel)
        {
            var i = rel.IndexOf('/');
            if (i < 0) return null;
            var top = rel[..i];
            if (_cats.Contains(top, StringComparer.OrdinalIgnoreCase)) return null;
            return folderByName.TryGetValue(top, out var fid) ? fid : null;
        }

        var disk = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(f);
                if (!Ignore(fi)) disk[Rel(f)] = fi;
            }
        }
        catch (Exception ex) { Say(S.T("تعذّرت مزامنة المجلد") + ": " + ex.Message); return new Result(changed, false); }

        var maps = _db.GetMaps();
        var byPath = maps.ToDictionary(m => m.RelPath, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var missing = new List<SyncEntry>();

        // 1) already-mirrored files: edits, app-side renames/moves, items removed from the app
        foreach (var m in maps)
        {
            if (!disk.TryGetValue(m.RelPath, out var fi)) { missing.Add(m); continue; }
            var unchanged = fi.Length == m.Size && fi.LastWriteTimeUtc.Ticks == m.MTime;
            if (!items.TryGetValue(m.ItemId, out var item))
            {
                // item trashed/deleted in the app: remove our mirror only if you never edited it; otherwise keep your file
                if (unchanged) { try { File.Delete(fi.FullName); } catch { } }
                _db.DeleteMap(m.ItemId);
                continue;
            }
            if (!unchanged)
            {
                if (now - fi.LastWriteTimeUtc < TimeSpan.FromSeconds(2)) { retry = true; continue; }
                var sha = TryHash(fi.FullName);
                if (sha == null) { retry = true; continue; }
                if (sha == m.Sha256) { _db.UpsertMap(m with { Size = fi.Length, MTime = fi.LastWriteTimeUtc.Ticks }); continue; }
                if (ApplyEdit(item, fi, sha, m)) changed = true; else retry = true;
                continue;
            }
            if (item.Title != m.Title || item.FolderId != m.FolderId)
            {
                if (MoveMirror(item, m, fi, nameByFolder)) { /* mirror follows an app-side rename/move */ }
            }
        }

        // 2) new files in the folder: renamed/moved mirrors, or genuinely new files
        var missingLeft = new List<SyncEntry>(missing);
        var fresh = new List<(FileInfo fi, string sha)>();
        foreach (var kv in disk)
        {
            if (byPath.ContainsKey(kv.Key)) continue;
            var fi = kv.Value;
            if (now - fi.LastWriteTimeUtc < TimeSpan.FromSeconds(2)) { retry = true; continue; }
            var sha = TryHash(fi.FullName);
            if (sha == null) { retry = true; continue; }
            var mv = missingLeft.FirstOrDefault(m => m.Size == fi.Length && m.Sha256 == sha);
            if (mv != null && items.TryGetValue(mv.ItemId, out var it))
            {
                missingLeft.Remove(mv);
                var rel = kv.Key; var folder = FolderOf(rel); var title = TitleFromFile(it, fi.Name);
                if (title != it.Title) _db.Rename(it.Id, title);
                if (folder != it.FolderId) _db.Move(it.Id, folder);
                _db.UpsertMap(new SyncEntry(it.Id, rel, fi.Length, fi.LastWriteTimeUtc.Ticks, sha, title, folder));
                changed = true;
                continue;
            }
            fresh.Add((fi, sha));
        }
        foreach (var (fi, sha) in fresh)
        {
            var r = ImportNew(fi, sha, FolderOf(Rel(fi.FullName)));
            if (r == 1) changed = true; else if (r == -1) retry = true;
        }

        // 3) files you deleted in Explorer -> app Trash (recoverable). Guarded against mass disappearance.
        var gone = missingLeft.Where(e => items.ContainsKey(e.ItemId)).ToList();
        foreach (var stale in missingLeft.Where(e => !items.ContainsKey(e.ItemId))) _db.DeleteMap(stale.ItemId);
        if (rootWasMissing || (gone.Count >= 5 && gone.Count * 2 > maps.Count))
        {
            if (gone.Count > 0) Say(S.T("تم إيقاف حذف عدد كبير من الملفات احتياطًا — راجع مجلد TwinMemory"));
        }
        else
        {
            foreach (var m in gone) { _db.SetTrashed(m.ItemId, true); _db.DeleteMap(m.ItemId); changed = true; }
        }

        // 4) items not yet in the folder -> write mirror copies
        var mapped = _db.GetMaps().Select(m => m.ItemId).ToHashSet();
        foreach (var it in items.Values)
        {
            if (mapped.Contains(it.Id)) continue;
            if (it.Trashed != null) continue;
            if (Export(it, nameByFolder)) changed = true; else if (it.Text != null || _store.PathFor(it.RelPath) != null) retry = true;
        }

        if (changed) Say(S.T("تمت مزامنة المجلد"));
        return new Result(changed, retry);
    }

    // ---------- operations ----------
    bool Export(Item it, Dictionary<string, string> nameByFolder)
    {
        try
        {
            var dirName = it.FolderId != null && nameByFolder.TryGetValue(it.FolderId, out var fn) ? fn : CatDir(it.Kind);
            var dir = Path.Combine(Root, dirName);
            Directory.CreateDirectory(dir);
            var dest = Unique(dir, ExportName(it));
            string? sha;
            if (it.Kind == "TEXT") { File.WriteAllText(dest, it.Text ?? "", Utf8); sha = null; }
            else if (it.Kind == "LINK") { File.WriteAllText(dest, "[InternetShortcut]\r\nURL=" + (it.Text ?? "").Trim() + "\r\n", Utf8); sha = null; }
            else
            {
                var src = _store.PathFor(it.RelPath);
                if (src == null) return false;
                File.Copy(src, dest, false); // never overwrites
                sha = it.Sha256;
            }
            sha ??= TryHash(dest);
            if (sha == null) return false;
            var fi = new FileInfo(dest);
            _db.UpsertMap(new SyncEntry(it.Id, Rel(dest), fi.Length, fi.LastWriteTimeUtc.Ticks, sha, it.Title, it.FolderId));
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    bool MoveMirror(Item it, SyncEntry m, FileInfo fi, Dictionary<string, string> nameByFolder)
    {
        try
        {
            var dirName = it.FolderId != null && nameByFolder.TryGetValue(it.FolderId, out var fn) ? fn : CatDir(it.Kind);
            var dir = Path.Combine(Root, dirName);
            Directory.CreateDirectory(dir);
            var desired = Path.Combine(dir, ExportName(it));
            if (string.Equals(desired, fi.FullName, StringComparison.OrdinalIgnoreCase))
            {
                _db.UpsertMap(m with { Title = it.Title, FolderId = it.FolderId }); // same path: only remember the new title/folder
                return true;
            }
            var dest = Unique(dir, ExportName(it));
            File.Move(fi.FullName, dest); // Unique() guarantees no overwrite
            var nf = new FileInfo(dest);
            _db.UpsertMap(m with { RelPath = Rel(dest), Size = nf.Length, MTime = nf.LastWriteTimeUtc.Ticks, Title = it.Title, FolderId = it.FolderId });
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    bool ApplyEdit(Item it, FileInfo fi, string sha, SyncEntry m)
    {
        try
        {
            var ver = Path.Combine(_dataDir, "versions", it.Id);
            Directory.CreateDirectory(ver);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (it.Kind is "TEXT" or "LINK")
            {
                var raw = ReadText(fi);
                if (raw == null) return false;
                var text = (it.Kind == "LINK" ? ParseUrl(raw) : raw).Trim();
                if (text.Length == 0) return false;
                File.WriteAllText(Path.Combine(ver, stamp + ".txt"), it.Text ?? "", Utf8); // previous version kept
                _db.UpdateText(it.Id, text, Encoding.UTF8.GetByteCount(text));
            }
            else
            {
                if (it.RelPath == null) return false;
                var cur = _store.PathFor(it.RelPath);
                if (cur != null) File.Copy(cur, Path.Combine(ver, stamp + "_" + Path.GetFileName(cur)), true); // previous version kept
                using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var st = _store.Replace(it.RelPath, fs);
                _db.UpdateFile(it.Id, st.Size, st.Sha256);
                sha = st.Sha256;
            }
            _db.UpsertMap(m with { Size = fi.Length, MTime = fi.LastWriteTimeUtc.Ticks, Sha256 = sha });
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>1 = imported, 0 = skipped, -1 = try again later.</summary>
    int ImportNew(FileInfo fi, string sha, string? folder)
    {
        var id = Guid.NewGuid().ToString();
        var rel = Rel(fi.FullName);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            if (fi.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var url = ParseUrl(ReadText(fi));
                if (Classify.IsSingleUrl(url))
                {
                    var title = Path.GetFileNameWithoutExtension(fi.Name);
                    _db.Insert(new Item { Id = id, Kind = "LINK", Title = title, Text = url, Mime = "text/plain", Size = Encoding.UTF8.GetByteCount(url), Source = "pc", FolderId = folder, Created = now, Updated = now });
                    _db.UpsertMap(new SyncEntry(id, rel, fi.Length, fi.LastWriteTimeUtc.Ticks, sha, title, folder));
                    return 1;
                }
            }
            Core.FileStore.Stored st;
            using (var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) st = _store.Store(id, fi.Name, fs);
            if (st.Sha256 != sha) { _store.Delete(id); return -1; } // changed while copying
            try
            {
                _db.Insert(new Item
                {
                    Id = id, Kind = Classify.KindFor(fi.Name), Title = fi.Name, FileName = fi.Name, Size = st.Size, Sha256 = st.Sha256, RelPath = st.RelPath,
                    Source = "pc", FolderId = folder, Created = now, Updated = now,
                });
            }
            catch { _store.Delete(id); throw; }
            _db.UpsertMap(new SyncEntry(id, rel, fi.Length, fi.LastWriteTimeUtc.Ticks, sha, fi.Name, folder));
            return 1;
        }
        catch (IOException) { return -1; }
        catch (UnauthorizedAccessException) { return -1; }
    }

    public void Dispose()
    {
        _watcher?.Dispose(); _debounce?.Dispose(); _tick?.Dispose();
    }
}
