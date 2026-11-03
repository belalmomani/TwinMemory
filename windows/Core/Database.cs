using Microsoft.Data.Sqlite;

namespace TwinMemory.Core;

/// <summary>SQLite metadata store. Schema changes go through Migrate() steps keyed on user_version; data is never dropped.</summary>
public class Database
{
    readonly string _cs;
    public Database(string path)
    {
        _cs = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        Migrate();
    }

    SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }

    static int Exec(SqliteConnection c, string sql, params (string n, object? v)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    void Migrate()
    {
        using var c = Open();
        long v;
        using (var cmd = c.CreateCommand()) { cmd.CommandText = "PRAGMA user_version"; v = (long)(cmd.ExecuteScalar() ?? 0L); }
        if (v < 1)
        {
            Exec(c, @"CREATE TABLE IF NOT EXISTS folders(id TEXT PRIMARY KEY, name TEXT NOT NULL, parent_id TEXT, created INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS items(id TEXT PRIMARY KEY, kind TEXT NOT NULL, title TEXT NOT NULL, text TEXT, file_name TEXT, mime TEXT,
 size INTEGER NOT NULL, sha256 TEXT, rel_path TEXT, source TEXT NOT NULL, folder_id TEXT, favorite INTEGER NOT NULL,
 created INTEGER NOT NULL, updated INTEGER NOT NULL, trashed INTEGER);
CREATE INDEX IF NOT EXISTS ix_items_folder ON items(folder_id);
CREATE INDEX IF NOT EXISTS ix_items_created ON items(created);
CREATE INDEX IF NOT EXISTS ix_items_sha ON items(sha256);
CREATE TABLE IF NOT EXISTS item_tags(item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE, tag TEXT NOT NULL, PRIMARY KEY(item_id, tag));");
            Exec(c, "PRAGMA user_version = 1");
        }
        if (v < 2)
        {
            Exec(c, @"CREATE TABLE IF NOT EXISTS devices(id TEXT PRIMARY KEY, name TEXT NOT NULL, token_hash TEXT NOT NULL, paired_at INTEGER NOT NULL, last_seen INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS transfers(id TEXT PRIMARY KEY, device_id TEXT NOT NULL, direction TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL,
 file_name TEXT, mime TEXT, size INTEGER NOT NULL, sha256 TEXT, state TEXT NOT NULL, item_id TEXT, error TEXT, started INTEGER NOT NULL, finished INTEGER);");
            Exec(c, "PRAGMA user_version = 2");
        }
        if (v < 3)
        {
            Exec(c, @"CREATE TABLE IF NOT EXISTS sync_map(item_id TEXT PRIMARY KEY, rel_path TEXT NOT NULL UNIQUE COLLATE NOCASE, size INTEGER NOT NULL,
 mtime INTEGER NOT NULL, sha256 TEXT NOT NULL, title TEXT NOT NULL, folder_id TEXT);");
            Exec(c, "PRAGMA user_version = 3");
        }
        if (v < 4)
        {
            Exec(c, "ALTER TABLE transfers ADD COLUMN folder_path TEXT");
            Exec(c, "PRAGMA user_version = 4");
        }
        if (v < 5)
        {
            // sync_id = the same id on both devices (for items already transferred: the transfer id both sides know)
            Exec(c, @"ALTER TABLE items ADD COLUMN sync_id TEXT;
ALTER TABLE transfers ADD COLUMN sync_id TEXT;
UPDATE items SET sync_id = COALESCE((SELECT t.id FROM transfers t WHERE t.item_id = items.id AND t.state = 'done' LIMIT 1), id);
CREATE INDEX IF NOT EXISTS ix_items_sync ON items(sync_id);");
            Exec(c, "PRAGMA user_version = 5");
        }
    }

    const string Cols = "id,kind,title,text,file_name,mime,size,sha256,rel_path,source,folder_id,favorite,created,updated,trashed,sync_id";

    public List<Item> Query(Filter f)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        var w = new List<string> { f.Trashed ? "trashed IS NOT NULL" : "trashed IS NULL" };
        if (f.Kinds != null)
        {
            var names = new List<string>();
            for (int i = 0; i < f.Kinds.Length; i++) { names.Add("$k" + i); cmd.Parameters.AddWithValue("$k" + i, f.Kinds[i]); }
            w.Add($"kind IN ({string.Join(",", names)})");
        }
        if (f.Source != null) { w.Add("source = $src"); cmd.Parameters.AddWithValue("$src", f.Source); }
        if (f.FavOnly) w.Add("favorite = 1");
        if (f.UseFolder)
        {
            if (f.FolderId == null) w.Add("folder_id IS NULL");
            else { w.Add("folder_id = $fid"); cmd.Parameters.AddWithValue("$fid", f.FolderId); }
        }
        var pat = Classify.LikePattern(f.Search ?? "");
        if (pat != null)
        {
            w.Add(@"(title LIKE $q ESCAPE '\' OR text LIKE $q ESCAPE '\' OR file_name LIKE $q ESCAPE '\' OR source LIKE $q ESCAPE '\'
 OR EXISTS(SELECT 1 FROM item_tags t WHERE t.item_id = items.id AND t.tag LIKE $q ESCAPE '\'))");
            cmd.Parameters.AddWithValue("$q", pat);
        }
        cmd.CommandText = $"SELECT {Cols} FROM items WHERE {string.Join(" AND ", w)} ORDER BY created DESC";
        var list = new List<Item>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            list.Add(new Item
            {
                Id = r.GetString(0), Kind = r.GetString(1), Title = r.GetString(2), Text = S(3), FileName = S(4), Mime = S(5),
                Size = r.GetInt64(6), Sha256 = S(7), RelPath = S(8), Source = r.GetString(9), FolderId = S(10),
                Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14), SyncId = S(15),
            });
        }
        return list;
    }

    public void Insert(Item i)
    {
        using var c = Open();
        Exec(c, $"INSERT INTO items({Cols}) VALUES($id,$kind,$title,$text,$fn,$mime,$size,$sha,$rel,$src,$fid,$fav,$cr,$up,$tr,$sy)",
            ("$id", i.Id), ("$kind", i.Kind), ("$title", i.Title), ("$text", i.Text), ("$fn", i.FileName), ("$mime", i.Mime), ("$size", i.Size),
            ("$sha", i.Sha256), ("$rel", i.RelPath), ("$src", i.Source), ("$fid", i.FolderId), ("$fav", i.Favorite ? 1 : 0),
            ("$cr", i.Created), ("$up", i.Updated), ("$tr", i.Trashed), ("$sy", string.IsNullOrEmpty(i.SyncId) ? i.Id : i.SyncId));
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Rename(string id, string title) { using var c = Open(); Exec(c, "UPDATE items SET title=$t, updated=$n WHERE id=$id", ("$t", title), ("$n", Now()), ("$id", id)); }
    public void SetFavorite(string id, bool fav) { using var c = Open(); Exec(c, "UPDATE items SET favorite=$f WHERE id=$id", ("$f", fav ? 1 : 0), ("$id", id)); }
    public void Move(string id, string? folder) { using var c = Open(); Exec(c, "UPDATE items SET folder_id=$f, updated=$n WHERE id=$id", ("$f", folder), ("$n", Now()), ("$id", id)); }
    public void SetTrashed(string id, bool trashed) { using var c = Open(); Exec(c, "UPDATE items SET trashed=$t, updated=$n WHERE id=$id", ("$t", trashed ? (object)Now() : null), ("$n", Now()), ("$id", id)); }
    public void Delete(string id) { using var c = Open(); Exec(c, "DELETE FROM items WHERE id=$id", ("$id", id)); }

    public int CountSha(string sha)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM items WHERE sha256=$s AND trashed IS NULL"; cmd.Parameters.AddWithValue("$s", sha);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public long TotalSize()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(size),0) FROM items WHERE trashed IS NULL";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public List<Folder> Folders()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,parent_id FROM folders ORDER BY name COLLATE NOCASE";
        var l = new List<Folder>(); using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(new Folder(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return l;
    }

    public void CreateFolder(string name, string? parentId = null) => CreateFolderId(name, parentId);

    /// <summary>"A/B/C" for a folder id (null for the root).</summary>
    public string? FolderPath(string? id)
    {
        if (id == null) return null;
        var all = Folders().ToDictionary(f => f.Id);
        var parts = new List<string>(); var cur = all.GetValueOrDefault(id); int g = 0;
        while (cur != null && g++ < 50) { parts.Insert(0, cur.Name); cur = cur.ParentId != null ? all.GetValueOrDefault(cur.ParentId) : null; }
        return parts.Count == 0 ? null : string.Join("/", parts);
    }

    /// <summary>Creates the folder chain for "A/B/C" if missing (case-insensitive match) and returns the last folder's id. Ignores "." and "..".</summary>
    public string? EnsureFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var segs = path.Split('/', '\\').Select(x => x.Trim()).Where(x => x.Length > 0 && x != "." && x != "..").Take(20).ToList();
        if (segs.Count == 0) return null;
        var all = Folders(); string? parent = null;
        foreach (var raw in segs)
        {
            var seg = raw.Length > 100 ? raw[..100] : raw;
            var ex = all.FirstOrDefault(f => f.ParentId == parent && f.Name.Equals(seg, StringComparison.OrdinalIgnoreCase));
            if (ex != null) { parent = ex.Id; continue; }
            var id = CreateFolderId(seg, parent);
            all.Add(new Folder(id, seg, parent));
            parent = id;
        }
        return parent;
    }
    public void RenameFolder(string id, string name) { using var c = Open(); Exec(c, "UPDATE folders SET name=$n WHERE id=$id", ("$n", name), ("$id", id)); }

    // ---- paired devices ----
    public DeviceInfo? GetDevice(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,token_hash,paired_at,last_seen FROM devices WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DeviceInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)) : null;
    }

    public List<DeviceInfo> Devices()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,name,token_hash,paired_at,last_seen FROM devices ORDER BY paired_at";
        var l = new List<DeviceInfo>(); using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(new DeviceInfo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4)));
        return l;
    }

    public void UpsertDevice(string id, string name, string tokenHash)
    {
        using var c = Open();
        Exec(c, "INSERT INTO devices(id,name,token_hash,paired_at,last_seen) VALUES($id,$n,$t,$now,$now) ON CONFLICT(id) DO UPDATE SET name=$n, token_hash=$t, paired_at=$now",
            ("$id", id), ("$n", name), ("$t", tokenHash), ("$now", Now()));
    }
    public void TouchDevice(string id) { using var c = Open(); Exec(c, "UPDATE devices SET last_seen=$n WHERE id=$id", ("$n", Now()), ("$id", id)); }
    public void DeleteDevice(string id) { using var c = Open(); Exec(c, "DELETE FROM devices WHERE id=$id", ("$id", id)); }

    // ---- transfers ----
    public void InsertTransfer(Transfer t)
    {
        using var c = Open();
        Exec(c, "INSERT INTO transfers(id,device_id,direction,kind,title,file_name,mime,size,sha256,state,item_id,error,started,finished,folder_path,sync_id) VALUES($id,$d,$dir,$k,$t,$fn,$m,$s,$sha,$st,$it,$e,$sa,$fi,$fp,$sy)",
            ("$id", t.Id), ("$d", t.DeviceId), ("$dir", t.Direction), ("$k", t.Kind), ("$t", t.Title), ("$fn", t.FileName), ("$m", t.Mime), ("$s", t.Size),
            ("$sha", t.Sha256), ("$st", t.State), ("$it", t.ItemId), ("$e", t.Error), ("$sa", t.Started), ("$fi", t.Finished), ("$fp", t.FolderPath), ("$sy", t.SyncId));
    }

    const string TCols = "t.id,t.device_id,t.direction,t.kind,t.title,t.file_name,t.mime,t.size,t.sha256,t.state,t.item_id,t.error,t.started,t.finished,COALESCE(d.name,''),t.folder_path,t.sync_id";
    static Transfer ReadTransfer(SqliteDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new Transfer
        {
            Id = r.GetString(0), DeviceId = r.GetString(1), Direction = r.GetString(2), Kind = r.GetString(3), Title = r.GetString(4),
            FileName = S(5) ?? "", Mime = S(6) ?? "", Size = r.GetInt64(7), Sha256 = S(8) ?? "", State = r.GetString(9), ItemId = S(10), Error = S(11),
            Started = r.GetInt64(12), Finished = r.IsDBNull(13) ? 0 : r.GetInt64(13), DeviceName = r.GetString(14), FolderPath = S(15), SyncId = S(16),
        };
    }

    public Transfer? GetTransfer(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {TCols} FROM transfers t LEFT JOIN devices d ON d.id=t.device_id WHERE t.id=$id"; cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTransfer(r) : null;
    }

    public List<Transfer> ListTransfers(int limit)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {TCols} FROM transfers t LEFT JOIN devices d ON d.id=t.device_id ORDER BY t.started DESC LIMIT $l"; cmd.Parameters.AddWithValue("$l", limit);
        var l = new List<Transfer>(); using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(ReadTransfer(r));
        return l;
    }

    public void SetTransferState(string id, string state, string? error) { using var c = Open(); Exec(c, "UPDATE transfers SET state=$s, error=$e, finished=$f WHERE id=$id", ("$s", state), ("$e", error), ("$f", Now()), ("$id", id)); }
    public void FinishTransfer(string id, string itemId) { using var c = Open(); Exec(c, "UPDATE transfers SET state='done', item_id=$i, error=NULL, finished=$f WHERE id=$id", ("$i", itemId), ("$f", Now()), ("$id", id)); }

    // ---- outbox (computer -> phone) lives in the transfers table with direction='out', state='queued' ----
    public List<Transfer> ListOutbox(string deviceId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {TCols} FROM transfers t LEFT JOIN devices d ON d.id=t.device_id WHERE t.device_id=$d AND t.direction='out' AND t.state='queued' ORDER BY t.started";
        cmd.Parameters.AddWithValue("$d", deviceId);
        var l = new List<Transfer>(); using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(ReadTransfer(r));
        return l;
    }

    public Item? GetItem(string id)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM items WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new Item
        {
            Id = r.GetString(0), Kind = r.GetString(1), Title = r.GetString(2), Text = S(3), FileName = S(4), Mime = S(5),
            Size = r.GetInt64(6), Sha256 = S(7), RelPath = S(8), Source = r.GetString(9), FolderId = S(10),
            Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14), SyncId = S(15),
        };
    }

    /// <summary>PC-origin items not yet queued for this phone (auto-sync). Large files above maxBytes are left for manual sending.</summary>
    public List<Item> ItemsNeedingOutbox(string deviceId, long maxBytes)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $@"SELECT {Cols} FROM items WHERE source='pc' AND trashed IS NULL
 AND (kind IN ('TEXT','LINK') OR (rel_path IS NOT NULL AND size <= $max))
 AND NOT EXISTS (SELECT 1 FROM transfers t WHERE t.item_id = items.id AND t.device_id = $d AND t.direction = 'out')";
        cmd.Parameters.AddWithValue("$d", deviceId); cmd.Parameters.AddWithValue("$max", maxBytes);
        var list = new List<Item>(); using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            list.Add(new Item
            {
                Id = r.GetString(0), Kind = r.GetString(1), Title = r.GetString(2), Text = S(3), FileName = S(4), Mime = S(5),
                Size = r.GetInt64(6), Sha256 = S(7), RelPath = S(8), Source = r.GetString(9), FolderId = S(10),
                Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14), SyncId = S(15),
            });
        }
        return list;
    }

    // ---- Explorer folder mirror ----
    public List<SyncEntry> GetMaps()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT item_id,rel_path,size,mtime,sha256,title,folder_id FROM sync_map";
        var l = new List<SyncEntry>(); using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(new SyncEntry(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return l;
    }

    public void UpsertMap(SyncEntry m)
    {
        using var c = Open();
        Exec(c, "DELETE FROM sync_map WHERE rel_path=$p AND item_id<>$i", ("$p", m.RelPath), ("$i", m.ItemId));
        Exec(c, @"INSERT INTO sync_map(item_id,rel_path,size,mtime,sha256,title,folder_id) VALUES($i,$p,$s,$m,$h,$t,$f)
 ON CONFLICT(item_id) DO UPDATE SET rel_path=$p, size=$s, mtime=$m, sha256=$h, title=$t, folder_id=$f",
            ("$i", m.ItemId), ("$p", m.RelPath), ("$s", m.Size), ("$m", m.MTime), ("$h", m.Sha256), ("$t", m.Title), ("$f", m.FolderId));
    }

    public void DeleteMap(string itemId) { using var c = Open(); Exec(c, "DELETE FROM sync_map WHERE item_id=$i", ("$i", itemId)); }
    public void UpdateText(string id, string text, long size) { using var c = Open(); Exec(c, "UPDATE items SET text=$t, size=$s, updated=$n WHERE id=$id", ("$t", text), ("$s", size), ("$n", Now()), ("$id", id)); }
    public void UpdateFile(string id, long size, string sha) { using var c = Open(); Exec(c, "UPDATE items SET size=$s, sha256=$h, updated=$n WHERE id=$id", ("$s", size), ("$h", sha), ("$n", Now()), ("$id", id)); }

    public string CreateFolderId(string name, string? parentId = null)
    {
        var id = Guid.NewGuid().ToString();
        using var c = Open();
        Exec(c, "INSERT INTO folders(id,name,parent_id,created) VALUES($id,$n,$p,$c)", ("$id", id), ("$n", name), ("$p", parentId), ("$c", Now()));
        return id;
    }

    // ---- state sync (rename / trash / move / text edit) ----
    public Item? GetItemBySync(string syncId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM items WHERE sync_id=$s LIMIT 1"; cmd.Parameters.AddWithValue("$s", syncId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new Item
        {
            Id = r.GetString(0), Kind = r.GetString(1), Title = r.GetString(2), Text = S(3), FileName = S(4), Mime = S(5),
            Size = r.GetInt64(6), Sha256 = S(7), RelPath = S(8), Source = r.GetString(9), FolderId = S(10),
            Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14), SyncId = S(15),
        };
    }

    /// <summary>All items (trashed included) changed after sinceMs.</summary>
    public List<Item> ChangedItems(long sinceMs)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Cols} FROM items WHERE sync_id IS NOT NULL AND updated > $s"; cmd.Parameters.AddWithValue("$s", sinceMs);
        var list = new List<Item>(); using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            list.Add(new Item
            {
                Id = r.GetString(0), Kind = r.GetString(1), Title = r.GetString(2), Text = S(3), FileName = S(4), Mime = S(5),
                Size = r.GetInt64(6), Sha256 = S(7), RelPath = S(8), Source = r.GetString(9), FolderId = S(10),
                Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14), SyncId = S(15),
            });
        }
        return list;
    }

    /// <summary>folder id -> "A/B/C" for all folders (one query).</summary>
    public Dictionary<string, string> FolderPaths()
    {
        var all = Folders().ToDictionary(f => f.Id);
        var map = new Dictionary<string, string>();
        foreach (var f in all.Values)
        {
            var parts = new List<string>(); var cur = f; int g = 0;
            while (cur != null && g++ < 50) { parts.Insert(0, cur.Name); cur = cur.ParentId != null ? all.GetValueOrDefault(cur.ParentId) : null; }
            map[f.Id] = string.Join("/", parts);
        }
        return map;
    }

    public void SetFolderParent(string id, string? parentId, string name)
    {
        using var c = Open();
        Exec(c, "UPDATE folders SET parent_id=$p, name=$n WHERE id=$id", ("$p", parentId), ("$n", name), ("$id", id));
    }

    /// <summary>Marks items inside the given folders as changed so a folder move reaches the other device.</summary>
    public void TouchItemsInFolders(IEnumerable<string> folderIds)
    {
        var ids = folderIds.ToList();
        if (ids.Count == 0) return;
        using var c = Open(); using var cmd = c.CreateCommand();
        var names = new List<string>();
        for (int i = 0; i < ids.Count; i++) { names.Add("$f" + i); cmd.Parameters.AddWithValue("$f" + i, ids[i]); }
        cmd.Parameters.AddWithValue("$n", Now());
        cmd.CommandText = $"UPDATE items SET updated=$n WHERE folder_id IN ({string.Join(",", names)})";
        cmd.ExecuteNonQuery();
    }
}
