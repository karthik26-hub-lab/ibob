using System;
using System.IO;

namespace Pip;

public partial class App : System.Windows.Application
{
    // Where Pip keeps its shelf, settings and logs: %LOCALAPPDATA%\PipIsland
    // (not "Pip": Python's pip installer already keeps its cache in %LOCALAPPDATA%\pip)
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PipIsland");

    static readonly string ErrorLog = Path.Combine(DataFolder, "errors.log");

    public App()
    {
        MoveOldData();

        // Safety net: an unexpected error gets written to errors.log instead of closing Pip.
        DispatcherUnhandledException += (_, e) =>
        {
            Directory.CreateDirectory(DataFolder);
            File.AppendAllText(ErrorLog, $"{DateTime.Now}\n{e.Exception}\n\n");
            e.Handled = true;
        };
    }

    // Pip used to be called "Island": carry its data over once, fixing saved shelf paths too.
    static void MoveOldData()
    {
        string old = Path.Combine(Path.GetDirectoryName(DataFolder), "Island");
        if (!Directory.Exists(old) || Directory.Exists(DataFolder)) return;

        Directory.Move(old, DataFolder);
        string shelfList = Path.Combine(DataFolder, "shelf.txt");
        if (File.Exists(shelfList)) File.WriteAllText(shelfList, File.ReadAllText(shelfList).Replace(old, DataFolder));
    }
}
