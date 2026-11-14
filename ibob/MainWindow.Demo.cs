using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ibob;

// Screenshot mode:  ibob.exe --screenshots <folder>
// Opens a hidden copy of ibob filled with made-up sample data (no songs, files, messages or Wi-Fi names of yours),
// saves a sharp PNG of each screen, then quits. Used for the README and for sharing.
public partial class MainWindow
{
    const double ShotScale = 3;   // 3× pixels so the images stay crisp when shared

    public async Task TakeScreenshots(string folder)
    {
        Directory.CreateDirectory(folder);
        FillWithSampleData();
        await Task.Delay(400);

        // The closed pill: just the eyes; then with the camera/mic dots and a running focus ring; then an alert.
        await Shot(folder, "pill");
        CameraDot.Visibility = MicDot.Visibility = FocusRing.Visibility = Visibility.Visible;
        FocusRing.Stroke = FocusOrange;
        FocusRing.Data = Arc(0.7, 12);
        await Shot(folder, "pill-busy");
        CameraDot.Visibility = MicDot.Visibility = FocusRing.Visibility = Visibility.Collapsed;
        ShowAlert(SampleIcon("#0A84FF", "S"), "Sprint review in 10 min", PlanIndex);
        Alert.Visibility = Visibility.Visible;
        Eyes.Visibility = Visibility.Collapsed;
        await Shot(folder, "pill-alert", 600);
        alerting = false;
        Alert.Visibility = Visibility.Collapsed;

        // Every open screen.
        Animate(420, OpenHeight, open: true);
        foreach (var (index, name) in new[] { (HomeIndex, "home"), (PlanIndex, "plan"), (NotesIndex, "notes"), (ShelfIndex, "shelf"),
                                               (ClipboardIndex, "clipboard"), (FocusIndex, "focus"), (NotificationsIndex, "notifications") })
        {
            ShowPage(index);
            await Shot(folder, name, 700);
        }
        ShowPage(HomeIndex);
        ShowHomeScreen(quick: true);
        await Shot(folder, "home-quick", 700);

        // Right-click disc on a shelf item, and its Convert sub-disc.
        ShowPage(ShelfIndex);
        await Task.Delay(500);
        var report = shelf.First(item => item.Name.EndsWith(".docx"));
        var pill = Pill.TranslatePoint(new Point(0, 0), Radial);
        var center = new Point(pill.X + 140, pill.Y + 120);
        ShowRing(report, center, ActionsFor(report, center), null);
        await Shot(folder, "disc", 600);
        var photo = shelf.First(item => item.Name.EndsWith(".png"));   // images have the most conversions, so they show the sub-disc best
        ShowRing(photo, center, ConvertActions(photo, center), "Convert");
        await Shot(folder, "disc-convert", 600);
    }

    // Renders the whole window (pill + any disc on top of it) and crops to the pill.
    async Task Shot(string folder, string name, int wait = 300)
    {
        await Task.Delay(wait);   // let animations finish
        var root = (FrameworkElement)Content;
        root.UpdateLayout();
        var picture = new RenderTargetBitmap((int)(root.ActualWidth * ShotScale), (int)(root.ActualHeight * ShotScale),
            96 * ShotScale, 96 * ShotScale, PixelFormats.Pbgra32);
        picture.Render(root);

        var area = Pill.TransformToAncestor(root).TransformBounds(new Rect(0, 0, Pill.ActualWidth, Pill.ActualHeight));
        var crop = new CroppedBitmap(picture, new Int32Rect((int)(area.X * ShotScale), (int)(area.Y * ShotScale),
            (int)(area.Width * ShotScale), (int)(area.Height * ShotScale)));
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(crop));
        using var file = File.Create(Path.Combine(folder, name + ".png"));
        png.Save(file);
    }

    void FillWithSampleData()
    {
        // Tab bar and pill.
        BigTime.Text = DateTime.Now.ToString("h:mm");   // real time, so it matches the sample meeting times
        BigBattery.Text = "86%";
        BigBattery.Foreground = Green;
        ShowBadge(3);
        Status.Opacity = 0;

        // Home: music screen.
        SongTitle.Text = "Lo-fi Beats to Study To";
        SongArtist.Text = "Chillhop Radio";
        ShowArt(SampleArt());
        Seek.Maximum = 214;
        Seek.Value = 102;
        Total.Text = "3:34";
        PlayIcon.Text = PauseGlyph;

        // Home: quick settings.
        DateLine.Text = DateTime.Now.ToString("dddd, d MMMM") + " · 4h 12m left";
        Volume.Value = 62;
        Brightness.Value = 70;
        BrightnessText.Text = "70%";
        WifiText.Text = "Home Wi-Fi";
        WifiButton.Background = Blue;
        BluetoothText.Text = "On";
        BluetoothButton.Background = Blue;
        MicIcon.Text = "";
        MicText.Text = "Mic on";
        MicButton.Background = Gray;

        // Plan.
        var now = DateTime.Now;
        MeetingList.ItemsSource = new[]
        {
            new Meeting(now.AddMinutes(25), now.AddMinutes(55), "Sprint review", "", "https://example.com", false),
            new Meeting(now.AddHours(3), now.AddHours(4), "Data Structures class", "Room 4B", null, false),
        };
        TaskList.ItemsSource = new[]
        {
            new TaskItem { Text = "Pay hostel fees", Due = now.AddHours(5).AddMinutes(20.5) },
            new TaskItem { Text = "DBMS assignment", Due = now.AddDays(2).AddHours(4.01) },
            new TaskItem { Text = "Read chapter 4 for the quiz" },
            new TaskItem { Text = "Update resume", Done = true },
        };
        NoTasks.Visibility = Visibility.Collapsed;

        // Notes.
        NotesBox.Text = "Hackathon ideas\n• Offline-first study planner\n• OCR for handwritten notes\n\nInterview prep: revise DP + system design basics";
        NotesHint.Visibility = Visibility.Collapsed;

        // Shelf: empty sample files (only their names and icons matter), kept in a temporary folder.
        string samples = Path.Combine(Path.GetTempPath(), "ibob-screenshot-samples");
        Directory.CreateDirectory(samples);
        var demoShelf = new ObservableCollection<ShelfItem>();
        foreach (var name in new[] { "Project report.docx", "Marks.xlsx", "Lecture 7 slides.pdf", "Whiteboard.png", "Meeting notes.txt" })
        {
            string path = Path.Combine(samples, name);
            if (!File.Exists(path)) File.WriteAllText(path, "");
            demoShelf.Add(new ShelfItem(path, name, FileIcon(path), ""));
        }
        foreach (var item in demoShelf) shelf.Add(item);   // the disc's Merge button checks this list
        ShelfList.ItemsSource = demoShelf;
        ShelfHint.Visibility = Visibility.Collapsed;
        AutoButton.Background = Blue;

        // Clipboard.
        ClipList.ItemsSource = new[]
        {
            new Clip { Text = "git push origin main", Pinned = true },
            new Clip { Text = "SELECT name, cgpa FROM students WHERE cgpa > 8.5;", Pinned = true },
            new Clip { Text = "Meeting moved to Room 4B at 3 pm" },
            new Clip { Text = "https://github.com/karthik26-hub-lab/ibob" },
        };
        NoClips.Visibility = Visibility.Collapsed;

        // Focus.
        FocusMode.Text = "Focus";
        FocusClock.Text = "18:42";
        FocusStartText.Text = "Pause";

        // Notifications.
        NotificationList.ItemsSource = new[]
        {
            new Note(SampleIcon("#34C759", "A"), "Asha", "Shared the slides for tomorrow's presentation", "2m"),
            new Note(SampleIcon("#0A84FF", "S"), "Sprint review", "Starts in 25 minutes", "5m"),
            new Note(SampleIcon("#FF9F0A", "M"), "College mail", "Your fee receipt is ready to download", "1h"),
        };
        NoNotifications.Visibility = Visibility.Collapsed;
    }

    // A soft gradient square standing in for album artwork.
    static ImageSource SampleArt()
    {
        var art = new DrawingVisual();
        using (var draw = art.RenderOpen())
        {
            var gradient = new LinearGradientBrush(Color.FromRgb(94, 92, 230), Color.FromRgb(255, 105, 180), 45);
            draw.DrawRectangle(gradient, null, new Rect(0, 0, 192, 192));
            draw.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), null, new Point(130, 70), 38, 38);
        }
        return Rendered(art, 192);
    }

    // A coloured rounded square with a letter, standing in for an app icon.
    static ImageSource SampleIcon(string color, string letter)
    {
        var icon = new DrawingVisual();
        using (var draw = icon.RenderOpen())
        {
            draw.DrawRoundedRectangle((Brush)new BrushConverter().ConvertFromString(color), null, new Rect(0, 0, 64, 64), 14, 14);
            var text = new FormattedText(letter, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), 34, Brushes.White, 1);
            draw.DrawText(text, new Point((64 - text.Width) / 2, (64 - text.Height) / 2));
        }
        return Rendered(icon, 64);
    }

    static ImageSource Rendered(Visual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
