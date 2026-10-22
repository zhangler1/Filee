// Shared SkiaSharp logic: applies preset ImageOptions and writes a bitmap in the requested format.
// Used by SkiaImageConverter (images) and by PdfiumConverter (PDF pages → images).

using Filee.Core.Conversion;
using Filee.Core.Presets;
using SkiaSharp;

namespace Filee.Engines.Image;

/// <summary>Image formats Skia reads and writes.</summary>
internal static class SkiaEncoder
{
    /// <summary>Image formats Lite reads.</summary>
    public static readonly string[] ReadFormats = ["png", "jpg", "webp", "bmp", "gif", "ico"];

    /// <summary>Image formats Lite writes: Skia encodes PNG / JPG / WebP, BMP and ICO have writers here.</summary>
    public static readonly string[] WriteFormats = ["png", "jpg", "webp", "bmp", "ico"];

    /// <summary>Formats without an alpha channel: transparent pixels are flattened onto the background colour.</summary>
    private static readonly HashSet<string> NoAlpha = ["jpg"];

    /// <summary>
    /// The edge an image engine offers from a picture in <paramref name="source"/> to <paramref name="target"/>.
    /// </summary>
    public static ConversionEdge Edge(string source, string target) => new(source, target, 10);

    /// <summary>Decodes a picture, applying its stored orientation (EXIF) so its pixels read the way it looks.</summary>
    public static SKBitmap Decode(string path)
    {
        using var codec = SKCodec.Create(new SKFileStream(path));
        if (codec is null)
            throw new NotSupportedException($"Skia cannot decode '{path}'.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul,
            codec.Info.ColorSpace ?? SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap();
        _ = bitmap.TryAllocPixels(info) ? 0 : throw new OutOfMemoryException();
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new NotSupportedException($"Skia could not decode '{path}' ({result}).");

        var oriented = Orient(bitmap, codec.EncodedOrigin);
        if (!ReferenceEquals(oriented, bitmap))
        {
            bitmap.Dispose();
            return oriented;
        }
        return bitmap;
    }

    /// <summary>Turns and mirrors a decoded picture as its EXIF orientation says.</summary>
    private static SKBitmap Orient(SKBitmap bitmap, SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.TopLeft => bitmap,
        SKEncodedOrigin.TopRight => Mirror(bitmap, flipVertical: false),
        SKEncodedOrigin.BottomRight => Turn(bitmap, 180),
        SKEncodedOrigin.BottomLeft => Mirror(bitmap, flipVertical: true),
        SKEncodedOrigin.LeftTop => Mirror(Turn(bitmap, -90), flipVertical: false),
        SKEncodedOrigin.RightTop => Turn(bitmap, 90),
        SKEncodedOrigin.RightBottom => Mirror(Turn(bitmap, 90), flipVertical: false),
        SKEncodedOrigin.LeftBottom => Turn(bitmap, -90),
        _ => bitmap,
    };

    /// <summary>Applies the preset's resize / turn / colour options to a bitmap. Takes ownership of <paramref name="source"/>.</summary>
    public static SKBitmap ApplyOptions(SKBitmap source, ImageOptions options, string targetFormat)
    {
        var bitmap = Turn(source, options);

        var (width, height) = (bitmap.Width, bitmap.Height);
        switch (options.Resize)
        {
            case ResizeMode.Percent when options.ResizePercent is > 0 and not 100:
                (width, height) = (Math.Max(1, (int)Math.Round(bitmap.Width * options.ResizePercent / 100.0)),
                    Math.Max(1, (int)Math.Round(bitmap.Height * options.ResizePercent / 100.0)));
                break;
            case ResizeMode.LongEdge when options.LongEdge > 0 && options.LongEdge < Math.Max(bitmap.Width, bitmap.Height):
                (width, height) = Scaled(bitmap.Width, bitmap.Height, options.LongEdge);
                break;
            case ResizeMode.Width when options.Width > 0 && options.Width < bitmap.Width:
                (width, height) = Scaled(bitmap.Width, bitmap.Height, options.Width);
                break;
        }
        var resized = Resample(bitmap, width, height);
        if (!ReferenceEquals(resized, bitmap))
        {
            if (!ReferenceEquals(bitmap, source))
                bitmap.Dispose();
            bitmap = resized;
        }

        if (options.Grayscale)
            bitmap = Recolour(bitmap, ColorMatrixGrayscale);

        if (NoAlpha.Contains(targetFormat) && bitmap.AlphaType != SKAlphaType.Opaque)
            bitmap = Flatten(bitmap, ParseColor(options.Background));

        return bitmap;
    }

    /// <summary>Turns and mirrors a bitmap as <paramref name="options"/> say. Returns <paramref name="source"/> when it stays as it is.</summary>
    private static SKBitmap Turn(SKBitmap source, ImageOptions options)
    {
        var turned = options.Rotation switch
        {
            ImageRotation.Right90 => Turn(source, 90),
            ImageRotation.Left90 => Turn(source, -90),
            ImageRotation.Rotate180 => Turn(source, 180),
            _ => source,
        };
        if (options.Flip == ImageFlip.Horizontal)
            turned = Mirror(turned, flipVertical: false);
        else if (options.Flip == ImageFlip.Vertical)
            turned = Mirror(turned, flipVertical: true);
        return turned;
    }

    /// <summary>The longest side a format stores: JPEG 65,535 px (65,500 keeps clear of encoder rounding), WebP 16,383 px.</summary>
    public static double MaxSide(string targetFormat) => targetFormat switch
    {
        "jpg" or "jpeg" => 65_500,
        "webp" => 16_383,
        _ => double.PositiveInfinity,
    };

    /// <summary>Scales an image down until both sides fit <see cref="MaxSide"/>.</summary>
    public static SKBitmap FitFormat(SKBitmap source, string targetFormat)
    {
        var scale = Math.Min(1, MaxSide(targetFormat) / Math.Max(source.Width, source.Height));
        if (scale >= 1)
            return source;
        var scaled = Resample(source, Math.Max(1, (int)Math.Floor(source.Width * scale)), Math.Max(1, (int)Math.Floor(source.Height * scale)));
        if (!ReferenceEquals(scaled, source))
            source.Dispose();
        return scaled;
    }

    /// <summary>Writes a bitmap as <paramref name="targetFormat"/> with the preset's quality options.</summary>
    public static void Write(SKBitmap bitmap, string path, string targetFormat, ImageOptions options)
    {
        var fitted = FitFormat(bitmap, targetFormat);
        using var image = SKImage.FromBitmap(fitted);
        using var data = Encode(image, targetFormat, options);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        if (!ReferenceEquals(fitted, bitmap))
            fitted.Dispose();
    }

    /// <summary>Encodes an image, choosing lossless or quality settings as the preset says.</summary>
    public static SKData Encode(SKImage image, string targetFormat, ImageOptions options)
    {
        var quality = Math.Clamp(options.Quality, 1, 100);
        return targetFormat switch
        {
            "png" => image.Encode(),
            "jpg" => image.Encode(SKEncodedImageFormat.Jpeg, quality),
            "webp" when options.WebpLossless => EncodeWebp(image, lossless: true, quality),
            "webp" => EncodeWebp(image, lossless: false, quality),
            "bmp" => EncodeBmp(image),
            "ico" => EncodeIco(image, options),
            _ => throw new NotSupportedException($"Skia cannot write '{targetFormat}'."),
        };
    }

    /// <summary>
    /// Writes a 32-bit BMP (BITMAPFILEHEADER + BITMAPINFOHEADER + bottom-up BGRA rows): Skia's own encoder has no
    /// BMP coder. The alpha byte stays in the file; readers that don't know 32-bit BI_RGB show the flattened pixel.
    /// </summary>
    private static SKData EncodeBmp(SKImage image)
    {
        using var pixmap = image.PeekPixels() ?? throw new InvalidOperationException("The image has no pixels to encode.");
        var info = pixmap.Info;
        if (info.ColorType != SKColorType.Bgra8888)
        {
            using var surface = SKSurface.Create(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul, info.ColorSpace ?? SKColorSpace.CreateSrgb()));
            surface.Canvas.DrawImage(image, 0, 0);
            using var copied = surface.Snapshot();
            return EncodeBmp(copied);
        }

        var rowSize = info.Width * 4;
        var topDown = new byte[rowSize * info.Height];
        var rowInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, pixmap.AlphaType);
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(topDown, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            pixmap.ReadPixels(rowInfo, pinned.AddrOfPinnedObject(), rowSize, 0, 0);
        }
        finally
        {
            pinned.Free();
        }

        // BMP rows are bottom-up; the pixmap's are top-down. Both are BGRA byte order.
        var file = new byte[54 + topDown.Length];
        for (var y = 0; y < info.Height; y++)
            topDown.AsSpan(y * rowSize, rowSize).CopyTo(file.AsSpan(54 + (info.Height - 1 - y) * rowSize));
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        Put32(file, 2, file.Length); // file size
        Put32(file, 10, 54);        // pixel data offset
        Put32(file, 14, 40);        // BITMAPINFOHEADER size
        Put32(file, 18, info.Width);
        Put32(file, 22, info.Height);
        Put16(file, 26, 1);         // colour planes
        Put16(file, 28, 32);        // bits per pixel
        Put32(file, 34, topDown.Length);
        Put32(file, 38, 2835);      // 72 dpi
        Put32(file, 42, 2835);
        return SKData.CreateCopy(file);

        static void Put16(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
        }

        static void Put32(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
            buffer[at + 2] = (byte)(value >> 16);
            buffer[at + 3] = (byte)(value >> 24);
        }
    }

    /// <summary>
    /// Wraps PNG pictures in an ICO container, one per size in <see cref="ImageOptions.IcoSizes"/> (the PNG-in-ICO
    /// form Windows Vista and later read; entries hold at most 256 px, where a stored 256 is written as 0).
    /// </summary>
    private static SKData EncodeIco(SKImage image, ImageOptions options)
    {
        using var pixmap = image.PeekPixels() ?? throw new InvalidOperationException("The image has no pixels to encode.");
        var sizes = options.IcoSizes.Where(s => s is >= 1 and <= 256).DefaultIfEmpty(256).Distinct().OrderBy(s => s).ToList();
        var pngs = new List<byte[]>();
        foreach (var size in sizes)
        {
            var scale = Math.Min(1, (double)size / Math.Max(pixmap.Width, pixmap.Height));
            var width = Math.Max(1, (int)Math.Round(pixmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(pixmap.Height * scale));
            using var surface = SKSurface.Create(new SKImageInfo(width, height, pixmap.Info.ColorType, pixmap.Info.AlphaType, pixmap.Info.ColorSpace));
            surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var snapshot = surface.Snapshot();
            using var png = snapshot.Encode();
            pngs.Add(png.ToArray());
        }

        var file = new byte[6 + 16 * pngs.Count + pngs.Sum(p => p.Length)];
        file[2] = 1; // type: icon
        file[4] = (byte)pngs.Count;
        var offset = 6 + 16 * pngs.Count;
        for (var i = 0; i < pngs.Count; i++)
        {
            var entry = 6 + 16 * i;
            var size = sizes[i];
            var scale = Math.Min(1, (double)size / Math.Max(pixmap.Width, pixmap.Height));
            file[entry] = (byte)(size == 256 ? 0 : size);
            file[entry + 1] = (byte)(size == 256 ? 0 : size);
            file[entry + 4] = 1; // colour planes
            file[entry + 5] = 0;
            file[entry + 6] = 32; // bits per pixel
            file[entry + 7] = 0;
            Put32(file, entry + 8, pngs[i].Length);
            Put32(file, entry + 12, offset);
            pngs[i].CopyTo(file, offset);
            offset += pngs[i].Length;
        }
        return SKData.CreateCopy(file);

        static void Put32(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
            buffer[at + 2] = (byte)(value >> 16);
            buffer[at + 3] = (byte)(value >> 24);
        }
    }

    private static SKData EncodeWebp(SKImage image, bool lossless, int quality)
    {
        using var pixmap = image.PeekPixels()
            ?? throw new InvalidOperationException("The image has no pixels to encode.");
        var data = lossless
            ? pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100f))
            : pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality));
        return data ?? throw new InvalidOperationException("Skia could not encode the picture as WebP.");
    }

    /// <summary>Scales <paramref name="width"/> × <paramref name="height"/> so its longer edge (or width) is <paramref name="target"/>, never upscales.</summary>
    private static (int Width, int Height) Scaled(int width, int height, int target)
    {
        var scale = Math.Min(1, (double)target / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static SKBitmap Resample(SKBitmap source, int width, int height)
    {
        if (width == source.Width && height == source.Height)
            return source;
        return source.Resize(new SKImageInfo(width, height, source.ColorType, SKAlphaType.Premul, source.ColorSpace),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    private static SKBitmap Turn(SKBitmap source, int degrees)
    {
        var swap = Math.Abs(degrees) == 90;
        var copy = new SKBitmap(new SKImageInfo(swap ? source.Height : source.Width, swap ? source.Width : source.Height,
            source.ColorType, SKAlphaType.Premul, source.ColorSpace));
        using (var canvas = new SKCanvas(copy))
        {
            canvas.RotateDegrees(degrees, copy.Width / 2f, copy.Height / 2f);
            var dx = degrees == 90 ? copy.Width / 2f - source.Width / 2f
                : degrees == -90 ? -(copy.Height / 2f - source.Height / 2f)
                : 0;
            var dy = degrees == 90 ? -(copy.Width / 2f - source.Width / 2f)
                : degrees == -90 ? copy.Height / 2f - source.Height / 2f
                : 0;
            canvas.DrawBitmap(source, dx, dy);
        }
        return copy;
    }

    private static SKBitmap Mirror(SKBitmap source, bool flipVertical)
    {
        var copy = new SKBitmap(new SKImageInfo(source.Width, source.Height, source.ColorType, SKAlphaType.Premul, source.ColorSpace));
        using (var canvas = new SKCanvas(copy))
        {
            canvas.Translate(flipVertical ? 0 : copy.Width, flipVertical ? copy.Height : 0);
            canvas.Scale(flipVertical ? 1 : -1, flipVertical ? -1 : 1);
            canvas.DrawBitmap(source, 0, 0);
        }
        return copy;
    }

    private static SKBitmap Recolour(SKBitmap source, SKColorFilter filter)
    {
        var copy = new SKBitmap(new SKImageInfo(source.Width, source.Height, source.ColorType, SKAlphaType.Premul, source.ColorSpace));
        using (var canvas = new SKCanvas(copy))
        using (var paint = new SKPaint { ColorFilter = filter })
            canvas.DrawBitmap(source, 0, 0, paint);
        source.Dispose();
        return copy;
    }

    private static SKBitmap Flatten(SKBitmap source, SKColor background)
    {
        var copy = new SKBitmap(new SKImageInfo(source.Width, source.Height, source.ColorType, SKAlphaType.Opaque, source.ColorSpace));
        using (var canvas = new SKCanvas(copy))
        {
            canvas.Clear(background);
            canvas.DrawBitmap(source, 0, 0);
        }
        source.Dispose();
        return copy;
    }

    /// <summary>BT.709 grayscale, as browsers and ImageMagick write it.</summary>
    private static readonly SKColorFilter ColorMatrixGrayscale = SKColorFilter.CreateColorMatrix(
    [
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    private static SKColor ParseColor(string hex)
    {
        try
        {
            return string.IsNullOrWhiteSpace(hex) ? SKColors.White : SKColor.Parse(hex);
        }
        catch (ArgumentException)
        {
            return SKColors.White;
        }
    }
}
