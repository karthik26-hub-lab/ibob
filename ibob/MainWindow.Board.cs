using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Ibob;

// The preview board: right-click a shelf item and the pill grows into a board that shows it.
public partial class MainWindow
{
    static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" };
    static readonly string[] TextTypes =
    {
        ".txt", ".md", ".csv", ".log", ".json", ".xml", ".html", ".css", ".js", ".ts", ".py",
        ".cs", ".java", ".c", ".cpp", ".h", ".sql", ".yaml", ".yml", ".ini", ".bat", ".ps1",
    };
    const int MaxPdfPages = 10;
    const int MaxTextBytes = 300_000;

    bool boardOpen;
    ShelfItem boardItem;

    async void OpenBoard(ShelfItem item)
    {
        boardItem = item;
        boardOpen = true;
        BoardTitle.Text = item.Name;
        Big.Visibility = Visibility.Collapsed;
        Board.Visibility = Visibility.Visible;
        Board.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { BeginTime = TimeSpan.FromMilliseconds(80) });
        SizePill(560, 400, 28, 380);

        ShowOnly(null);
        try { await ShowPreview(item.Path); }
        catch { ShowInfo(item, "Can't preview this file — use Open"); }
    }

    async Task ShowPreview(string path)
    {
        string type = Path.GetExtension(path).ToLowerInvariant();

        if (Directory.Exists(path)) ShowInfo(boardItem, "Folder — use Open to see what's inside");
        else if (ImageTypes.Contains(type))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;   // don't keep the file locked
            image.DecodePixelWidth = 1040;
            image.EndInit();
            BoardImage.Source = image;
            ShowOnly(BoardImage);
        }
        else if (type == ".pdf") await ShowPdf(path);
        else if (type == ".docx") { BoardText.Text = WordText(path); ShowOnly(BoardText); }
        else if (TextTypes.Contains(type) && new FileInfo(path).Length <= MaxTextBytes) { BoardText.Text = File.ReadAllText(path); ShowOnly(BoardText); }
        else ShowInfo(boardItem, "No preview for this type — use Open");
    }

    // Renders the first pages of a PDF as images using Windows' built-in PDF reader.
    async Task ShowPdf(string path)
    {
        var pdf = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        ShowOnly(BoardPdf);
        for (uint i = 0; i < Math.Min(pdf.PageCount, MaxPdfPages); i++)
        {
            using var page = pdf.GetPage(i);
            using var rendered = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(rendered, new PdfPageRenderOptions { DestinationWidth = 1040 });

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = rendered.AsStream();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            BoardPages.Children.Add(new Image { Source = image, Margin = new Thickness(0, 0, 0, 8) });
        }
        if (pdf.PageCount > MaxPdfPages)
            BoardPages.Children.Add(new TextBlock { Text = $"Showing {MaxPdfPages} of {pdf.PageCount} pages — use Open for the rest", Foreground = Gray, FontSize = 11 });
    }

    // A .docx is a zip; the text lives in word/document.xml as <w:t> pieces inside <w:p> paragraphs.
    static string WordText(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var document = zip.GetEntry("word/document.xml").Open();
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return string.Join("\n", XDocument.Load(document).Descendants(w + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value))));
    }

    void ShowInfo(ShelfItem item, string message)
    {
        BoardInfoIcon.Source = item.Icon;
        BoardInfoText.Text = message;
        ShowOnly(BoardInfo);
    }

    // Shows one preview area and clears the others.
    void ShowOnly(UIElement shown)
    {
        foreach (var area in new UIElement[] { BoardImage, BoardText, BoardPdf, BoardInfo })
            area.Visibility = area == shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown != BoardImage) BoardImage.Source = null;
        if (shown != BoardText) BoardText.Text = "";
        if (shown != BoardPdf) BoardPages.Children.Clear();
    }

    // ---------- Board buttons ----------

    void CloseBoard(object sender, RoutedEventArgs e)
    {
        HideBoard();
        Big.Visibility = Visibility.Visible;
        SizePill(420, OpenHeight, 28, 300);
        ShowPage(ShelfIndex);
    }

    void HideBoard()
    {
        boardOpen = false;
        Board.Visibility = Visibility.Collapsed;
        ShowOnly(null);
    }

    void BoardOpen(object sender, RoutedEventArgs e) => OpenFile(boardItem);
    void BoardCopy(object sender, RoutedEventArgs e) => CopyItem(boardItem);

    void BoardRemove(object sender, RoutedEventArgs e)
    {
        Forget(boardItem);
        SaveShelf();
        CloseBoard(null, null);
    }
}
