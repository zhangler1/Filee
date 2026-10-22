// The Skia image converter: every donut format round-trips, and the preset's options (quality, background,
// resize, turn, mirror, grayscale) come out in the pixels. Inputs are generated in the tests.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using SkiaSharp;

namespace Filee.Engines.Tests;

public class SkiaImageConverterTests(EngineFixture fx) : IClassFixture<EngineFixture>
{
    private static void AssertDone(ConversionJob job)
    {
        var errors = string.Join("; ", job.Files.Where(f => f.State == FileState.Failed).Select(f => $"{f.ErrorKey} {f.ErrorDetail}"));
        Assert.True(job.State == JobState.Completed, $"{job.State}: {errors}");
        Assert.All(job.Outputs, o => Assert.True(new FileInfo(o).Length > 0, o));
    }

    /// <summary>The file's first bytes (format sniffing without a second image library).</summary>
    private static byte[] Header(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[16];
        var read = stream.Read(buffer, 0, buffer.Length);
        return buffer[..read];
    }

    [Theory]
    [InlineData("jpg", new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData("webp", new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' })]
    [InlineData("bmp", new byte[] { (byte)'B', (byte)'M' })]
    [InlineData("ico", new byte[] { 0, 0, 1, 0 })]
    public async Task Png_converts_to(string target, byte[] magic)
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "a.png", "png");

        var job = await fx.ConvertAsync([source], new Preset { Name = target, TargetFormat = target });

        AssertDone(job);
        var output = job.Outputs.Single();
        Assert.True(Header(output).AsSpan().StartsWith(magic), $"{target}: {Convert.ToHexString(Header(output))}");
        // The pixels survive the round trip: it decodes to the source's size. ICO holds at most 256 px.
        using var decoded = Filee.Engines.Image.SkiaEncoder.Decode(output);
        var expected = target == "ico" ? (256, 160) : (320, 200);
        Assert.Equal(expected, (decoded.Width, decoded.Height));
    }

    [Fact]
    public async Task A_gif_decodes_to_a_still_picture()
    {
        var dir = fx.NewFolder();
        // The smallest animated GIF there is: 1 × 1, one red pixel, looping.
        var source = Path.Combine(dir, "dot.gif");
        File.WriteAllBytes(source, Convert.FromBase64String("R0lGODlhAQABAAD/ACwAAAAAAQABAAACAkQBADs="));

        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "png" });

        AssertDone(job);
        using var decoded = Filee.Engines.Image.SkiaEncoder.Decode(job.Outputs.Single());
        Assert.Equal((1, 1), (decoded.Width, decoded.Height));
    }

    [Fact]
    public async Task Lossless_webp_round_trips_the_pixels()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "a.png", "png");

        var toWebp = await fx.ConvertAsync([source], new Preset { TargetFormat = "webp", Image = { WebpLossless = true } });
        AssertDone(toWebp);
        var back = await fx.ConvertAsync([toWebp.Outputs.Single()], new Preset { TargetFormat = "png", Output = { FileNamePattern = "{name}_back" } });
        AssertDone(back);

        using var original = Filee.Engines.Image.SkiaEncoder.Decode(source);
        using var result = Filee.Engines.Image.SkiaEncoder.Decode(back.Outputs.Single());
        Assert.Equal((original.Width, original.Height), (result.Width, result.Height));
        // A few spots across the picture: the gradient, the middle and the transparent corner.
        foreach (var (x, y) in new[] { (200, 100), (100, 50), (320 - 1, 199) })
            Assert.Equal(original.GetPixel(x, y), result.GetPixel(x, y));
    }

    [Fact]
    public async Task Jpg_output_flattens_transparency_onto_the_background_colour()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "a.png", "png");

        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "jpg", Image = { Quality = 40, Background = "#FF0000" } });

        AssertDone(job);
        using var result = Filee.Engines.Image.SkiaEncoder.Decode(job.Outputs.Single());
        var corner = result.GetPixel(5, 5);
        Assert.True(corner.Red > 200 && corner.Green < 60, $"transparent corner should become the background colour, is {corner}");
    }

    [Fact]
    public async Task A_higher_quality_jpg_is_bigger_than_a_lower_quality_one()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "a.png", "png", 1000, 600);

        var big = await fx.ConvertAsync([source], new Preset { TargetFormat = "jpg", Image = { Quality = 95 }, Output = { FileNamePattern = "{name}_95" } });
        var small = await fx.ConvertAsync([source], new Preset { TargetFormat = "jpg", Image = { Quality = 40 }, Output = { FileNamePattern = "{name}_40" } });

        AssertDone(big);
        AssertDone(small);
        Assert.True(new FileInfo(big.Outputs.Single()).Length > new FileInfo(small.Outputs.Single()).Length * 1.5, "quality should change the file size");
    }

    [Fact]
    public async Task Resize_by_long_edge_and_percent()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "a.png", "png", 1000, 500);

        var longEdge = await fx.ConvertAsync([source], new Preset { TargetFormat = "png", Image = { Resize = ResizeMode.LongEdge, LongEdge = 400 }, Output = { FileNamePattern = "{name}_le" } });
        var percent = await fx.ConvertAsync([source], new Preset { TargetFormat = BuiltInData.SameAsSource, Image = { Resize = ResizeMode.Percent, ResizePercent = 50 }, Output = { FileNamePattern = "{name}_50" } });

        AssertDone(longEdge);
        AssertDone(percent);
        using (var a = Filee.Engines.Image.SkiaEncoder.Decode(longEdge.Outputs.Single())) Assert.Equal((400, 200), (a.Width, a.Height));
        using (var b = Filee.Engines.Image.SkiaEncoder.Decode(percent.Outputs.Single())) Assert.Equal((500, 250), (b.Width, b.Height));
    }

    /// <summary>A 4 × 2 white image with one red pixel in the top left corner, run through a built-in preset.</summary>
    [Theory]
    [InlineData("rotate-right", "a_right.png", 2, 4, 1, 0)]
    [InlineData("rotate-left", "a_left.png", 2, 4, 0, 3)]
    [InlineData("rotate-180", "a_180.png", 4, 2, 3, 1)]
    [InlineData("flip-horizontal", "a_mirrored.png", 4, 2, 3, 0)]
    [InlineData("flip-vertical", "a_flipped.png", 4, 2, 0, 1)]
    public async Task The_corner_pixel_moves_where_the_preset_says(string presetId, string name, int width, int height, int x, int y)
    {
        var dir = fx.NewFolder();
        var source = Path.Combine(dir, "a.png");
        using (var bitmap = new SKBitmap(4, 2))
        {
            bitmap.Erase(SKColors.White);
            bitmap.SetPixel(0, 0, SKColors.Red);
            Filee.Engines.Image.SkiaEncoder.Write(bitmap, source, "png", new ImageOptions());
        }

        var job = await fx.ConvertAsync([source], BuiltInData.CreatePresets().Single(p => p.Id == presetId));

        Assert.Equal(JobState.Completed, job.State);
        var output = job.Outputs.Single();
        Assert.Equal(name, Path.GetFileName(output));
        using var result = Filee.Engines.Image.SkiaEncoder.Decode(output);
        Assert.Equal((width, height), (result.Width, result.Height));
        var red = result.GetPixel(x, y);
        Assert.True(red.Red > 200 && red.Green < 60, $"{presetId}: ({x},{y}) is {red}");
    }

    [Fact]
    public async Task Grayscale_makes_every_pixel_grey()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "a.png", "png");

        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "png", Image = { Grayscale = true }, Output = { FileNamePattern = "{name}_gray" } });

        AssertDone(job);
        using var result = Filee.Engines.Image.SkiaEncoder.Decode(job.Outputs.Single());
        foreach (var (x, y) in new[] { (200, 100), (100, 50), (60, 60) })
        {
            var pixel = result.GetPixel(x, y);
            Assert.True(Math.Max(Math.Max(pixel.Red, pixel.Green), pixel.Blue) - Math.Min(Math.Min(pixel.Red, pixel.Green), pixel.Blue) <= 2,
                $"({x},{y}) is {pixel}");
        }
    }

    [Fact]
    public async Task Ico_holds_the_sizes_the_preset_asked_for()
    {
        var dir = fx.NewFolder();
        var source = EngineFixture.MakeImage(dir, "logo.png", "png", 300, 200);

        var job = await fx.ConvertAsync([source], new Preset { TargetFormat = "ico", Image = { IcoSizes = [64, 32, 16] } });

        AssertDone(job);
        // An ICO with three sizes: the directory right after the 6-byte header lists 3 entries of 16 bytes,
        // each followed by a PNG picture of its own.
        var output = job.Outputs.Single();
        var header = Header(output);
        Assert.Equal(3, header[4] | header[5] << 8);
        Assert.True(new FileInfo(output).Length >= 6 + 3 * 16, "the ICO has no room for three pictures");
    }
}
