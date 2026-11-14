using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Ibob;

// Right-click ring of actions for a shelf item, the actions themselves, and picking files to merge.
public partial class MainWindow
{
    static readonly Brush DiscFill = new SolidColorBrush(Color.FromRgb(24, 24, 26));
    static readonly Brush DiscEdge = new SolidColorBrush(Color.FromRgb(58, 58, 60));
    static readonly Brush ButtonHover = new SolidColorBrush(Color.FromRgb(58, 58, 60));
    const double DiscSize = 172, ButtonSize = 38, ButtonRing = 61;   // ButtonRing = distance from centre to each button

    bool radialOpen, selecting;
    readonly List<ShelfItem> mergePicks = new();

    // ---------- The ring: a compact dark disc with icon buttons around the edge ----------

    // Only the actions that make sense for this file type are shown. Glyph "MD" is drawn as text.
    List<(string Label, string Glyph, Action Run)> ActionsFor(ShelfItem item)
    {
        var actions = new List<(string, string, Action)>
        {
            ("Preview", "", () => OpenBoard(item)),
            ("Open", "", () => OpenFile(item)),
        };
        if (Converters.CanMakePdf(item.Path)) actions.Add(("To PDF", "", () => Convert(item, Converters.ToPdf, "PDF")));
        if (ImageTypes.Contains(Path.GetExtension(item.Path).ToLowerInvariant())) actions.Add(("Extract text", "Aa", () => ExtractText(item)));
        if (Converters.CanMakeMarkdown(item.Path)) actions.Add(("To Markdown", "MD", () => Convert(item, Converters.ToMarkdown, "Markdown")));
        if (Converters.CanMerge(item.Path) && shelf.Any(other => other != item && Converters.CanMerge(other.Path)))
            actions.Add(("Merge", "", () => StartMerge(item)));
        actions.Add(("Copy", "", () => CopyItem(item)));
        if (File.Exists(item.Path)) actions.Add(("Save", "", () => SaveItem(item)));
        actions.Add(("Remove", "", () => { Forget(item); SaveShelf(); }));
        return actions;
    }

    void ShowRadial(ShelfItem item, Point mouse)
    {
        if (selecting) return;
        Radial.Children.Clear();

        // Centre the disc on the click, but keep it fully inside the pill.
        var pill = Pill.TranslatePoint(new Point(0, 0), Radial);
        double half = DiscSize / 2 + 4;
        var center = new Point(
            Math.Clamp(mouse.X, pill.X + half, pill.X + Pill.ActualWidth - half),
            Math.Clamp(mouse.Y, pill.Y + half, pill.Y + Pill.ActualHeight - half));

        var disc = new Canvas { Width = DiscSize, Height = DiscSize, RenderTransformOrigin = new Point(0.5, 0.5) };
        // Solid fill and edge only: shadow effects render see-through in the island's transparent window.
        disc.Children.Add(new System.Windows.Shapes.Ellipse { Width = DiscSize, Height = DiscSize, Fill = DiscFill, Stroke = DiscEdge, StrokeThickness = 1 });

        // Centre: the file's icon and name; hovering a button swaps the name for the action's name.
        var name = new TextBlock { Text = item.Name, FontSize = 10, Foreground = Gray3, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Width = 74 };
        var middle = new StackPanel { Width = 74 };
        middle.Children.Add(new Image { Source = item.Icon, Width = 26, Height = 26, Margin = new Thickness(0, 0, 0, 4) });
        middle.Children.Add(name);
        Place(middle, DiscSize / 2, DiscSize / 2 - 4, 74, 46);
        disc.Children.Add(middle);

        var actions = ActionsFor(item);
        for (int i = 0; i < actions.Count; i++)
        {
            var (label, glyph, run) = actions[i];
            var button = RingButton(glyph, label == "Remove" ? Red : Brushes.White);
            button.Click += (_, _) => { CloseRadial(); run(); };
            button.MouseEnter += (_, _) => { button.Background = ButtonHover; name.Text = label; name.Foreground = Brushes.White; };
            button.MouseLeave += (_, _) => { button.Background = Brushes.Transparent; name.Text = item.Name; name.Foreground = Gray3; };

            double angle = -Math.PI / 2 + i * 2 * Math.PI / actions.Count;   // start at the top, go clockwise
            Place(button, DiscSize / 2 + Math.Cos(angle) * ButtonRing, DiscSize / 2 + Math.Sin(angle) * ButtonRing, ButtonSize, ButtonSize);
            PopIn(button, i);
            disc.Children.Add(button);
        }

        Canvas.SetLeft(disc, center.X - DiscSize / 2);
        Canvas.SetTop(disc, center.Y - DiscSize / 2);
        Radial.Children.Add(disc);
        Grow(disc);

        radialOpen = true;
        Radial.Visibility = Visibility.Visible;
    }

    static readonly Brush Gray3 = new SolidColorBrush(Color.FromRgb(142, 142, 147));

    // A round icon button (or "MD" text for Markdown).
    Button RingButton(string glyph, Brush color) => new()
    {
        Style = (Style)FindResource("Chip"), Background = Brushes.Transparent, Padding = new Thickness(0),
        Width = ButtonSize, Height = ButtonSize,
        Content = glyph.Length > 1
            ? new TextBlock { Text = glyph, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = color }
            : new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 15, Foreground = color },
    };

    // Positions an element on a canvas by its centre.
    static void Place(FrameworkElement element, double centerX, double centerY, double width, double height)
    {
        Canvas.SetLeft(element, centerX - width / 2);
        Canvas.SetTop(element, centerY - height / 2);
    }

    // The disc grows in from slightly smaller while fading in.
    static void Grow(FrameworkElement element)
    {
        var scale = new ScaleTransform();
        element.RenderTransform = scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    // Each button springs out a moment after the previous one.
    static void PopIn(FrameworkElement element, int order)
    {
        var scale = new ScaleTransform(0, 0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            BeginTime = TimeSpan.FromMilliseconds(60 + order * 18),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    void CloseRadial()
    {
        radialOpen = false;
        Radial.Visibility = Visibility.Collapsed;
        Radial.Children.Clear();
    }

    void RadialBackgroundClicked(object sender, MouseButtonEventArgs e)
    {
        CloseRadial();
        if (!MouseInsidePill()) Collapse(null, null);
    }

    void WindowLeft(object sender, MouseEventArgs e)
    {
        if (!radialOpen) return;
        CloseRadial();
        Collapse(null, null);
    }

    // Esc closes the ring / cancels merging / leaves the preview; Enter merges the picked files.
    void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && selecting) MergePicked();
        else if (e.Key == Key.Escape && radialOpen) CloseRadial();
        else if (e.Key == Key.Escape && selecting) EndMerge();
        else if (e.Key == Key.Escape && boardOpen) CloseBoard(null, null);
        else return;
        e.Handled = true;
    }

    // ---------- Actions ----------

    void OpenFile(ShelfItem item) => Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });

    // Puts the item on the clipboard: saved text as text, images as images, and always the file itself.
    void CopyItem(ShelfItem item)
    {
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { item.Path });
        if (IsSavedText(item.Path)) data.SetText(File.ReadAllText(item.Path));
        if (ImageTypes.Contains(Path.GetExtension(item.Path).ToLowerInvariant()))
            data.SetImage(new BitmapImage(new Uri(item.Path)));

        ignoreClipboardUntil = DateTime.Now.AddSeconds(1);   // don't save our own copy back onto the shelf
        Clipboard.SetDataObject(data, true);
        SetShelfStatus("Copied");
    }

    // The only way a shelf result reaches your folders: you pick where.
    void SaveItem(ShelfItem item)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = item.Name,
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        if (dialog.ShowDialog(this) == true) File.Copy(item.Path, dialog.FileName, overwrite: true);
    }

    // Runs a conversion in the background and puts the result on the shelf.
    async void Convert(ShelfItem item, Func<string, string, string> convert, string what)
    {
        SetShelfStatus($"Making {what}…");
        try
        {
            Directory.CreateDirectory(ShelfFolder);
            string result = await Converters.RunInBackground(() => convert(item.Path, ShelfFolder));
            Finished(result, $"{what} ready");
        }
        catch (Exception e) { SetShelfStatus("Couldn't convert: " + e.GetBaseException().Message); }
    }

    // Reads the text in an image (screenshots, photos of slides or notes) with Windows' built-in OCR,
    // copies it, and puts it on the shelf and in clipboard history.
    async void ExtractText(ShelfItem item)
    {
        SetShelfStatus("Reading text…");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.Path);
            using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            var ocr = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
                      ?? throw new InvalidOperationException("no text-recognition language is installed");

            var result = await ocr.RecognizeAsync(bitmap);
            string text = string.Join("\n", result.Lines.Select(line => line.Text));
            if (text.Length == 0) { SetShelfStatus("No text found"); return; }

            ignoreClipboardUntil = DateTime.Now.AddSeconds(1);
            Clipboard.SetText(text);
            Remember(text);
            Finished(SaveText(text), "Text copied");
        }
        catch (Exception e) { SetShelfStatus("Couldn't read text: " + e.Message); }
    }

    void Finished(string result, string message)
    {
        var added = AddToShelf(result);
        SaveShelf();
        SetShelfStatus(message);
        if (added != null) ShowAlert(added.Icon, "Shelf · " + added.Name, ShelfIndex);   // only shows if the island is closed
    }

    void SetShelfStatus(string text) => ShelfStatus.Text = text;

    // ---------- Merging ----------

    // Picking mode: mergeable files can be clicked in order (numbers show the order), others fade out.
    void StartMerge(ShelfItem first)
    {
        selecting = true;
        mergePicks.Clear();
        foreach (var item in shelf) item.Dimmed = !Converters.CanMerge(item.Path);
        ShelfTitle.Text = "Pick files";
        SetShelfStatus("Enter to merge, Esc to cancel");
        ShelfButtons.Visibility = Visibility.Collapsed;
        MergeButtons.Visibility = Visibility.Visible;
        ToggleMergePick(first);
        Activate();   // so Enter and Esc reach the island
    }

    void ToggleMergePick(ShelfItem item)
    {
        if (item.Dimmed) return;
        if (!mergePicks.Remove(item)) mergePicks.Add(item);
        foreach (var each in shelf) each.Badge = mergePicks.Contains(each) ? (mergePicks.IndexOf(each) + 1).ToString() : null;
        MergeButtonText.Text = $"Merge {mergePicks.Count}";
    }

    void EndMerge()
    {
        if (!selecting) return;
        selecting = false;
        mergePicks.Clear();
        foreach (var item in shelf) { item.Dimmed = false; item.Badge = null; }
        ShelfTitle.Text = "Shelf";
        SetShelfStatus("");
        ShelfButtons.Visibility = Visibility.Visible;
        MergeButtons.Visibility = Visibility.Collapsed;
    }

    void CancelMerge(object sender, RoutedEventArgs e) => EndMerge();
    void MergeClicked(object sender, RoutedEventArgs e) => MergePicked();

    async void MergePicked()
    {
        if (mergePicks.Count < 2) { SetShelfStatus("Pick at least 2 files"); return; }
        var files = mergePicks.Select(item => item.Path).ToList();
        EndMerge();

        SetShelfStatus($"Merging {files.Count} files…");
        try
        {
            Directory.CreateDirectory(ShelfFolder);
            string result = await Converters.RunInBackground(() => Converters.Merge(files, ShelfFolder));
            Finished(result, "Merged");
        }
        catch (Exception e) { SetShelfStatus("Couldn't merge: " + e.GetBaseException().Message); }
    }
}
