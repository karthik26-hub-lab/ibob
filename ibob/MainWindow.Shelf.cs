using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Ibob;

// One file or folder parked on the shelf. Glyph is a folder icon used when there's no file icon.
// Badge (merge order number) and Dimmed (can't be merged) change while picking files to merge.
public class ShelfItem : System.ComponentModel.INotifyPropertyChanged
{
    public ShelfItem(string path, string name, ImageSource icon, string glyph) { Path = path; Name = name; Icon = icon; Glyph = glyph; }

    public string Path { get; }
    public string Name { get; }
    public ImageSource Icon { get; }
    public string Glyph { get; }

    string badge;
    public string Badge { get => badge; set { badge = value; Changed(nameof(Badge)); } }

    bool dimmed;
    public bool Dimmed { get => dimmed; set { dimmed = value; Changed(nameof(Dimmed)); } }

    public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
}

// The file shelf: drop files on the island to keep them, drag them out later to copy them somewhere.
public partial class MainWindow
{
    const int ShelfLimit = 10;
    static readonly string IslandFolder = App.DataFolder;
    static readonly string ShelfFile = System.IO.Path.Combine(IslandFolder, "shelf.txt");
    // Files that only existed temporarily (WhatsApp, Outlook, pasted images) are saved here.
    static readonly string ShelfFolder = System.IO.Path.Combine(IslandFolder, "Shelf");

    readonly ObservableCollection<ShelfItem> shelf = new();
    string lastUnknownFormats;

    // Restores what was on the shelf last time (skipping files that no longer exist).
    void LoadShelf()
    {
        ShelfList.ItemsSource = shelf;
        if (File.Exists(ShelfFile))
            foreach (var path in File.ReadAllLines(ShelfFile)) AddToShelf(path);
        ShelfHint.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Returns the new item, or null if the path is missing or already on the shelf.
    ShelfItem AddToShelf(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        if (shelf.Any(item => item.Path == path)) return null;
        if (shelf.Count == ShelfLimit) Forget(shelf[0]);   // oldest makes room

        var icon = File.Exists(path) ? FileIcon(path) : null;
        var added = new ShelfItem(path, System.IO.Path.GetFileName(path.TrimEnd('\\')), icon, icon == null ? "" : "");
        shelf.Add(added);
        return added;
    }

    // Adds everything a drop/paste/copy carried and saves. Returns the last item added (or null).
    ShelfItem AddFrom(IDataObject data)
    {
        Directory.CreateDirectory(ShelfFolder);
        ShelfItem last = null;
        foreach (var path in DroppedFiles(data)) last = AddToShelf(path) ?? last;
        SaveShelf();
        return last;
    }

    // Removes an item; if the island saved its own copy of the file, that copy is deleted too.
    void Forget(ShelfItem item)
    {
        shelf.Remove(item);
        if (!item.Path.StartsWith(ShelfFolder, StringComparison.OrdinalIgnoreCase)) return;   // never delete your own files
        try
        {
            if (Directory.Exists(item.Path)) Directory.Delete(item.Path, recursive: true);   // e.g. a split PDF's pages folder
            else File.Delete(item.Path);
        }
        catch { }
    }

    void SaveShelf()
    {
        Directory.CreateDirectory(IslandFolder);
        File.WriteAllLines(ShelfFile, shelf.Select(item => item.Path));
        ShelfHint.Visibility = shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static ImageSource FileIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { return null; }
    }

    // ---------- Dropping in ----------

    // Real file paths, file contents (WhatsApp, Outlook), or a plain image.
    static bool CanTake(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) || VirtualFiles.Present(data)
        || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.UnicodeText);

    // Dragging files over the island opens it straight to the shelf.
    void FilesDragOver(object sender, DragEventArgs e)
    {
        bool canTake = CanTake(e.Data);
        e.Effects = canTake ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!canTake) LogUnknownFormats(e.Data);
        if (!isOpen) Open(ShelfIndex);
        else if (page != ShelfIndex) ShowPage(ShelfIndex);
    }

    void FilesDragLeave(object sender, DragEventArgs e)
    {
        if (!MouseInsidePill()) Collapse(null, null);
    }

    void FilesDropped(object sender, DragEventArgs e) => AddFrom(e.Data);

    // Paste works like a drop, but reads from the clipboard (copied files, a copied WhatsApp photo, a screenshot).
    void PasteToShelf(object sender, RoutedEventArgs e) => AddFrom(Clipboard.GetDataObject());

    // ---------- Auto: anything you copy (files, images, screenshots) lands on the shelf by itself ----------


    // Asks Windows to tell this window every time the clipboard changes.
    void WatchClipboard()
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook(ClipboardHook);
        AddClipboardFormatListener(source.Handle);
        ShowAutoState();
    }

    DateTime ignoreClipboardUntil;   // set when the island itself copies something, so it doesn't re-save it

    IntPtr ClipboardHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int ClipboardChanged = 0x031D;
        if (message == ClipboardChanged && DateTime.Now > ignoreClipboardUntil) TakeFromClipboard();
        return IntPtr.Zero;
    }

    // Copied text always goes into clipboard history; with Auto on, copies also land on the shelf.
    // The app that just copied may still be holding the clipboard, so retry briefly.
    async void TakeFromClipboard()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(150);
            try
            {
                var data = Clipboard.GetDataObject();
                if (IsPrivate(data)) return;
                if (data.GetData(DataFormats.UnicodeText) is string text && !data.GetDataPresent(DataFormats.FileDrop)) Remember(text);
                if (!settings.AutoShelf) return;
                bool justText = data.GetDataPresent(DataFormats.UnicodeText) && !data.GetDataPresent(DataFormats.FileDrop) && !VirtualFiles.Present(data);
                if (justText && !settings.TextToShelf) return;
                var added = AddFrom(data);
                if (added != null) ShowAlert(added.Icon, "Shelf · " + added.Name, ShelfIndex);
                return;
            }
            catch (COMException) { }
        }
    }

    // Password managers mark their copies as private so clipboard tools skip them.
    static bool IsPrivate(IDataObject data) =>
        data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing")
        || data.GetDataPresent("Clipboard Viewer Ignore")
        || (data.GetData("CanIncludeInClipboardHistory") is MemoryStream allowed && allowed.Length >= 4 && BitConverter.ToInt32(allowed.ToArray(), 0) == 0);

    void ToggleAuto(object sender, RoutedEventArgs e)
    {
        settings.AutoShelf = !settings.AutoShelf;
        settings.Save();
        ShowAutoState();
    }

    void ShowAutoState() => AutoButton.Background = settings.AutoShelf ? Blue : Gray;

    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr window);

    string lastText;   // so copying the same text twice doesn't add it twice

    // Order matters: files first, then text (so Excel cells come in as text, not a picture), then images.
    IEnumerable<string> DroppedFiles(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is string[] paths) return paths.Select(KeepIfTemporary);
        if (VirtualFiles.Present(data)) return VirtualFiles.Save(data, ShelfFolder);
        if (data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
        {
            if (text == lastText) return Array.Empty<string>();
            lastText = text;
            return new[] { SaveText(text) };
        }
        if (data.GetData("PNG") is MemoryStream png) return new[] { SavePng(png) };   // keeps transparency, unlike Bitmap
        if (data.GetData(DataFormats.Bitmap) is BitmapSource image) return new[] { SaveImage(image) };
        return Array.Empty<string>();
    }

    // Saved as a .txt named after its first words, e.g. "Meeting at 5 tomorrow.txt".
    static string SaveText(string text)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        string name = new string(text.Trim().Take(30).Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        string path = UniquePath(ShelfFolder, (name.Length == 0 ? "Text" : name) + ".txt");
        File.WriteAllText(path, text);
        return path;
    }

    static bool IsSavedText(string path) =>
        path.StartsWith(ShelfFolder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    static string SavePng(MemoryStream png)
    {
        string path = UniquePath(ShelfFolder, $"Image {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        File.WriteAllBytes(path, png.ToArray());
        return path;
    }

    // Some apps drag a file from their temp folder and delete it later, so keep our own copy.
    static string KeepIfTemporary(string path)
    {
        bool temporary = path.StartsWith(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                         || path.Contains(@"\Packages\", StringComparison.OrdinalIgnoreCase);
        if (!temporary || !File.Exists(path)) return path;

        string copy = UniquePath(ShelfFolder, System.IO.Path.GetFileName(path));
        File.Copy(path, copy);
        return copy;
    }

    static string SaveImage(BitmapSource image)
    {
        string path = UniquePath(ShelfFolder, $"Image {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        using var file = File.Create(path);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        png.Save(file);
        return path;
    }

    static string UniquePath(string folder, string name) => Converters.UniquePath(folder, name);

    // If an app drags something the shelf can't read, note what it offered so it can be supported later.
    void LogUnknownFormats(IDataObject data)
    {
        string formats = string.Join(", ", data.GetFormats());
        if (formats == lastUnknownFormats) return;
        lastUnknownFormats = formats;
        Directory.CreateDirectory(IslandFolder);
        File.AppendAllText(System.IO.Path.Combine(IslandFolder, "drops.log"), $"{DateTime.Now}: {formats}\n");
    }

    // ---------- Dragging out and removing ----------

    // Dragging an item out copies it wherever you drop it. Saved text also drops as plain text into chat boxes.
    void ShelfItemDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || selecting) return;
        var item = (ShelfItem)((FrameworkElement)sender).DataContext;
        var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
        if (IsSavedText(item.Path)) data.SetText(File.ReadAllText(item.Path));
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        if (!MouseInsidePill()) Collapse(null, null);
    }

    // Right-click shows the ring of actions around the cursor.
    void ShelfItemMenu(object sender, MouseButtonEventArgs e)
    {
        ShowRadial((ShelfItem)((FrameworkElement)sender).DataContext, e.GetPosition(Radial));
        e.Handled = true;
    }

    // Left-click only matters while picking files to merge.
    void ShelfItemClick(object sender, MouseButtonEventArgs e)
    {
        if (selecting) ToggleMergePick((ShelfItem)((FrameworkElement)sender).DataContext);
    }

    // Right-clicking an item shows its preview, not the island's "Close island" menu (which would close the board).
    void SkipMenu(object sender, System.Windows.Controls.ContextMenuEventArgs e) => e.Handled = true;

    void ClearShelf(object sender, RoutedEventArgs e)
    {
        foreach (var item in shelf.ToList()) Forget(item);
        SaveShelf();
    }

    // Mouse events pause during drag and drop, so check the cursor position directly.
    bool MouseInsidePill()
    {
        var mouse = Forms.Control.MousePosition;
        var point = Pill.PointFromScreen(new Point(mouse.X, mouse.Y));
        return point.X >= 0 && point.Y >= 0 && point.X <= Pill.ActualWidth && point.Y <= Pill.ActualHeight;
    }
}
