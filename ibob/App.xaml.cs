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
