using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace Ibob;

// Settings window: every change is saved and applied to the island straight away (no Save button needed),
// except calendar links, which have their own Save button since they're downloaded.
public partial class SettingsWindow : Window
{
    readonly MainWindow main;

    public SettingsWindow(MainWindow main, Settings settings)
    {
        InitializeComponent();
        this.main = main;

        VersionText.Text = $"Version {Installer.Version}" + (Installer.IsInstalled ? "" : " · running as a portable copy (not installed)");

        Toggle(StartWithWindows, settings.StartWithWindows, on => settings.StartWithWindows = on);
        Toggle(Sounds, settings.Sounds, on => settings.Sounds = on);
        Toggle(AutoShelf, settings.AutoShelf, on => settings.AutoShelf = on);
        Toggle(TextToShelf, settings.TextToShelf, on => settings.TextToShelf = on);
        Toggle(ClipboardHistory, settings.ClipboardHistory, on => settings.ClipboardHistory = on);
        StartWithWindows.IsEnabled = Installer.IsInstalled;   // a portable copy can't start with Windows

        foreach (var size in new[] { "Small", "Medium", "Large" })
            SizeChoices.Children.Add(Choice(size, "size", settings.PillSize == size, () => settings.PillSize = size));

        var screens = Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            string label = $"{(screen.Primary ? "Main screen" : $"Screen {i + 1}")} ({screen.Bounds.Width} × {screen.Bounds.Height})";
            bool chosen = screen.DeviceName == settings.Screen || (screen.Primary && !screens.Any(s => s.DeviceName == settings.Screen));
            ScreenChoices.Children.Add(Choice(label, "screen", chosen, () => settings.Screen = screen.Primary ? "" : screen.DeviceName));
        }

        foreach (var name in MainWindow.AllPageNames)
        {
            var box = new CheckBox { Content = name, Width = 135 };
            Toggle(box, !settings.HiddenPages.Contains(name), shown =>
            {
                settings.HiddenPages.Remove(name);
                if (!shown) settings.HiddenPages.Add(name);
            });
            PageChoices.Children.Add(box);
        }

        Calendars.Text = main.CalendarText;
        UninstallButton.Visibility = Installer.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
    }

    // A checkbox that updates one setting and applies it immediately.
    void Toggle(CheckBox box, bool on, Action<bool> set)
    {
        box.IsChecked = on;
        box.Checked += (_, _) => { set(true); main.ApplySettings(); };
        box.Unchecked += (_, _) => { set(false); main.ApplySettings(); };
    }

    RadioButton Choice(string label, string group, bool chosen, Action choose)
    {
        var option = new RadioButton { Content = label, GroupName = group, IsChecked = chosen };
        option.Checked += (_, _) => { choose(); main.ApplySettings(); };
        return option;
    }

    void ClearHistory(object sender, RoutedEventArgs e) => main.ClearHistoryFromSettings();
    void ClearShelf(object sender, RoutedEventArgs e) => main.ClearShelfFromSettings();

    void SaveCalendars(object sender, RoutedEventArgs e)
    {
        main.CalendarText = Calendars.Text;
        CalendarsSaved.Text = "Saved · loading…";
    }

    void OpenDataFolder(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(App.DataFolder);
        Process.Start("explorer.exe", App.DataFolder);
    }

    // Runs the same uninstaller that Settings → Apps uses (it asks first).
    void Uninstall(object sender, RoutedEventArgs e) => Process.Start(Environment.ProcessPath, "--uninstall");
}
