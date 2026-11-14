using System;
using System.IO;
using System.Linq;

namespace Ibob;

public partial class App : System.Windows.Application
{
    static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Where ibob keeps its shelf, history, settings and logs: %LOCALAPPDATA%\ibob
    public static readonly string DataFolder = Path.Combine(LocalAppData, "ibob");

    static readonly string ErrorLog = Path.Combine(DataFolder, "errors.log");

    static System.Threading.Mutex oneCopy;   // held while ibob runs, so a second launch just exits

    // ibob.exe --uninstall   : remove ibob (Settings → Apps uses this); add --quiet to skip the questions
    // ibob.exe --install     : install without asking (single-file release only)
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        // ibob.exe --screenshots <folder> : save sample-data screenshots of every screen, then quit
        if (e.Args.Length >= 2 && e.Args[0] == "--screenshots") { TakeScreenshots(e.Args[1]); return; }

        bool ask = !e.Args.Contains("--quiet");
        if (e.Args.Contains("--uninstall")) { Installer.Uninstall(ask); Shutdown(); return; }
        if (Installer.OfferInstall(ask: !e.Args.Contains("--install"))) { Shutdown(); return; }

        oneCopy = new System.Threading.Mutex(true, "ibob-one-copy", out bool first);
        if (!first) { Shutdown(); return; }   // ibob is already running

        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;   // closing the pill closes Settings too
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    // A hidden, off-screen window filled with sample data (see MainWindow.Demo.cs).
    async void TakeScreenshots(string folder)
    {
        var window = new MainWindow(demo: true) { Left = -3000, Top = -3000, ShowActivated = false };
        window.Show();
        try { await window.TakeScreenshots(folder); }
        finally { Shutdown(); }
    }

    public App()
    {
        MoveOldData();

        // Safety net: an unexpected error gets written to errors.log instead of closing ibob.
        DispatcherUnhandledException += (_, e) =>
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(ErrorLog, $"{DateTime.Now}\n{e.Exception}\n\n");
            e.Handled = true;
        };
    }

    // ibob was earlier called "PipIsland" and "Island": carry that data over once, fixing saved shelf paths too.
    static void MoveOldData()
    {
        if (Directory.Exists(DataFolder)) return;
        string old = new[] { "PipIsland", "Island" }.Select(name => Path.Combine(LocalAppData, name)).FirstOrDefault(Directory.Exists);
        if (old == null) return;

        Directory.Move(old, DataFolder);
        string shelfList = Path.Combine(DataFolder, "shelf.txt");
        if (File.Exists(shelfList)) File.WriteAllText(shelfList, File.ReadAllText(shelfList).Replace(old, DataFolder));
    }
}
