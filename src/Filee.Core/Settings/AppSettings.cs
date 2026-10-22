// Root settings object persisted to settings.json.

using System.Text.Json;
using System.Text.Json.Serialization;
using Filee.Core.Presets;

namespace Filee.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>What closing the main window does (Settings → General).</summary>
public enum CloseAction
{
    /// <summary>Ask, with "Don't ask again" (which saves the answer).</summary>
    Ask,

    /// <summary>Hide the window; Filee keeps running in the tray.</summary>
    Tray,

    /// <summary>Quit Filee.</summary>
    Quit,
}

/// <summary>Look and feel.</summary>
public sealed class ThemeSettings
{
    public ThemeMode Mode { get; set; } = ThemeMode.System;

    /// <summary>Accent colour as <c>#RRGGBB</c>.</summary>
    public string Accent { get; set; } = "#7F77DD";

    /// <summary>Base corner radius in pixels. Larger = "softer" UI.</summary>
    public double CornerRadius { get; set; } = 16;

    /// <summary>
    /// Disables animations (for low-end PCs or motion sensitivity).
    /// <c>null</c> follows the operating system setting.
    /// </summary>
    public bool? ReduceMotion { get; set; }

    public ThemeSettings Clone() => (ThemeSettings)MemberwiseClone();
}

/// <summary>Geometry and appearance of the donut toolbar.</summary>
public sealed class DonutSettings
{
    /// <summary>Outer radius in logical pixels.</summary>
    public double OuterRadius { get; set; } = 150;

    /// <summary>Inner (hole) radius as a fraction of the outer radius (0.2 - 0.7).</summary>
    public double HoleRatio { get; set; } = 0.42;

    /// <summary>Background opacity of the slices (<see cref="MinOpacity"/> - 1); labels and outlines stay opaque.</summary>
    public double Opacity { get; set; } = 0.96;

    /// <summary>The lowest <see cref="Opacity"/> the Theme and Donut toolbar pages offer.</summary>
    public const double MinOpacity = 0.1;

    /// <summary>The flip key unless the user picks another one (Settings → Shortcuts).</summary>
    public const string DefaultFlipKey = "Tab";

    /// <summary>
    /// The key that flips the open donut to its other face, as a SharpHook key name without "Vc" ("Tab", "F2", "Q");
    /// empty for none. See <see cref="EffectiveFlipKey"/>.
    /// </summary>
    public string FlipKey { get; set; } = DefaultFlipKey;

    /// <summary>A turn of the mouse wheel flips the open donut to its other face.</summary>
    public bool FlipWithWheel { get; set; } = true;

    /// <summary>
    /// Keys that never flip the donut: Esc closes it, Enter and Space convert or open a group, the arrows move between
    /// slices and Backspace folds a group's ring.
    /// </summary>
    public static readonly IReadOnlySet<string> ReservedFlipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Escape", "Enter", "Space", "Left", "Right", "Up", "Down", "Backspace",
    };

    /// <summary>
    /// Whether <paramref name="key"/> may flip the donut: not a key the donut uses (<see cref="ReservedFlipKeys"/>), nor
    /// the key of a keyboard shortcut in <paramref name="gestures"/>. While a drag holds its modifier that shortcut would
    /// fire too, and open the donut for the selected files instead.
    /// </summary>
    public static bool IsFlipKeyAllowed(string key, IEnumerable<TriggerGesture> gestures) =>
        !string.IsNullOrWhiteSpace(key) && !ReservedFlipKeys.Contains(key) &&
        !gestures.Any(g => g.Kind == TriggerKind.KeyChord && string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The key that flips the donut now: none when the user cleared it; Tab in place of one that isn't allowed (a file
    /// edited by hand, or a keyboard shortcut that took the key since), or none when Tab isn't either.
    /// </summary>
    public string? EffectiveFlipKey(IEnumerable<TriggerGesture> gestures)
    {
        if (string.IsNullOrWhiteSpace(FlipKey))
            return null;
        var list = gestures as IReadOnlyCollection<TriggerGesture> ?? gestures.ToList();
        if (IsFlipKeyAllowed(FlipKey.Trim(), list))
            return FlipKey.Trim();
        return IsFlipKeyAllowed(DefaultFlipKey, list) ? DefaultFlipKey : null;
    }

    public DonutSettings Clone() => (DonutSettings)MemberwiseClone();
}

/// <summary>All user settings except presets and profiles (which have their own files).</summary>
public sealed class AppSettings
{
    /// <summary>Current schema version. Increase when the format changes and add a migration.</summary>
    public const int CurrentSchemaVersion = 32;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary><c>"auto"</c>, <c>"en"</c>, <c>"ko"</c> or <c>"zh-CN"</c>.</summary>
    public string Language { get; set; } = "auto";

    public bool StartWithSystem { get; set; } = true;

    /// <summary>Show the Explorer "Convert with Filee" context menu entry.</summary>
    public bool ContextMenuEnabled { get; set; } = true;

    /// <summary>What closing the main window does. A value from a newer Filee reads as <see cref="CloseAction.Ask"/>.</summary>
    [JsonConverter(typeof(LenientEnumJsonConverter<CloseAction>))]
    public CloseAction CloseAction { get; set; } = CloseAction.Ask;

    /// <summary>Record finished conversions in the history (home page). Off: nothing new is recorded.</summary>
    public bool KeepHistory { get; set; } = true;

    /// <summary>
    /// How many finished conversions the history keeps, newest first (<see cref="MinHistoryLimit"/> to
    /// <see cref="MaxHistoryLimit"/>; a value outside is taken as the nearest end). The home page lists the latest
    /// ones and searches all of them.
    /// </summary>
    public int HistoryLimit { get; set; } = 200;

    public const int MinHistoryLimit = 50;
    public const int MaxHistoryLimit = 2000;

    /// <summary><see cref="HistoryLimit"/> within its range.</summary>
    [JsonIgnore]
    public int EffectiveHistoryLimit => Math.Clamp(HistoryLimit, MinHistoryLimit, MaxHistoryLimit);

    /// <summary>Whether the macOS Dock shows Filee.</summary>
    [JsonConverter(typeof(IconPreferenceJsonConverter))]
    public bool ShowInDock { get; set; } = true;

    /// <summary>Whether the macOS menu bar shows Filee's status icon.</summary>
    [JsonConverter(typeof(IconPreferenceJsonConverter))]
    public bool ShowMenuBarIcon { get; set; } = true;

    /// <summary>Temporarily disable all trigger gestures (tray "Pause").</summary>
    public bool Paused { get; set; }

    public ThemeSettings Theme { get; set; } = new();

    public DonutSettings Donut { get; set; } = new();

    public List<TriggerGesture> Triggers { get; set; } = TriggerGesture.Defaults();

    /// <summary>Process names (without .exe) in which gestures never trigger.</summary>
    public List<string> ExcludedProcesses { get; set; } = [];

    /// <summary>Engine ids in preferred order (see <see cref="Conversion.ConverterCatalog.Priority"/>).</summary>
    public List<string> EnginePriority { get; set; } = ["skia", "pdfium", "pdfsharp"];

    /// <summary>Set once the first-run welcome has been shown.</summary>
    public bool FirstRunCompleted { get; set; }

    /// <summary>Where presets set to "Default" save (Settings → General).</summary>
    public DefaultOutputSettings DefaultOutput { get; set; } = new();

    /// <summary>Every conversion keeps the created and modified dates of the original, whatever the preset says.</summary>
    public bool KeepFileDates { get; set; }

    /// <summary>
    /// Returns <paramref name="preset"/> itself, or a copy with the app-wide output settings applied: it saves to
    /// <see cref="DefaultOutput"/> when the preset's location is <see cref="OutputLocation.Default"/>, and keeps the
    /// original dates when <see cref="KeepFileDates"/> is on. Name pattern and conflict handling stay the preset's.
    /// </summary>
    public Preset WithDefaultOutput(Preset preset)
    {
        var location = preset.Output.Location == OutputLocation.Default;
        var dates = KeepFileDates && !preset.Output.KeepDates;
        if (!location && !dates)
            return preset;
        var copy = preset.Clone();
        if (location)
        {
            copy.Output.Location = DefaultOutput.Location == OutputLocation.Default ? OutputLocation.SameFolder : DefaultOutput.Location;
            copy.Output.SubfolderName = DefaultOutput.SubfolderName;
            copy.Output.CustomFolder = DefaultOutput.CustomFolder;
        }
        if (dates)
            copy.Output.KeepDates = true;
        return copy;
    }
}

/// <summary>Reads unexpected icon preference values as enabled while preserving valid booleans.</summary>
internal sealed class IconPreferenceJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            return reader.GetBoolean();

        reader.Skip();
        return true;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}

/// <summary>The save location presets set to "Default" use. Starts as "next to the source file", as before 1.4.</summary>
public sealed class DefaultOutputSettings
{
    /// <summary><see cref="OutputLocation.SameFolder"/>, <see cref="OutputLocation.Subfolder"/> or <see cref="OutputLocation.CustomFolder"/>.</summary>
    public OutputLocation Location { get; set; } = OutputLocation.SameFolder;

    public string SubfolderName { get; set; } = "converted";

    public string CustomFolder { get; set; } = "";
}
