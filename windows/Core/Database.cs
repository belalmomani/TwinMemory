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
    }

    const string Cols = "id,kind,title,text,file_name,mime,size,sha256,rel_path,source,folder_id,favorite,created,updated,trashed";

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
                Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14),
            });
        }
        return list;
    }

    public void Insert(Item i)
    {
        using var c = Open();
        Exec(c, $"INSERT INTO items({Cols}) VALUES($id,$kind,$title,$text,$fn,$mime,$size,$sha,$rel,$src,$fid,$fav,$cr,$up,$tr)",
            ("$id", i.Id), ("$kind", i.Kind), ("$title", i.Title), ("$text", i.Text), ("$fn", i.FileName), ("$mime", i.Mime), ("$size", i.Size),
            ("$sha", i.Sha256), ("$rel", i.RelPath), ("$src", i.Source), ("$fid", i.FolderId), ("$fav", i.Favorite ? 1 : 0),
            ("$cr", i.Created), ("$up", i.Updated), ("$tr", i.Trashed));
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Rename(string id, string title) { using var c = Open(); Exec(c, "UPDATE items SET title=$t, updated=$n WHERE id=$id", ("$t", title), ("$n", Now()), ("$id", id)); }
    public void SetFavorite(string id, bool fav) { using var c = Open(); Exec(c, "UPDATE items SET favorite=$f, updated=$n WHERE id=$id", ("$f", fav ? 1 : 0), ("$n", Now()), ("$id", id)); }
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
        cmd.CommandText = "SELECT id,name FROM folders ORDER BY name COLLATE NOCASE";
        var l = new List<Folder>(); using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(new Folder(r.GetString(0), r.GetString(1)));
        return l;
    }

    public void CreateFolder(string name) { using var c = Open(); Exec(c, "INSERT INTO folders(id,name,created) VALUES($id,$n,$c)", ("$id", Guid.NewGuid().ToString()), ("$n", name), ("$c", Now())); }
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
        Exec(c, "INSERT INTO transfers(id,device_id,direction,kind,title,file_name,mime,size,sha256,state,item_id,error,started,finished) VALUES($id,$d,$dir,$k,$t,$fn,$m,$s,$sha,$st,$it,$e,$sa,$fi)",
            ("$id", t.Id), ("$d", t.DeviceId), ("$dir", t.Direction), ("$k", t.Kind), ("$t", t.Title), ("$fn", t.FileName), ("$m", t.Mime), ("$s", t.Size),
            ("$sha", t.Sha256), ("$st", t.State), ("$it", t.ItemId), ("$e", t.Error), ("$sa", t.Started), ("$fi", t.Finished));
    }

    const string TCols = "t.id,t.device_id,t.direction,t.kind,t.title,t.file_name,t.mime,t.size,t.sha256,t.state,t.item_id,t.error,t.started,t.finished,COALESCE(d.name,'')";
    static Transfer ReadTransfer(SqliteDataReader r)
    {
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new Transfer
        {
            Id = r.GetString(0), DeviceId = r.GetString(1), Direction = r.GetString(2), Kind = r.GetString(3), Title = r.GetString(4),
            FileName = S(5) ?? "", Mime = S(6) ?? "", Size = r.GetInt64(7), Sha256 = S(8) ?? "", State = r.GetString(9), ItemId = S(10), Error = S(11),
            Started = r.GetInt64(12), Finished = r.IsDBNull(13) ? 0 : r.GetInt64(13), DeviceName = r.GetString(14),
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
            Favorite = r.GetInt64(11) == 1, Created = r.GetInt64(12), Updated = r.GetInt64(13), Trashed = r.IsDBNull(14) ? null : r.GetInt64(14),
        };
    }
}
