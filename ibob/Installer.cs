using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Ibob;

// The release is a single ibob.exe. Run from anywhere (e.g. Downloads), it offers to install itself for your
// Windows account only (no admin needed): copy to %LOCALAPPDATA%\Programs\ibob, add Start menu and
// start-with-Windows shortcuts, and register in Settings → Apps so it can be uninstalled like any app.
static class Installer
{
    static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ibob");
    static readonly string Exe = Path.Combine(Folder, "ibob.exe");
    static readonly string StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ibob.lnk");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ibob";

    public static string Version => typeof(Installer).Assembly.GetName().Version.ToString(3);
    public static bool IsInstalled => string.Equals(Path.GetDirectoryName(Environment.ProcessPath), Folder, StringComparison.OrdinalIgnoreCase);

    // A single-file build has no separate .dll on disk; developer builds do, and never offer to install.
    static bool IsSingleFile => string.IsNullOrEmpty(typeof(Installer).Assembly.Location);

    // Returns true if ibob was installed (and started from its new home), so this copy should quit.
    public static bool OfferInstall(bool ask)
    {
        if (IsInstalled || !IsSingleFile) return false;
        if (ask && MessageBox.Show(
                $"Install ibob {Version} for your account?\n\n" +
                "It goes into your user folder (no admin needed), starts with Windows, and shows up in the Start menu " +
                "and in Settings → Apps for uninstalling.\n\nChoose No to just run it this once.",
                "ibob", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;

        StopOtherCopies();
        if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);   // replaces any older version
        Directory.CreateDirectory(Folder);
        File.Copy(Environment.ProcessPath, Exe);

        Shortcut(StartMenuLink, Exe);
        Startup.Set(true, Exe);
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "ibob");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "ibob");
            key.SetValue("DisplayIcon", Exe);
            key.SetValue("InstallLocation", Folder);
            key.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
            key.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024));   // in KB
        }

        Process.Start(Exe);
        return true;
    }

    // Started by Settings → Apps → Uninstall (or the button in ibob's Settings window).
    public static void Uninstall(bool ask)
    {
        if (ask && MessageBox.Show("Uninstall ibob?", "ibob", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool deleteData = ask && MessageBox.Show(
            $"Also delete your shelf, notes, tasks, clipboard history and settings?\n\n(They're in {App.DataFolder})",
            "ibob", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        StopOtherCopies();
        File.Delete(StartMenuLink);
        Startup.Set(false, null);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (deleteData && Directory.Exists(App.DataFolder)) Directory.Delete(App.DataFolder, recursive: true);

        // This exe is running from the install folder, so a short command removes the folder once ibob has exited.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak > nul & rmdir /s /q \"{Folder}\"")
            { CreateNoWindow = true, UseShellExecute = false });
    }

    static void StopOtherCopies()
    {
        foreach (var other in Process.GetProcessesByName("ibob").Where(p => p.Id != Environment.ProcessId))
            try { other.Kill(); other.WaitForExit(3000); } catch { }
    }

    public static void Shortcut(string link, string target)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.Save();
    }
}

// The "start with Windows" shortcut in your Startup folder.
static class Startup
{
    static readonly string Link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ibob.lnk");

    // Only the installed copy changes it, so a test copy run from elsewhere never points Windows at the wrong ibob.exe.
    public static void Set(bool on)
    {
        if (Installer.IsInstalled) Set(on, Environment.ProcessPath);
    }

    public static void Set(bool on, string exe)
    {
        if (on) Installer.Shortcut(Link, exe);
        else File.Delete(Link);
    }
}
