using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using ClosedXML.Excel;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Ibob;

// Turns shelf files into PDFs (single or merged) and Markdown (.md). Results are written into the given folder.
static class Converters
{
    // HEIC/HEIF (iPhone photos) open only if Windows' "HEIF Image Extensions" is installed.
    public static readonly string[] ImageTypes = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif" };
    static readonly string[] WordTypes = { ".docx", ".doc" };
    static readonly string[] MarkdownSources = { ".pdf", ".docx", ".doc", ".xlsx", ".xlsm", ".txt", ".csv" };
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();

    // "photo.jpg" → "photo (2).jpg" if the name is already taken.
    public static string UniquePath(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        for (int n = 2; File.Exists(path) || Directory.Exists(path); n++)
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
        return path;
    }

    // What each file type can do (used to decide which buttons the right-click ring shows).
    public static bool CanMerge(string path) => File.Exists(path) && (Ext(path) == ".pdf" || ImageTypes.Contains(Ext(path)) || WordTypes.Contains(Ext(path)));
    public static bool CanMakePdf(string path) => File.Exists(path) && (ImageTypes.Contains(Ext(path)) || WordTypes.Contains(Ext(path)));
    public static bool CanMakeMarkdown(string path) => File.Exists(path) && MarkdownSources.Contains(Ext(path));

    // Runs work on its own STA thread: Word automation and WPF image decoding both need one, and the island stays smooth.
    public static Task<T> RunInBackground<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    // ---------- PDF ----------

    public static string ToPdf(string path, string folder) => Merge(new[] { path }, folder, Path.GetFileNameWithoutExtension(path));

    // Joins PDFs, Word documents and images, in the given order, into one PDF.
    public static string Merge(IEnumerable<string> paths, string folder, string name = null)
    {
        using var output = new PdfDocument();
        foreach (var path in paths) AddToPdf(output, path);

        string result = UniquePath(folder, (name ?? $"Merged {DateTime.Now:yyyy-MM-dd HH.mm.ss}") + ".pdf");
        output.Save(result);
        return result;
    }

    static void AddToPdf(PdfDocument output, string path)
    {
        if (ImageTypes.Contains(Ext(path))) { AddImagePage(output, path); return; }

        // Word files are turned into a temporary PDF first, then their pages are copied in.
        string pdf = path;
        if (WordTypes.Contains(Ext(path)))
        {
            pdf = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
            WithWord(path, document => document.ExportAsFixedFormat(pdf, 17));   // 17 = PDF
        }
        try
        {
            using var input = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
            foreach (var page in input.Pages) output.AddPage(page);
        }
        finally { if (pdf != path) File.Delete(pdf); }
    }

    // One image per A4 page (landscape for wide images), shrunk to fit with a small margin.
    static void AddImagePage(PdfDocument output, string path)
    {
        using var bytes = ImageBytes(path);
        using var image = XImage.FromStream(bytes);
        bool wide = image.PointWidth > image.PointHeight;

        var page = output.AddPage();
        page.Width = XUnit.FromPoint(wide ? 842 : 595);
        page.Height = XUnit.FromPoint(wide ? 595 : 842);

        const double margin = 36;
        double scale = Math.Min(1, Math.Min((page.Width.Point - 2 * margin) / image.PointWidth, (page.Height.Point - 2 * margin) / image.PointHeight));
        double width = image.PointWidth * scale, height = image.PointHeight * scale;

        using var canvas = XGraphics.FromPdfPage(page);
        canvas.DrawImage(image, (page.Width.Point - width) / 2, (page.Height.Point - height) / 2, width, height);
    }

    // JPEG and PNG go in as they are; other formats (WebP, GIF, BMP, TIFF) are converted to PNG first.
    static MemoryStream ImageBytes(string path)
    {
        if (Ext(path) is ".jpg" or ".jpeg" or ".png")
        {
            var bytes = File.ReadAllBytes(path);
            return new MemoryStream(bytes, 0, bytes.Length, false, publiclyVisible: true);   // the PDF library reads the buffer directly
        }

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]);
        var stream = new MemoryStream();
        png.Save(stream);
        stream.Position = 0;
        return stream;
    }

    // Opens a document in a hidden copy of Microsoft Word, runs the action, then closes Word.
    static void WithWord(string path, Action<dynamic> action)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word is needed for Word files");
        dynamic word = Activator.CreateInstance(wordType);
        try
        {
            word.Visible = false;
            word.DisplayAlerts = 0;
            dynamic document = word.Documents.Open(path, false, true, false);   // no conversion prompt, read-only, not in recent files
            try { action(document); }
            finally { document.Close(false); }
        }
        finally
        {
            word.Quit(false);
            Marshal.FinalReleaseComObject(word);
        }
    }

    // ---------- Markdown ----------

    public static string ToMarkdown(string path, string folder)
    {
        string markdown = Ext(path) switch
        {
            ".pdf" => PdfMarkdown(path),
            ".docx" => WordMarkdown(path),
            ".doc" => OldWordMarkdown(path),
            ".xlsx" or ".xlsm" => ExcelMarkdown(path),
            ".csv" => Table(File.ReadAllLines(path).Where(line => line.Length > 0).Select(SplitCsv).ToList()),
            _ => File.ReadAllText(path),
        };

        string result = UniquePath(folder, Path.GetFileNameWithoutExtension(path) + ".md");
        File.WriteAllText(result, markdown);
        return result;
    }

    // PDF text, rebuilt line by line from word positions, with a heading per page.
    static string PdfMarkdown(string path)
    {
        using var pdf = PigDocument.Open(path);
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            text.AppendLine($"## Page {page.Number}").AppendLine();
            var lines = page.GetWords()
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 3))      // words on the same baseline form a line
                .OrderByDescending(line => line.Key)                            // PDF y grows upwards, so top line first
                .Select(line => string.Join(" ", line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)));
            foreach (var line in lines) text.AppendLine(line).AppendLine();
        }
        return text.ToString();
    }

    // A .docx is a zip; word/document.xml holds paragraphs (<w:p>) and tables (<w:tbl>).
    static string WordMarkdown(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml").Open();
        var body = XDocument.Load(stream).Root.Element(W + "body");

        var text = new StringBuilder();
        foreach (var block in body.Elements())
        {
            if (block.Name == W + "p") text.AppendLine(Paragraph(block)).AppendLine();
            else if (block.Name == W + "tbl")
                text.AppendLine(Table(block.Elements(W + "tr").Select(row => row.Elements(W + "tc").Select(CellText).ToList()).ToList())).AppendLine();
        }
        return Regex.Replace(text.ToString(), @"(\r?\n){3,}", "\n\n").Trim() + "\n";
    }

    // Headings become #, list items become -, bold/italic runs become **bold** / *italic*.
    static string Paragraph(XElement paragraph)
    {
        var properties = paragraph.Element(W + "pPr");
        string style = properties?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? "";
        string text = string.Concat(paragraph.Descendants(W + "r").Select(Run)).Replace("****", "").Trim();
        if (text.Length == 0) return "";

        if (style == "Title") return "# " + text;
        var heading = Regex.Match(style, @"Heading(\d)$");
        if (heading.Success) return new string('#', int.Parse(heading.Groups[1].Value)) + " " + text;
        if (properties?.Element(W + "numPr") != null) return "- " + text;
        return text;
    }

    static string Run(XElement run)
    {
        string text = string.Concat(run.Elements(W + "t").Select(t => t.Value));
        string core = text.Trim();
        if (core.Length == 0) return text;

        var format = run.Element(W + "rPr");
        bool On(string tag) => format?.Element(W + tag) is XElement flag && flag.Attribute(W + "val")?.Value is not ("0" or "false");
        if (On("b")) text = text.Replace(core, $"**{core}**");
        if (On("i")) text = text.Replace(core, $"*{core}*");
        return text;
    }

    static string CellText(XElement cell) =>
        string.Join(" ", cell.Elements(W + "p").Select(p => string.Concat(p.Descendants(W + "t").Select(t => t.Value))));

    // Old .doc files are saved as a temporary .docx by Word, then read like one.
    static string OldWordMarkdown(string path)
    {
        string docx = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
        try
        {
            WithWord(path, document => document.SaveAs2(docx, 16));   // 16 = .docx
            return WordMarkdown(docx);
        }
        finally { File.Delete(docx); }
    }

    // Each sheet becomes a "## Sheet name" heading and a table of its used cells (first row as the header).
    static string ExcelMarkdown(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);   // works even if Excel has it open
        using var book = new XLWorkbook(stream);
        var text = new StringBuilder();
        foreach (var sheet in book.Worksheets)
        {
            var used = sheet.RangeUsed();
            if (used == null) continue;
            var rows = used.Rows()
                .Select(row => Enumerable.Range(1, used.ColumnCount()).Select(column => row.Cell(column).GetFormattedString()).ToList())
                .ToList();
            text.AppendLine("## " + sheet.Name).AppendLine().AppendLine(Table(rows)).AppendLine();
        }
        return text.ToString();
    }

    // Splits one CSV line, keeping commas inside "quoted, values".
    static List<string> SplitCsv(string line) =>
        Regex.Matches(line, "(?:^|,)(\"(?:[^\"]|\"\")*\"|[^,]*)")
            .Select(match => match.Groups[1].Value.Trim('"').Replace("\"\"", "\""))
            .ToList();

    // Markdown table; the first row is the header.
    static string Table(List<List<string>> rows)
    {
        if (rows.Count == 0) return "";
        int columns = rows.Max(row => row.Count);
        string Line(List<string> row) =>
            "| " + string.Join(" | ", Enumerable.Range(0, columns).Select(i => i < row.Count ? row[i].Replace("|", "\\|").Replace("\n", " ") : "")) + " |";

        var lines = new List<string> { Line(rows[0]), "|" + string.Concat(Enumerable.Repeat(" --- |", columns)) };
        lines.AddRange(rows.Skip(1).Select(Line));
        return string.Join("\n", lines);
    }
}
