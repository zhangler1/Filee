// Real engines + a temp folder with generated sample files (Skia for pictures, PDFsharp for PDFs).

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Core.Settings;
using Filee.Engines;
using Filee.Engines.Infrastructure;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace Filee.Engines.Tests;

/// <summary>Real engines + a temp folder with generated sample files.</summary>
public sealed class EngineFixture : IDisposable
{
    public EngineFixture()
    {
        Directory.CreateDirectory(Root);
        var env = new EngineEnvironment(Path.Combine(Root, "data"));
        Converters = EngineRegistry.CreateAll(env);
        Catalog = new ConverterCatalog(Converters) { Priority = new AppSettings().EnginePriority };
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "filee-engine-tests", Guid.NewGuid().ToString("N")[..8]);
    public IReadOnlyList<IConverter> Converters { get; }
    public ConverterCatalog Catalog { get; }

    /// <summary>Creates a fresh sub folder for one test.</summary>
    public string NewFolder()
    {
        var dir = Path.Combine(Root, Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes a width×height gradient picture with a fully transparent 40×40 corner in the given format.</summary>
    public static string MakeImage(string folder, string name, string format, int width = 320, int height = 200)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                    [new SKColor(0x7F, 0x77, 0xDD), new SKColor(0x1D, 0x9E, 0x75)], null, SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, paint);
            canvas.Flush();
        }
        // Punch a fully transparent hole in the top-left corner (RGBA = 0).
        bitmap.Erase(SKColors.Transparent, new SKRectI(0, 0, Math.Min(40, width), Math.Min(40, height)));
        var path = Path.Combine(folder, name);
        Filee.Engines.Image.SkiaEncoder.Write(bitmap, path, format, new ImageOptions());
        return path;
    }

    /// <summary>Writes a PDF with <paramref name="pages"/> pages, alternating coral and teal.</summary>
    public static string MakePdf(string folder, string name, int pages)
    {
        var path = Path.Combine(folder, name);
        using var pdf = new PdfDocument();
        for (var i = 0; i < pages; i++)
        {
            var page = pdf.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(new XSolidBrush(i % 2 == 0 ? XColors.Coral : XColors.Teal), 0, 0, page.Width.Point, page.Height.Point);
        }
        pdf.Save(path);
        return path;
    }

    public async Task<ConversionJob> ConvertAsync(IReadOnlyList<string> sources, Preset preset)
    {
        await using var queue = new JobQueue(Catalog, EngineRegistry.FindPdfMerger(Converters));
        var job = new ConversionJob(sources, preset, preset.Name);
        await queue.RunAsync(job);
        return job;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}
