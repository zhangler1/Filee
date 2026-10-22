// Renders PDF pages to raster images with PDFium (via PDFtoImage, MIT / PDFium BSD-3): one image per page,
// with the preset's page range and image options applied (PDFtoImage already yields Skia bitmaps).

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Image;
using Filee.Engines.Infrastructure;
using PDFtoImage;
using SkiaSharp;

// PDFtoImage marks its API for the platforms PDFium ships for (Windows, macOS, Linux, mobile).
// Filee only targets desktop platforms that are all covered.
#pragma warning disable CA1416

namespace Filee.Engines.Pdf;

/// <summary>PDF → every raster format Skia writes (PNG, JPG, WebP, BMP, GIF, ICO).</summary>
public sealed class PdfiumConverter : IConverter, IImageTransformer
{
    public string Id => "pdfium";
    public string DisplayName => "PDFium";

    /// <summary>PDFium is not thread-safe.</summary>
    public int MaxParallelism => 1;

    /// <summary>Renders pages to images, then applies the image options to them.</summary>
    public bool Transforms(string from, string to) => true;

    public IReadOnlyList<ConversionEdge> Edges { get; } =
        SkiaEncoder.WriteFormats.Select(target => new ConversionEdge("pdf", target)).ToList();

    public EngineStatus GetStatus() => EngineStatus.Available(version: EngineVersions.Library("PDFtoImage", typeof(Conversion)));

    public Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var pdf = File.ReadAllBytes(step.InputPath);
            var pageCount = Conversion.GetPageCount(pdf, null);
            var pages = PageRange.Parse(step.Preset.Pdf.PageRange, pageCount);
            var dpi = Math.Clamp(step.Preset.Pdf.RenderDpi, 36, 1200);
            var options = new RenderOptions(Dpi: dpi, WithAnnotations: true, WithFormFill: true, BackgroundColor: SKColors.White);
            var written = new List<string>();

            var index = 0;
            foreach (var bitmap in Conversion.ToImages(pdf, pages, null, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageNumber = pages[index] + 1;
                index++;
                using (bitmap)
                using (var frame = SkiaEncoder.ApplyOptions(bitmap, step.Preset.Image, step.To))
                {
                    var path = step.Output.Allocate(step.To, pages.Count > 1 ? $"_p{pageNumber}" : null);
                    if (path is not null)
                    {
                        SkiaEncoder.Write(frame, path, step.To, step.Preset.Image);
                        written.Add(path);
                    }
                }
                progress?.Report((double)index / pages.Count);
            }
            return written;
        }, cancellationToken);
}
