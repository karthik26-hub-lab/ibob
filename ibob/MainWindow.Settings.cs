using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Ibob;

// Applying settings: pill size, which pages show, which monitor ibob sits on, start with Windows, sounds.
public partial class MainWindow
{
    readonly Settings settings = Settings.Load();

    double PillWidth => settings.PillDimensions.Width;
    double PillHeight => settings.PillDimensions.Height;

    // Called at start and whenever something changes in the Settings window.
    public void ApplySettings()
    {
        settings.Save();

        // The face inside the pill grows or shrinks with the pill.
        double scale = PillHeight / 22;
        Mini.LayoutTransform = new ScaleTransform(scale, scale);
        if (!isOpen && !alerting) SizePill(PillWidth, PillHeight, PillHeight / 2, 200);

        HighlightTab();   // hides tabs turned off in Settings

        ShowAutoState();
        if (!settings.ClipboardHistory) { history.RemoveAll(clip => !clip.Pinned); SaveHistory(); ShowClips(); }
        Startup.Set(settings.StartWithWindows);
        PlaceOnScreen();
    }

    bool PageShown(int index) => index >= TabCount || !settings.HiddenPages.Contains(TabInfo[index].Name);   // Notifications is always available

    // A hidden page falls back to the nearest shown page (and Home if everything is hidden).
    int ShownPage(int index)
    {
        if (PageShown(index)) return index;
        var shown = Enumerable.Range(0, TabCount).Where(PageShown).ToList();
        return shown.Count == 0 ? HomeIndex : shown.OrderBy(i => Math.Abs(i - index)).First();
    }

    // Next shown page in the scroll direction (or stay put at the ends).
    int NextShownPage(int from, int step)
    {
        for (int i = from + step; i >= 0 && i < TabCount; i += step)
            if (PageShown(i)) return i;
        return from;
    }

    // Top centre of the chosen monitor (the main one if it's unplugged). Windows reports screens in
    // physical pixels, WPF positions windows in DIPs, so convert with this window's scale.
    public void PlaceOnScreen()
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Screen) ?? Forms.Screen.PrimaryScreen;
        var toDips = PresentationSource.FromVisual(this)?.CompositionTarget.TransformFromDevice ?? Matrix.Identity;
        var topCenter = toDips.Transform(new Point(screen.Bounds.Left + screen.Bounds.Width / 2.0, screen.Bounds.Top));
        Left = topCenter.X - Width / 2;
        Top = topCenter.Y;
    }

    void Chime()
    {
        if (settings.Sounds) SystemSounds.Asterisk.Play();
    }

    void OpenSettings(object sender, RoutedEventArgs e)
    {
        var existing = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing != null) { existing.Activate(); return; }
        new SettingsWindow(this, settings).Show();
    }

    // Used by the Settings window's "Calendars" box.
    public string CalendarText
    {
        get => string.Join(Environment.NewLine, CalendarLinks());
        set
        {
            Directory.CreateDirectory(App.DataFolder);
            File.WriteAllLines(CalendarsFile, value.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            LoadCalendars();
        }
    }

    public void ClearHistoryFromSettings() => ClearHistory(null, null);
    public void ClearShelfFromSettings() => ClearShelf(null, null);
}
