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
}
