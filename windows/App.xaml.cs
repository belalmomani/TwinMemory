using System.Diagnostics;
using System.IO;
using System.Windows;
using TwinMemory.Core;
using TwinMemory.Net;
using TwinMemory.Sync;

namespace TwinMemory;

public partial class App : Application
{
    public static Database Db = null!;
    public static FileStore Store = null!;
    public static TwinServer Server = null!;
    public static SyncEngine Mirror = null!;
    public static string DataDir = "";
    public static string? ServerError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // After a language change the new process waits for the old one so the server port is free.
            var i = Array.IndexOf(e.Args, "--restart");
            if (i >= 0 && i + 1 < e.Args.Length && int.TryParse(e.Args[i + 1], out var pid))
            {
                try { Process.GetProcessById(pid).WaitForExit(8000); } catch { }
            }
            // Program files are separate from user data: data lives in %LOCALAPPDATA%\TwinMemory and survives updates.
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinMemory");
            Directory.CreateDirectory(DataDir);
            Settings.Load(DataDir);
            Db = new Database(Path.Combine(DataDir, "twinmemory.db"));
            Store = new FileStore(Path.Combine(DataDir, "items"));
            Server = new TwinServer(Db, Store, DataDir);
            Mirror = new SyncEngine(Db, Store, DataDir);
            Server.DataChanged += () => Mirror.Trigger();
            new MainWindow().Show();
            Mirror.Start();
            _ = Task.Run(async () => { try { await Server.StartAsync(); } catch (Exception ex) { ServerError = ex.Message; } });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TwinMemory");
            Shutdown(1);
        }
    }
}
