// Builds PDFs from images, splits / extracts PDF pages and merges PDFs (PDFsharp, MIT).
// Lite: the compression levels (which need ImageMagick and Ghostscript) are gone; pages are copied as they are.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Image;
using Filee.Engines.Infrastructure;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace Filee.Engines.Pdf;

/// <summary>Image → PDF, PDF → PDF (split / page range) and PDF merging.</summary>
public sealed class PdfSharpConverter : IConverter, IPdfMerger, IImageTransformer
{
    private const double PointsPerInch = 72.0;
    private const double MillimetresPerInch = 25.4;

    public string Id => "pdfsharp";
    public string DisplayName => "PDFsharp";
    public int MaxParallelism => 0;

    /// <summary>Images into a PDF are turned first; a PDF to a PDF (split, extract) is copied page by page, as is.</summary>
    public bool Transforms(string from, string to) => from != "pdf";

    public IReadOnlyList<ConversionEdge> Edges { get; } =
    [
        .. SkiaEncoder.ReadFormats.Select(source => new ConversionEdge(source, "pdf")),
        new("pdf", "pdf"),
    ];

    public EngineStatus GetStatus() => EngineStatus.Available(version: EngineVersions.Library("PDFsharp", typeof(PdfDocument)));

    public Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
            step.From != "pdf" ? ImageToPdf(step, progress, cancellationToken) : SplitOrExtract(step, progress, cancellationToken),
            cancellationToken);

    public Task MergeAsync(IReadOnlyList<string> inputPaths, string outputPath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var output = new PdfDocument();
            foreach (var input in inputPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = Open(input, PdfDocumentOpenMode.Import);
                foreach (var page in source.Pages)
                    output.AddPage(page);
            }
            output.Save(outputPath);
        }, cancellationToken);

    /// <summary>Places a picture on its own PDF page.</summary>
    private static IReadOnlyList<string> ImageToPdf(ConversionStep step, IProgress<double>? progress, CancellationToken ct)
    {
        var options = step.Preset.Image;
        var pdfOptions = step.Preset.Pdf;
        var bitmap = SkiaEncoder.Decode(step.InputPath);
        ct.ThrowIfCancellationRequested();
        bitmap = SkiaEncoder.ApplyOptions(bitmap, options, "pdf");

        using var document = new PdfDocument();
        // "pdf" as target: resize / grayscale apply, no format-specific encoding settings.
        var dpi = options.Dpi is > 0 ? options.Dpi.Value : 96.0;
        using var stream = EncodeForPdf(bitmap, step.From, options);
        using var ximage = XImage.FromStream(stream);

        var imageWidthPt = bitmap.Width / dpi * PointsPerInch;
        var imageHeightPt = bitmap.Height / dpi * PointsPerInch;
        var page = document.AddPage();
        var (pageWidth, pageHeight) = PageSize(pdfOptions.PageSize, imageWidthPt > imageHeightPt);
        if (pdfOptions.PageSize == PdfPageSize.FitImage)
            (pageWidth, pageHeight) = (imageWidthPt, imageHeightPt);
        page.Width = XUnit.FromPoint(pageWidth);
        page.Height = XUnit.FromPoint(pageHeight);

        // Fit the image inside the page margins, keeping its aspect ratio, centred.
        var margin = pdfOptions.PageSize == PdfPageSize.FitImage ? 0 : pdfOptions.MarginMm / MillimetresPerInch * PointsPerInch;
        var boxWidth = pageWidth - 2 * margin;
        var boxHeight = pageHeight - 2 * margin;
        var scale = Math.Min(boxWidth / imageWidthPt, boxHeight / imageHeightPt);
        var drawWidth = imageWidthPt * scale;
        var drawHeight = imageHeightPt * scale;

        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawImage(ximage, margin + (boxWidth - drawWidth) / 2, margin + (boxHeight - drawHeight) / 2, drawWidth, drawHeight);
        bitmap.Dispose();
        progress?.Report(1.0);

        var path = step.Output.Allocate("pdf");
        if (path is null)
            return [];
        document.Save(path);
        return [path];
    }

    /// <summary>PDF → PDF: split into single pages, or extract the configured page range.</summary>
    private static IReadOnlyList<string> SplitOrExtract(ConversionStep step, IProgress<double>? progress, CancellationToken ct)
    {
        using var source = Open(step.InputPath, PdfDocumentOpenMode.Import);
        var pages = PageRange.Parse(step.Preset.Pdf.PageRange, source.PageCount);
        var written = new List<string>();

        if (step.Preset.Pdf.SplitPages)
        {
            for (var i = 0; i < pages.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var single = new PdfDocument();
                single.AddPage(source.Pages[pages[i]]);
                var path = step.Output.Allocate("pdf", $"_p{pages[i] + 1}");
                if (path is null)
                    continue;
                single.Save(path);
                written.Add(path);
                progress?.Report((i + 1.0) / pages.Count);
            }
            return written;
        }

        using var extract = new PdfDocument();
        foreach (var index in pages)
            extract.AddPage(source.Pages[index]);
        var output = step.Output.Allocate("pdf");
        if (output is null)
            return [];
        extract.Save(output);
        return [output];
    }

    /// <summary>Opens a PDF; a password-protected one fails with the message the other PDF conversions give.</summary>
    internal static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConversionRefusedException("error.password_protected", Path.GetFileName(path));
        }
    }

    /// <summary>
    /// Encodes a picture for embedding. JPEG sources stay JPEG (no generational loss beyond one re-encode at high
    /// quality); everything else is embedded losslessly as PNG.
    /// </summary>
    private static MemoryStream EncodeForPdf(SKBitmap bitmap, string sourceFormat, ImageOptions options)
    {
        var stream = new MemoryStream();
        var encoded = sourceFormat == "jpg" && bitmap.AlphaType == SKAlphaType.Opaque;
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = encoded
            ? image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(Math.Max(options.Quality, 85), 1, 100))
            : image.Encode())
            data.SaveTo(stream);
        stream.Position = 0;
        return stream;
    }

    private static (double Width, double Height) PageSize(PdfPageSize size, bool landscape)
    {
        var (w, h) = size switch
        {
            PdfPageSize.Letter => (612.0, 792.0),
            _ => (595.28, 841.89), // A4
        };
        return landscape ? (h, w) : (w, h);
    }
}
