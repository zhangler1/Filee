// THE place where conversion engines are registered. Adding an engine = one line in CreateAll.
// Lite: pictures (Skia) and PDF (PDFsharp / PDFium) only.

using Filee.Core.Conversion;
using Filee.Engines.Image;
using Filee.Engines.Pdf;

namespace Filee.Engines;

/// <summary>Creates every built-in converter.</summary>
public static class EngineRegistry
{
    /// <summary>
    /// Instantiates all converters. Order only matters as a tie-breaker when no priority is set.
    /// </summary>
    public static IReadOnlyList<IConverter> CreateAll() =>
    [
        new SkiaImageConverter(),
        new PdfSharpConverter(),
        new PdfiumConverter(),
    ];

    /// <summary>Finds the PDF merger among the converters.</summary>
    public static IPdfMerger? FindPdfMerger(IEnumerable<IConverter> converters) =>
        converters.OfType<IPdfMerger>().FirstOrDefault();
}
