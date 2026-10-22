using System.Text.Json;
using System.Text.RegularExpressions;
using Filee.App.Services;

namespace Filee.App.Tests;

/// <summary>Keeps shipped languages in sync so none silently falls back to English.</summary>
public partial class LocalizationTests
{
    private static string I18nDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Filee.slnx")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "Filee.App", "Assets", "i18n");
        }
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(I18nDirectory, code + ".json")))!;

    public static TheoryData<string> Languages => new(LocalizationService.Languages.Where(l => l.Code != "en").Select(l => l.Code));

    [Fact]
    public void Shared_language_registry_covers_every_shipped_translation_once()
    {
        var codes = LocalizationService.Languages.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Directory.GetFiles(I18nDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).Order(), codes.Order());
    }

    [Fact]
    public void Chinese_is_offered_under_its_own_name()
    {
        Assert.Contains(("zh-CN", "简体中文"), LocalizationService.Languages);
        Assert.Equal("zh-CN", LocalizationService.Resolve("zh"));
        Assert.False(SoftPalette.IsRightToLeft("zh-CN"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Load("en").Keys.ToHashSet();
        var other = Load(code).Keys.ToHashSet();

        Assert.Empty(english.Except(other));
        Assert.Empty(other.Except(english));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Format_placeholders_match_english(string code)
    {
        var english = Load("en");
        foreach (var (key, value) in Load(code))
            Assert.True(
                Placeholders(english[key]).SetEquals(Placeholders(value)),
                $"{code}:{key} placeholders differ from English");
    }

    [Fact]
    public void Every_key_used_in_xaml_exists()
    {
        var english = Load("en");
        var src = Path.GetFullPath(Path.Combine(I18nDirectory, "..", ".."));
        var missing = Directory.EnumerateFiles(src, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(f => DynamicResourceKey().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Where(k => k.Contains('.') && !k.StartsWith("Icon.", StringComparison.Ordinal))
            .Where(k => !english.ContainsKey(k))
            .Distinct()
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>Han characters look different in Chinese, Japanese and Korean fonts: the UI language's comes first.</summary>
    [Theory]
    [InlineData("zh-CN", "Microsoft YaHei")]
    [InlineData("ja", "Yu Gothic UI")]
    [InlineData("en", "Malgun Gothic")]
    public void The_ui_languages_cjk_font_comes_first(string language, string cjk)
    {
        var families = ThemeService.FontFamilies(language).Split(", ").ToList();

        Assert.Equal("Segoe UI", families[0]); // Windows' own Latin UI font, first for every language
        Assert.Equal(cjk, families[1]);
    }

    private static HashSet<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{DynamicResource ([\w.]+)\}")]
    private static partial Regex DynamicResourceKey();
}
