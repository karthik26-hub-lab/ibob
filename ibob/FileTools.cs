using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Ibob;

// Everyday file fixes: make PDFs smaller, split them into pages, turn images into (smaller) JPGs.
// Like Converters, every result is a new file in the given folder; originals are never changed.
static class FileTools
{
    const int PhotoMaxSide = 1600, ShrinkMaxSide = 1920;

    static string Ext(string path) => Path.GetExtension(path).ToLowerInvariant();
    static string Name(string path) => Path.GetFileNameWithoutExtension(path);

    public static bool IsPdf(string path) => File.Exists(path) && Ext(path) == ".pdf";
    public static bool IsImage(string path) => File.Exists(path) && Converters.ImageTypes.Contains(Ext(path));

    // ---------- PDF ----------

    // Most big PDFs are big because of photos (scans, phone pictures). This re-saves each JPEG photo
    // at most 1600 px wide/high and at quality 70, which usually cuts the size a lot with little visible change.
    public static string CompressPdf(string path, string folder)
    {
        using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        foreach (var page in pdf.Pages)
        {
            var pictures = page.Resources?.Elements.GetDictionary("/XObject");
            if (pictures == null) continue;
            foreach (var key in pictures.Elements.Keys.ToList())
                if (pictures.Elements.GetReference(key)?.Value is PdfDictionary picture && IsSimpleJpeg(picture))
                    ShrinkPicture(picture);
        }
        pdf.Options.CompressContentStreams = true;

        string result = Converters.UniquePath(folder, Name(path) + " (compressed).pdf");
        pdf.Save(result);
        return result;
    }

    // Plain RGB or greyscale JPEG photos only; anything with transparency or unusual colours is left alone.
    static bool IsSimpleJpeg(PdfDictionary picture) =>
        picture.Elements.GetName("/Subtype") == "/Image"
        && picture.Elements.GetName("/Filter") == "/DCTDecode"
        && picture.Elements.GetName("/ColorSpace") is "/DeviceRGB" or "/DeviceGray"
        && !picture.Elements.ContainsKey("/SMask");

    static void ShrinkPicture(PdfDictionary picture)
    {
        byte[] original = picture.Stream.Value;
        var frame = BitmapDecoder.Create(new MemoryStream(original), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var resized = Resize(frame, PhotoMaxSide);
        byte[] smaller = Jpeg(resized, 70);
        if (smaller.Length >= original.Length) return;   // already small: keep it

        picture.Stream.Value = smaller;
        picture.Elements.SetInteger("/Length", smaller.Length);
        picture.Elements.SetInteger("/Width", resized.PixelWidth);
        picture.Elements.SetInteger("/Height", resized.PixelHeight);
        picture.Elements.Remove("/DecodeParms");
    }

    // Each page becomes its own PDF, all inside one new folder ("Report pages").
    public static string SplitPdf(string path, string folder)
    {
        using var input = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        string target = Converters.UniquePath(folder, Name(path) + " pages");
        Directory.CreateDirectory(target);
        for (int i = 0; i < input.PageCount; i++)
        {
            using var single = new PdfDocument();
            single.AddPage(input.Pages[i]);
            single.Save(Path.Combine(target, $"{Name(path)} - page {i + 1}.pdf"));
        }
        return target;
    }

    // ---------- Images ----------

    // Full-size JPG (handy for HEIC, WebP or PNG files that a website or form won't accept).
    public static string ToJpg(string path, string folder) =>
        SaveJpg(Load(path), Converters.UniquePath(folder, Name(path) + ".jpg"), 90);

    // Smaller JPG for emailing or uploading: at most 1920 px on the long side.
    public static string Shrink(string path, string folder) =>
        SaveJpg(Resize(Load(path), ShrinkMaxSide), Converters.UniquePath(folder, Name(path) + " (small).jpg"), 80);

    static BitmapSource Load(string path) =>
        BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

    static string SaveJpg(BitmapSource image, string path, int quality)
    {
        File.WriteAllBytes(path, Jpeg(OnWhite(image), quality));
        return path;
    }

    static BitmapSource Resize(BitmapSource image, int maxSide)
    {
        double scale = Math.Min(1, maxSide / (double)Math.Max(image.PixelWidth, image.PixelHeight));
        return scale < 1 ? new TransformedBitmap(image, new ScaleTransform(scale, scale)) : image;
    }

    static byte[] Jpeg(BitmapSource image, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    // JPG has no transparency: put see-through images (PNG logos, screenshots) on white instead of black.
    static BitmapSource OnWhite(BitmapSource image)
    {
        bool seeThrough = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32;
        if (!seeThrough) return image;
        var canvas = new DrawingVisual();
        using (var draw = canvas.RenderOpen())
        {
            var area = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            draw.DrawRectangle(Brushes.White, null, area);
            draw.DrawImage(image, area);
        }
        var flat = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        flat.Render(canvas);
        return flat;
    }
}
