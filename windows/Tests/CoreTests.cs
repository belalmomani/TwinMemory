using Microsoft.Data.Sqlite;
using TwinMemory.Core;
using Xunit;

namespace TwinMemory.Tests;

public class ClassifyTests
{
    [Fact] public void KindFor_GuessesFromExtension()
    {
        Assert.Equal("IMAGE", Classify.KindFor("a.PNG"));
        Assert.Equal("VIDEO", Classify.KindFor("a.mp4"));
        Assert.Equal("DOCUMENT", Classify.KindFor("a.pdf"));
        Assert.Equal("FILE", Classify.KindFor("a.zip"));
    }

    [Fact] public void IsSingleUrl_OnlyAcceptsOneHttpUrl()
    {
        Assert.True(Classify.IsSingleUrl(" https://example.com/a?b=1 "));
        Assert.False(Classify.IsSingleUrl("see https://example.com"));
        Assert.False(Classify.IsSingleUrl("example.com"));
    }

    [Fact] public void SanitizeFileName_CannotEscapeTheFolder()
    {
        var n = Classify.SanitizeFileName("../../Windows/x.txt");
        Assert.DoesNotContain('/', n);
        Assert.DoesNotContain('\\', n);
        Assert.Equal("file", Classify.SanitizeFileName("..."));
    }

    [Fact] public void LikePattern_EscapesWildcards()
    {
        Assert.Null(Classify.LikePattern("  "));
        Assert.Equal("%50\\%\\_a%", Classify.LikePattern("50%_a"));
    }

    [Fact] public void RiskyExtensions_AreDetected()
    {
        Assert.True(Classify.IsRisky("setup.EXE"));
        Assert.False(Classify.IsRisky("photo.jpg"));
    }
}

public class DatabaseTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "tm-test-" + Guid.NewGuid());
    readonly Database _db;
    readonly FileStore _store;

    public DatabaseTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "t.db"));
        _store = new FileStore(Path.Combine(_dir, "items"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    static Item Text(string title = "t", string source = "pc", long updated = 1000) => new()
    {
        Id = Guid.NewGuid().ToString(), Kind = "TEXT", Title = title, Text = "hello", Size = 5, Source = source, Created = updated, Updated = updated,
    };

    [Fact] public void OpeningTwice_DoesNotFail_AndKeepsData()
    {
        var it = Text();
        _db.Insert(it);
        var again = new Database(Path.Combine(_dir, "t.db")); // migrations must be idempotent
        Assert.Single(again.Query(new Filter()));
    }

    [Fact] public void Insert_AssignsSyncId()
    {
        var it = Text();
        _db.Insert(it);
        Assert.Equal(it.Id, _db.GetItemBySync(it.Id)?.SyncId);
    }

    [Fact] public void SavingNewItem_NeverReplacesOldOnes()
    {
        for (int i = 0; i < 11; i++) _db.Insert(Text("item " + i));
        Assert.Equal(11, _db.Query(new Filter()).Count);
    }

    [Fact] public void Trashed_AreHiddenFromQuery_ButVisibleToStateSync()
    {
        var it = Text();
        _db.Insert(it);
        _db.SetTrashed(it.Id, true);
        Assert.Empty(_db.Query(new Filter()));
        Assert.Single(_db.Query(new Filter { Trashed = true }));
        Assert.Contains(_db.ChangedItems(0), c => c.Id == it.Id && c.Trashed != null);
        _db.SetTrashed(it.Id, false);
        Assert.Single(_db.Query(new Filter()));
    }

    [Fact] public void Favorite_IsNotASyncedChange()
    {
        var it = Text(updated: 1000);
        _db.Insert(it);
        _db.SetFavorite(it.Id, true);
        Assert.Empty(_db.ChangedItems(1000)); // updated stays 1000, so nothing newer than 1000
    }

    [Fact] public void FolderPaths_CreateReuseAndResolve()
    {
        var leaf = _db.EnsureFolderPath("A/B/C");
        Assert.Equal("A/B/C", _db.FolderPath(leaf));
        Assert.Equal(leaf, _db.EnsureFolderPath("a/b/c"));            // case-insensitive reuse
        Assert.Equal(3, _db.Folders().Count);
        Assert.Null(_db.EnsureFolderPath("../.."));                   // traversal segments ignored
    }

    [Fact] public void Search_IsLiteral_NotAWildcard()
    {
        _db.Insert(Text("100% done"));
        _db.Insert(Text("other"));
        Assert.Single(_db.Query(new Filter { Search = "100%" }));
    }

    [Fact] public void AutoOutbox_SkipsPhoneItems_AndAlreadyQueued()
    {
        var pc = Text("pc item", "pc");
        var phone = Text("phone item", "phone");
        _db.Insert(pc); _db.Insert(phone);
        Assert.Equal(new[] { pc.Id }, _db.ItemsNeedingOutbox("dev1", 500L * 1024 * 1024).Select(i => i.Id).ToArray());
        _db.InsertTransfer(new Transfer { Id = Guid.NewGuid().ToString(), DeviceId = "dev1", Direction = "out", Kind = "TEXT", Title = "x", State = "queued", ItemId = pc.Id, Started = 1 });
        Assert.Empty(_db.ItemsNeedingOutbox("dev1", 500L * 1024 * 1024));
    }

    [Fact] public void FolderMove_RejectsCycles_AndRenamesClashes()
    {
        var ops = new FolderOps(_db, _store);
        var a = _db.EnsureFolderPath("A")!;
        var b = _db.EnsureFolderPath("A/B")!;
        Assert.Equal("cycle", ops.Move(a, b));          // into its own sub-folder
        Assert.Equal("cycle", ops.Move(a, a));          // into itself
        var other = _db.EnsureFolderPath("X")!;
        var clash = _db.EnsureFolderPath("X/B")!;
        Assert.Null(ops.Move(b, other));                 // X already has a "B": becomes "B (2)"
        var names = _db.Folders().Where(f => f.ParentId == other).Select(f => f.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "B", "B (2)" }, names);
        _ = clash;
    }

    [Fact] public void FolderCopy_DuplicatesItemsAndFiles()
    {
        var ops = new FolderOps(_db, _store);
        var f = _db.EnsureFolderPath("Src/Sub")!;
        var id = Guid.NewGuid().ToString();
        using (var ms = new MemoryStream(new byte[] { 1, 2, 3, 4 })) _store.Store(id, "a.bin", ms);
        _db.Insert(new Item { Id = id, Kind = "FILE", Title = "a.bin", FileName = "a.bin", Size = 4, RelPath = id + "/a.bin", Source = "pc", FolderId = f, Created = 1, Updated = 1 });
        var src = _db.Folders().First(x => x.Name == "Src");
        Assert.Equal(1, ops.Copy(src.Id, null));
        Assert.Equal(2, _db.Query(new Filter()).Count(i => i.Kind == "FILE"));
        Assert.NotEqual(id, _db.Query(new Filter()).First(i => i.Id != id).Id);
        Assert.True(File.Exists(_store.PathFor(id + "/a.bin"))); // the original is untouched
    }
}
