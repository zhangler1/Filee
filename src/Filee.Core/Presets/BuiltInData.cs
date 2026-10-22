// Factory defaults: presets and toolbar profiles created on first run or "Reset to defaults".
// Lite: two donuts (pictures and PDF) with the presets the three kept engines can run.

using Filee.Core.Formats;
using Filee.Core.Profiles;

namespace Filee.Core.Presets;

/// <summary>Built-in presets and profiles.</summary>
public static class BuiltInData
{
    /// <summary>Special target format meaning "same format as the source file" (e.g. resize only).</summary>
    public const string SameAsSource = "*";

    /// <summary>Creates the default preset list.</summary>
    public static List<Preset> CreatePresets() =>
    [
        new() { Id = "to-png", Name = "PNG", TargetFormat = "png" },
        new() { Id = "to-jpg", Name = "JPG", TargetFormat = "jpg", Image = { Quality = 90 } },
        new()
        {
            Id = "jpg-70", Name = "JPG 70%", TargetFormat = "jpg",
            Image = { Quality = 70 }, Output = { FileNamePattern = "{name}_70" },
        },
        new() { Id = "to-webp", Name = "WEBP", TargetFormat = "webp", Image = { Quality = 85 } },
        new() { Id = "to-bmp", Name = "BMP", TargetFormat = "bmp" },
        new() { Id = "to-ico", Name = "ICO", TargetFormat = "ico" },
        new() { Id = "to-pdf", Name = "PDF", TargetFormat = "pdf" },
        new()
        {
            Id = "merge-pdf", NameKey = "preset.merge_pdf", TargetFormat = "pdf",
            Pdf = { MergeIntoSingle = true }, Output = { FileNamePattern = "{name}_merged" },
        },
        new()
        {
            Id = "pdf-split", NameKey = "preset.split_pdf", TargetFormat = "pdf",
            Pdf = { SplitPages = true }, Output = { Location = OutputLocation.Subfolder, SubfolderName = "{name}" },
        },
        new()
        {
            Id = "half-size", NameKey = "preset.half_size", TargetFormat = SameAsSource,
            Image = { Resize = ResizeMode.Percent, ResizePercent = 50 }, Output = { FileNamePattern = "{name}_50" },
        },
        new()
        {
            Id = "grayscale", NameKey = "preset.grayscale", TargetFormat = SameAsSource,
            Image = { Grayscale = true }, Output = { FileNamePattern = "{name}_gray" },
        },
        .. ImageToolPresets(),
    ];

    /// <summary>
    /// Rotate and flip presets, and the two groups that fan them out on the donut. On the back of the images donut:
    /// the front is for conversions, a flip of the open donut (Tab or the mouse wheel) reaches them.
    /// </summary>
    public static List<Preset> ImageToolPresets() =>
    [
        Tool("rotate-left", "preset.rotate_left", "{name}_left", i => i.Rotation = ImageRotation.Left90),
        Tool("rotate-right", "preset.rotate_right", "{name}_right", i => i.Rotation = ImageRotation.Right90),
        Tool("rotate-180", "preset.rotate_180", "{name}_180", i => i.Rotation = ImageRotation.Rotate180),
        Tool("flip-horizontal", "preset.flip_horizontal", "{name}_mirrored", i => i.Flip = ImageFlip.Horizontal),
        Tool("flip-vertical", "preset.flip_vertical", "{name}_flipped", i => i.Flip = ImageFlip.Vertical),
        new()
        {
            Id = "rotate", NameKey = "preset.group_rotate", TargetFormat = Preset.GroupTarget, Kind = PresetKind.Group,
            SubPresetIds = ["rotate-left", "rotate-right", "rotate-180"],
        },
        new()
        {
            Id = "flip", NameKey = "preset.group_flip", TargetFormat = Preset.GroupTarget, Kind = PresetKind.Group,
            SubPresetIds = ["flip-horizontal", "flip-vertical"],
        },
    ];

    /// <summary>
    /// A rotate / flip preset. A JPG (or WEBP) is encoded again when it turns: at 95 rather than the usual 90, so
    /// turning a good photo doesn't visibly cost it quality.
    /// </summary>
    private static Preset Tool(string id, string nameKey, string pattern, Action<ImageOptions> set)
    {
        var preset = new Preset
        {
            Id = id,
            NameKey = nameKey,
            TargetFormat = SameAsSource,
            Image = { Quality = 95 },
            Output = { FileNamePattern = pattern },
        };
        set(preset.Image);
        return preset;
    }

    /// <summary>
    /// Creates the default toolbar profiles: pictures and PDF donuts, each with a back face (Tab or the mouse wheel
    /// flips the open donut to it) of less common targets and tools, and the mixed-files catch-all.
    /// </summary>
    public static List<ToolbarProfile> CreateProfiles() =>
    [
        new()
        {
            Id = "images", NameKey = "profile.images",
            Extensions = ["png", "jpg", "jpeg", "jfif", "webp", "bmp", "gif", "ico"],
            PresetIds = ["to-png", "to-jpg", "to-webp", "to-pdf", "to-bmp", "to-ico", "jpg-70", "half-size"],
            BackPresetIds = ["rotate", "flip", "grayscale", "merge-pdf"],
        },
        new()
        {
            Id = "pdf", NameKey = "profile.pdf",
            Extensions = ["pdf"],
            PresetIds = ["to-png", "to-jpg", "to-webp", "pdf-split", "merge-pdf", "to-pdf"],
            BackPresetIds = ["to-bmp", "to-ico", "half-size", "grayscale"],
        },
        new()
        {
            Id = "mixed", NameKey = "profile.mixed", IsFallback = true,
            PresetIds = ["to-pdf", "merge-pdf", "to-png", "to-jpg"],
            BackPresetIds = ["to-webp", "half-size", "grayscale"],
        },
    ];
}
