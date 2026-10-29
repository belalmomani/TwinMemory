using System.IO;
using System.Windows;
using TwinMemory.Core;

namespace TwinMemory;

public partial class App : Application
{
    public static Database Db = null!;
    public static FileStore Store = null!;
    public static string DataDir = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // Program files are separate from user data: data lives in %LOCALAPPDATA%\TwinMemory and survives updates.
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinMemory");
            Directory.CreateDirectory(DataDir);
            Db = new Database(Path.Combine(DataDir, "twinmemory.db"));
            Store = new FileStore(Path.Combine(DataDir, "items"));
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TwinMemory");
            Shutdown(1);
        }
    }
}
