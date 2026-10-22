// Image → image with SkiaSharp (the same library the UI draws with, so it ships no second image engine):
// every picture format of the donut's built-in presets (PNG, JPG, WebP, BMP, GIF, ICO) with the preset's
// resize / quality / colour options. A multi-frame file (an animated GIF, an icon with several sizes) converts
// its first frame.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Infrastructure;
using SkiaSharp;

namespace Filee.Engines.Image;

/// <summary>Converts pictures between the common raster formats.</summary>
public sealed class SkiaImageConverter : IConverter, IImageTransformer
{
    public string Id => "skia";
    public string DisplayName => "Skia";

    /// <summary>Skia decodes and encodes in parallel; each conversion works on its own bitmaps.</summary>
    public int MaxParallelism => 0;

    /// <summary>Converting a picture applies the preset's turn and mirror options.</summary>
    public bool Transforms(string from, string to) => true;

    public IReadOnlyList<ConversionEdge> Edges { get; } =
        [.. SkiaEncoder.ReadFormats.SelectMany(source => SkiaEncoder.WriteFormats.Select(target => SkiaEncoder.Edge(source, target)))];

    public EngineStatus GetStatus() => EngineStatus.Available(version: EngineVersions.Library("SkiaSharp", typeof(SKBitmap)));

    public Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            var decoded = SkiaEncoder.Decode(step.InputPath);
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = SkiaEncoder.ApplyOptions(decoded, step.Preset.Image, step.To);
            var path = step.Output.Allocate(step.To);
            if (path is null)
                return [];
            SkiaEncoder.Write(bitmap, path, step.To, step.Preset.Image);
            progress?.Report(1.0);
            return [path];
        }, cancellationToken);
}