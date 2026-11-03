using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using TwinMemory.Core;

namespace TwinMemory;

public class NavEntry
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public int Level { get; set; }
    public Thickness Pad => S.Ar ? new Thickness(12, 7, 12 + Level * 18, 7) : new Thickness(12 + Level * 18, 7, 12, 7);
}

public partial class MainWindow : Window
{
    readonly Database _db = App.Db;
    readonly FileStore _store = App.Store;
    readonly FolderOps _ops = new(App.Db, App.Store);
    List<Item> _items = new();
    List<Folder> _folders = new();
    string _navKey = "zone:all";
    bool _details, _building;
    SortBy _sort = SortBy.Date;

    ListView Active => _details ? DetailsList : IconList;
    List<Item> Selected() => Active.SelectedItems.Cast<Item>().ToList();
    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    string? CurrentFolder() => _navKey.StartsWith("folder:") ? _navKey[7..] : null;
    void Status(string m) => MsgText.Text = m;

    public MainWindow()
    {
        InitializeComponent();
        FlowDirection = S.Ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Title = S.Ar ? "TwinMemory — الذاكرة المشتركة" : "TwinMemory";
        BuildNav();
        Refresh();
        App.Server.DataChanged += () => Dispatcher.BeginInvoke(new Action(() => { Refresh(); UpdatePhone(); }));
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) => UpdatePhone();
        t.Start();
        UpdatePhone();
        AutoStartItem.IsChecked = IsAutoStart();
        Loaded += async (_, _) => await AutoCheckUpdateAsync();
        App.Mirror.Changed += () => Dispatcher.BeginInvoke(new Action(Refresh));
        App.Mirror.Message += m => Dispatcher.BeginInvoke(new Action(() => Status(m)));
    }

    void UpdatePhone()
    {
        var devs = App.Db.Devices();
        if (App.ServerError != null) PhoneText.Text = S.T("تعذّر تشغيل خادم الربط");
        else if (devs.Count == 0) PhoneText.Text = S.T("الهاتف: غير مرتبط");
        else if ((DateTime.UtcNow - App.Server.LastSeenUtc).TotalSeconds < 45) PhoneText.Text = S.T("الهاتف: متصل") + " (" + App.Server.LastDeviceName + ")";
        else PhoneText.Text = S.T("الهاتف: مرتبط — غير متصل");
    }

    void BuildNav()
    {
        _folders = _db.Folders();
        var l = new List<NavEntry>
        {
            new() { Key = "zone:all", Title = "☁️ TwinMemory" },
            new() { Key = "cat:text", Title = "📝 " + S.T("النصوص"), Level = 1 },
            new() { Key = "cat:link", Title = "🔗 " + S.T("الروابط"), Level = 1 },
            new() { Key = "cat:image", Title = "🖼️ " + S.T("الصور"), Level = 1 },
            new() { Key = "cat:file", Title = "📎 " + S.T("الملفات"), Level = 1 },
            new() { Key = "zone:c", Title = "📱 " + S.T("هاتف C") },
            new() { Key = "zone:d", Title = "🖥️ " + S.T("كمبيوتر D") },
        };
        AddFolders(l, null, 0);
        l.Add(new NavEntry { Key = "fav", Title = "⭐ " + S.T("المفضلة") });
        l.Add(new NavEntry { Key = "trash", Title = "🗑️ " + S.T("سلة المحذوفات") });
        _building = true;
        Nav.ItemsSource = l;
        var sel = l.FirstOrDefault(x => x.Key == _navKey) ?? l[0];
        _navKey = sel.Key;
        Nav.SelectedItem = sel;
        _building = false;
    }

    void AddFolders(List<NavEntry> l, string? parent, int level)
    {
        if (level > 20) return;
        var kids = _folders.Where(x => x.ParentId == parent || (parent == null && x.ParentId != null && !_folders.Any(y => y.Id == x.ParentId)))
                           .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var f in kids)
        {
            l.Add(new NavEntry { Key = "folder:" + f.Id, Title = "📁 " + f.Name, Level = level });
            AddFolders(l, f.Id, level + 1);
        }
    }

    Filter MakeFilter()
    {
        var f = new Filter { Search = Search.Text };
        switch (_navKey)
        {
            case "cat:text": f.Kinds = new[] { "TEXT" }; break;
            case "cat:link": f.Kinds = new[] { "LINK" }; break;
            case "cat:image": f.Kinds = new[] { "IMAGE" }; break;
            case "cat:file": f.Kinds = new[] { "FILE", "DOCUMENT", "VIDEO" }; break;
            case "zone:c": f.Source = "phone"; break;
            case "zone:d": f.Source = "pc"; break;
            case "fav": f.FavOnly = true; break;
            case "trash": f.Trashed = true; break;
            default:
                if (_navKey.StartsWith("folder:")) { f.UseFolder = true; f.FolderId = _navKey[7..]; }
                break;
        }
        return f;
    }

    void Refresh()
    {
        try { _items = _db.Query(MakeFilter()); }
        catch (Exception ex) { Status(ex.Message); _items = new(); }
        IEnumerable<Item> sorted = _sort switch
        {
            SortBy.Name => _items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
            SortBy.Size => _items.OrderByDescending(i => i.Size),
            SortBy.Type => _items.OrderBy(i => i.Kind).ThenBy(i => i.Title),
            _ => _items.OrderByDescending(i => i.Created),
        };
        _items = sorted.ToList();
        IconList.ItemsSource = _items;
        DetailsList.ItemsSource = _items;
        IconList.Visibility = _details ? Visibility.Collapsed : Visibility.Visible;
        DetailsList.Visibility = _details ? Visibility.Visible : Visibility.Collapsed;
        var fresh = string.IsNullOrWhiteSpace(Search.Text) && _navKey == "zone:all";
        Empty.Text = S.T(fresh ? "لا توجد عناصر بعد. اضغط + جديد، أو الصق (Ctrl+V)، أو اسحب ملفات إلى النافذة." : "لا توجد عناصر هنا.");
        Empty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Crumb.Text = "TwinMemory ‹ " + ((Nav.SelectedItem as NavEntry)?.Title ?? "");
        UpdateStatus();
        App.Mirror?.Trigger(); // keep the Explorer folder in step with what the app shows
    }

    void UpdateStatus()
    {
        var n = Active.SelectedItems.Count;
        CountText.Text = $"{_items.Count} {S.T("عناصر")}" + (n > 0 ? $"   |   {S.T("تم تحديد")} {n}" : "");
    }

    // ---- saving ----
    void SaveText(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return;
        var link = Classify.IsSingleUrl(t);
        var now = Now();
        try
        {
            _db.Insert(new Item
            {
                Id = Guid.NewGuid().ToString(), Kind = link ? "LINK" : "TEXT", Title = link ? Classify.HostOf(t) : Classify.TitleFor(t),
                Text = t, Mime = "text/plain", Size = Encoding.UTF8.GetByteCount(t), Source = "pc", FolderId = CurrentFolder(), Created = now, Updated = now,
            });
            Status(S.T("تم الحفظ"));
        }
        catch { Status(S.T("تعذّر الحفظ. عناصرك السابقة سليمة.")); }
        Refresh();
    }

    record Imported(bool Dup, string Sha, long Size, string Rel);

    Imported ImportOne(string path, string? folder)
    {
        var id = Guid.NewGuid().ToString();
        var name = Path.GetFileName(path);
        FileStore.Stored st;
        using (var fs = File.OpenRead(path)) st = _store.Store(id, name, fs);
        var dup = _db.CountSha(st.Sha256) > 0;
        var now = Now();
        try
        {
            _db.Insert(new Item
            {
                Id = id, Kind = Classify.KindFor(name), Title = name, FileName = name, Size = st.Size, Sha256 = st.Sha256, RelPath = st.RelPath,
                Source = "pc", FolderId = folder, Created = now, Updated = now,
            });
        }
        catch { _store.Delete(id); throw; }
        return new Imported(dup, st.Sha256, st.Size, st.RelPath);
    }

    static string? HashOf(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch { return null; }
    }

    /// <summary>Move mode: the original goes to the Recycle Bin only if it is exactly what was stored and the stored copy is intact.</summary>
    bool RemoveOriginal(string path, Imported r)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length != r.Size || HashOf(path) != r.Sha) return false;
            var stored = _store.PathFor(r.Rel);
            if (stored == null || new FileInfo(stored).Length != r.Size) return false;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Imports a whole directory tree: every sub-folder becomes a folder in the app, every file an item inside it. move = remove verified originals.</summary>
    (int Count, bool Dup, bool Fail, int Removed) ImportTree(string dir, string? baseFolder, bool move)
    {
        int n = 0, removed = 0; bool dup = false, fail = false;
        string Join(string a, string b) => string.IsNullOrEmpty(b) ? a : string.IsNullOrEmpty(a) ? b : a + "/" + b;
        var start = Join(_db.FolderPath(baseFolder) ?? "", new DirectoryInfo(dir).Name);
        _db.EnsureFolderPath(start);
        var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var d in Directory.EnumerateDirectories(dir, "*", opt))
            _db.EnsureFolderPath(Join(start, Path.GetRelativePath(dir, d).Replace('\\', '/')));
        foreach (var f in Directory.EnumerateFiles(dir, "*", opt))
        {
            var name = Path.GetFileName(f);
            if (name.StartsWith("~$") || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
            var rel = (Path.GetDirectoryName(Path.GetRelativePath(dir, f)) ?? "").Replace('\\', '/');
            var folderId = _db.EnsureFolderPath(Join(start, rel));
            try
            {
                var r = ImportOne(f, folderId);
                dup |= r.Dup; n++;
                if (move && RemoveOriginal(f, r)) removed++;
            }
            catch { fail = true; }
        }
        if (move)
        {
            // only directories that are now completely empty are removed (deepest first)
            foreach (var d in Directory.EnumerateDirectories(dir, "*", opt).OrderByDescending(x => x.Length))
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        }
        return (n, dup, fail, removed);
    }

    async void ImportPaths(List<string> paths, bool move = false)
    {
        int ok = 0, removed = 0; bool dup = false, fail = false;
        var folder = CurrentFolder();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                Status(S.T("جارٍ استيراد المجلد…"));
                var r = await Task.Run(() => ImportTree(p, folder, move));
                ok += r.Count; dup |= r.Dup; fail |= r.Fail; removed += r.Removed;
                BuildNav();
                continue;
            }
            if (!File.Exists(p)) continue;
            try
            {
                var r = await Task.Run(() =>
                {
                    var imp = ImportOne(p, folder);
                    return (imp, removed: move && RemoveOriginal(p, imp));
                });
                dup |= r.imp.Dup; ok++;
                if (r.removed) removed++;
            }
            catch { fail = true; } // e.g. disk full: nothing existing is touched
        }
        Refresh();
        if (fail) Status(S.T("تعذّر الحفظ. عناصرك السابقة سليمة."));
        else if (move && ok > 0) Status(removed == ok ? S.T("تم النقل — نُقلت الأصول إلى سلة Windows بعد التحقق") : S.T("بعض الأصول لم تُحذف (تغيّرت أو مقفلة) فبقيت كما هي"));
        else if (dup) Status(S.T("تم الحفظ — يشبه عنصرًا محفوظًا مسبقًا"));
        else if (ok > 0) Status(S.T("تم الحفظ"));
    }

    async void CopyFolderAsync(string folderId, string? parent)
    {
        Status(S.T("جارٍ نسخ المجلد…"));
        var n = await Task.Run(() => _ops.Copy(folderId, parent));
        BuildNav(); Refresh();
        Status(S.T("تم نسخ المجلد") + $" ({n})");
    }

    // ---- actions ----
    void CopySel()
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        try
        {
            var files = sel.Select(i => _store.PathFor(i.RelPath)).Where(p => p != null).Cast<string>().ToArray();
            if (files.Length > 0) { var sc = new StringCollection(); sc.AddRange(files); Clipboard.SetFileDropList(sc); }
            else
            {
                var t = string.Join(Environment.NewLine, sel.Where(i => i.Text != null).Select(i => i.Text));
                if (t.Length > 0) Clipboard.SetText(t);
            }
        }
        catch (Exception ex) { Status(ex.Message); }
    }

    void Paste()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                // Explorer "Cut" (move) vs "Copy" is honoured: cut files are moved in, copied files are copied in.
                bool cut = false;
                try
                {
                    if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms) { var b = new byte[4]; if (ms.Read(b, 0, 4) == 4) cut = (BitConverter.ToInt32(b, 0) & 2) != 0; }
                }
                catch { }
                ImportPaths(Clipboard.GetFileDropList().Cast<string>().ToList(), cut);
                return;
            }
            if (Clipboard.ContainsText()) { SaveText(Clipboard.GetText()); return; }
        }
        catch { }
        Status(S.T("لا يوجد ما يمكن لصقه (نص أو ملفات)."));
    }

    void OpenItem(Item i)
    {
        try
        {
            if (i.Kind == "LINK" && i.Text != null && Classify.IsSingleUrl(i.Text))
            {
                Process.Start(new ProcessStartInfo(i.Text.Trim()) { UseShellExecute = true });
                return;
            }
            var path = _store.PathFor(i.RelPath);
            if (path != null)
            {
                if (Classify.IsRisky(path) && !Dlg.Confirm(this, S.T("هذا النوع من الملفات قد ينفّذ برنامجًا. هل تريد فتحه فعلًا؟"))) return;
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
            if (i.Text != null) { Dlg.Info(this, i.Text); return; }
        }
        catch { }
        Status(S.T("لا يمكن فتح هذا العنصر."));
    }

    void Run(string tag)
    {
        var sel = Selected();
        switch (tag)
        {
            case "newtext": { var t = Dlg.Input(this, S.T("النص أو الرابط"), "", true); if (t != null) SaveText(t); break; }
            case "import":
            {
                var d = new OpenFileDialog { Multiselect = true };
                if (d.ShowDialog(this) == true) { var mode = Dlg.ImportMode(this); if (mode != null) ImportPaths(d.FileNames.ToList(), mode.Value); }
                break;
            }
            case "newfolder":
            {
                var n = Dlg.Input(this, S.T("اسم المجلد"));
                if (n != null) { _db.CreateFolder(n.Trim(), CurrentFolder()); BuildNav(); Refresh(); }
                break;
            }
            case "importfolder":
            {
                var fd = new OpenFolderDialog();
                if (fd.ShowDialog(this) == true) { var mode = Dlg.ImportMode(this); if (mode != null) ImportPaths(new List<string> { fd.FolderName }, mode.Value); }
                break;
            }
            case "renamefolder":
            {
                var fid = CurrentFolder();
                if (fid == null) break;
                var nn = Dlg.Input(this, S.T("اسم المجلد"), _folders.FirstOrDefault(x => x.Id == fid)?.Name ?? "");
                if (nn != null) { _db.RenameFolder(fid, nn.Trim()); BuildNav(); Refresh(); }
                break;
            }
            case "movefolder":
            case "copyfolder":
            {
                var fid = CurrentFolder();
                if (fid == null) break;
                var all = _db.Folders();
                var exclude = FolderOps.Subtree(all, fid); // never into itself or one of its sub-folders
                var paths = _db.FolderPaths();
                var opts = new List<Choice> { new(null, S.T("بدون مجلد (الجذر)")) };
                opts.AddRange(all.Where(x => !exclude.Contains(x.Id)).Select(x => new Choice(x.Id, paths.GetValueOrDefault(x.Id) ?? x.Name)).OrderBy(c => c.Name));
                var pick = Dlg.Pick(this, S.T(tag == "movefolder" ? "نقل المجلد إلى…" : "نسخ المجلد إلى…"), opts);
                if (pick == null) break;
                if (tag == "movefolder")
                {
                    var err = _ops.Move(fid, pick.Id);
                    BuildNav(); Refresh();
                    Status(err == null ? S.T("تم نقل المجلد") : S.T("لا يمكن نقل المجلد إلى نفسه أو إلى أحد مجلداته الفرعية"));
                }
                else CopyFolderAsync(fid, pick.Id);
                break;
            }
            case "selall": Active.SelectAll(); break;
            case "copy": CopySel(); break;
            case "paste": Paste(); break;
            case "open": if (sel.Count > 0) OpenItem(sel[0]); break;
            case "rename":
                if (sel.Count == 1) { var t = Dlg.Input(this, S.T("الاسم الجديد"), sel[0].Title); if (t != null) { _db.Rename(sel[0].Id, t.Trim()); Refresh(); } }
                break;
            case "fav":
                if (sel.Count > 0) { var to = !sel.All(i => i.Favorite); foreach (var i in sel) _db.SetFavorite(i.Id, to); Refresh(); }
                break;
            case "move":
                if (sel.Count > 0)
                {
                    var opts = new List<Choice> { new(null, S.T("بدون مجلد")) };
                    opts.AddRange(_folders.Select(f => new Choice(f.Id, _db.FolderPath(f.Id) ?? f.Name)).OrderBy(c => c.Name));
                    var c = Dlg.Pick(this, S.T("نقل إلى"), opts);
                    if (c != null) { foreach (var i in sel) _db.Move(i.Id, c.Id); Refresh(); }
                }
                break;
            case "sendphone":
            {
                if (sel.Count == 0) break;
                var devs = _db.Devices();
                if (devs.Count == 0) { Dlg.Info(this, S.T("اربط الهاتف أولًا (أدوات ← ربط الهاتف…)")); break; }
                var target = devs[0];
                if (devs.Count > 1)
                {
                    var pick = Dlg.Pick(this, S.T("ربط الهاتف"), devs.Select(x => new Choice(x.Id, x.Name)));
                    if (pick == null) break;
                    target = devs.First(x => x.Id == pick.Id);
                }
                var queued = _db.ListOutbox(target.Id).Select(x => x.ItemId).ToHashSet();
                int added = 0;
                foreach (var it in sel)
                {
                    if (queued.Contains(it.Id)) continue; // already waiting for this phone
                    if (it.Text == null && _store.PathFor(it.RelPath) == null) continue;
                    _db.InsertTransfer(new Transfer
                    {
                        Id = Guid.NewGuid().ToString(), DeviceId = target.Id, Direction = "out", Kind = it.Kind, Title = it.Title,
                        FileName = it.FileName ?? "", Mime = it.Mime ?? "", Size = it.Size, Sha256 = it.Sha256 ?? "", State = "queued", ItemId = it.Id, Started = Now(),
                    });
                    added++;
                }
                if (added > 0) Status(S.T("أُضيف إلى قائمة النقل — يصل الهاتف عند فتح TwinMemory عليه"));
                break;
            }
            case "delete":
                if (sel.Count == 0) break;
                if (_navKey == "trash") { Run("purge"); break; }
                foreach (var i in sel) _db.SetTrashed(i.Id, true);
                Status(S.T("نُقل إلى سلة المحذوفات")); Refresh();
                break;
            case "restore":
                foreach (var i in sel) _db.SetTrashed(i.Id, false);
                Refresh();
                break;
            case "purge": // permanent deletion: explicit, confirmed
                if (sel.Count > 0 && Dlg.Confirm(this, S.T("سيُحذف العنصر وملفه نهائيًا من هذا الجهاز ولا يمكن التراجع. متابعة؟")))
                { foreach (var i in sel) { _db.Delete(i.Id); _store.Delete(i.Id); } Refresh(); }
                break;
            case "props":
                if (sel.Count > 0)
                {
                    var i = sel[0];
                    Dlg.Info(this, $"{i.Title}\n\n{S.T("النوع")}: {i.KindLabel}\n{S.T("المصدر")}: {i.SourceLabel}\n{S.T("الحجم")}: {i.SizeText}\n{S.T("تاريخ الحفظ")}: {i.DateText}\n{S.T("المكان")}: {_store.PathFor(i.RelPath) ?? "—"}");
                }
                break;
            case "view:icons": _details = false; Refresh(); break;
            case "view:details": _details = true; Refresh(); break;
            case "sort:name": _sort = SortBy.Name; Refresh(); break;
            case "sort:date": _sort = SortBy.Date; Refresh(); break;
            case "sort:size": _sort = SortBy.Size; Refresh(); break;
            case "sort:type": _sort = SortBy.Type; Refresh(); break;
            case "refresh": BuildNav(); Refresh(); break;
            case "trashview": _navKey = "trash"; BuildNav(); Refresh(); break;
            case "close": Close(); break;
            case "pair": new PairWindow(this).Show(); break;
            case "update": new UpdateWindow(this).Show(); break;
            case "transfers": new TransfersWindow(this).Show(); break;
            case "openfolder":
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.Mirror.Root}\"") { UseShellExecute = true }); } catch { Status(App.Mirror.Root); }
                break;
            case "syncnow": App.Mirror.Trigger(100); break;
            case "lang:ar": case "lang:en": case "lang:auto":
                if (Dlg.Confirm(this, S.T("ستُعاد تشغيل التطبيق لتطبيق اللغة. متابعة؟")))
                {
                    Settings.SaveLanguage(tag[5..]);
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--restart {Environment.ProcessId}") { UseShellExecute = true });
                    Application.Current.Shutdown();
                }
                break;
            case "help": Dlg.Info(this, S.T("• + جديد ← حفظ نص/رابط أو استيراد ملف.\n• Ctrl+V للصق ما نسخته (نص أو ملفات)، أو اسحب الملفات إلى النافذة.\n• زر الفأرة الأيمن على عنصر: نسخ، إعادة تسمية، نقل، مفضلة.\n• المحذوفات تبقى في السلة حتى تحذفها نهائيًا.\n• ربط الهاتف والنقل بين الجهازين: المرحلة 4.")); break;
            case "about":
                Dlg.Info(this, $"TwinMemory — الذاكرة المشتركة\n{S.T("الإصدار")}: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}\n{S.T("مجلد البيانات")}: {App.DataDir}\n{S.T("مجلد المزامنة")}: {App.Mirror.Root}\n\n{S.T("بياناتك محفوظة على هذا الكمبيوتر فقط ولا تُرفع إلى أي خدمة سحابية.")}");
                break;
        }
    }

    // ---- events ----
    /// <summary>Silent once-a-day check; only tells you in the status bar (once per version).</summary>
    async Task AutoCheckUpdateAsync()
    {
        if (!Settings.ShouldCheckUpdates()) return;
        var (info, _) = await Update.UpdateService.CheckAsync();
        Settings.MarkUpdateChecked();
        if (info != null && Settings.ShouldNotify(info.Version)) Status(S.T("يتوفر إصدار جديد") + " " + info.Version + " — " + S.T("مساعدة") + " ← " + S.T("التحقق من التحديثات…"));
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static bool IsAutoStart() { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("TwinMemory") != null; }
    static void SetAutoStart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (on) k.SetValue("TwinMemory", $"\"{Environment.ProcessPath}\" --minimized"); else k.DeleteValue("TwinMemory", false);
        }
        catch { }
    }

    void Cmd(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: "autostart" } mi) { SetAutoStart(mi.IsChecked); return; }
        if ((sender as FrameworkElement)?.Tag is string t) Run(t);
    }

    void Nav_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_building || Nav.SelectedItem is not NavEntry n) return;
        _navKey = n.Key;
        Refresh();
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) Refresh();
    }

    void List_DoubleClick(object sender, MouseButtonEventArgs e) { if (Selected().Count > 0) OpenItem(Selected()[0]); }
    void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStatus();

    void NewBtn_Click(object sender, RoutedEventArgs e) => ShowMenu("NewMenu", (UIElement)sender);
    void ViewBtn_Click(object sender, RoutedEventArgs e) => ShowMenu("ViewMenu", (UIElement)sender);
    void ShowMenu(string key, UIElement target)
    {
        var m = (ContextMenu)FindResource(key);
        m.PlacementTarget = target;
        m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        m.IsOpen = true;
    }

    void Nav_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Nav, e.OriginalSource as DependencyObject) is ListBoxItem li) li.IsSelected = true;
    }

    void Nav_MenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!_navKey.StartsWith("folder:")) e.Handled = true; // the folder menu only appears on a custom folder
    }

    void ItemMenu_Opened(object sender, RoutedEventArgs e)
    {
        var trash = _navKey == "trash";
        foreach (var o in ((ContextMenu)sender).Items)
        {
            var show = o is MenuItem { Tag: string t }
                ? (trash ? t is "restore" or "purge" or "props" : t is not ("restore" or "purge"))
                : true;
            if (o is UIElement u) u.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var mode = Dlg.ImportMode(this);
            if (mode != null) ImportPaths(files.ToList(), mode.Value);
        }
        else if (e.Data.GetDataPresent(DataFormats.UnicodeText) && e.Data.GetData(DataFormats.UnicodeText) is string s) SaveText(s);
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return; // never hijack text editing
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        string? tag = null;
        if (ctrl && shift && e.Key == Key.N) tag = "newfolder";
        else if (ctrl && e.Key == Key.A) tag = "selall";
        else if (ctrl && e.Key == Key.C) tag = "copy";
        else if (ctrl && e.Key == Key.V) tag = "paste";
        else if (e.Key == Key.F2) tag = "rename";
        else if (e.Key == Key.Delete) tag = "delete";
        if (tag != null) { Run(tag); e.Handled = true; }
    }
}
