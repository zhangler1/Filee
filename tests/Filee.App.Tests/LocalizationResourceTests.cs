using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Filee.App.Services;

namespace Filee.App.Tests;

public class LocalizationResourceTests
{
    [AvaloniaFact]
    public void Strings_are_available_as_application_resources()
    {
        TestServices.EnsureInitialized("zh-CN");
        Assert.True(Application.Current!.TryGetResource("home.title", null, out var value));
        Assert.Equal("首页", value);
    }

    [AvaloniaFact]
    public void DynamicResource_in_xaml_resolves_and_follows_language_changes()
    {
        TestServices.EnsureInitialized("en");
        var text = new TextBlock();
        text.Bind(TextBlock.TextProperty, text.GetResourceObservable("home.title"));
        var toggle = new ToggleSwitch();
        var window = new Window { Content = new StackPanel { Children = { text, toggle } } };
        window.Show();
        try
        {
            Assert.Equal("Home", text.Text);

            TestServices.EnsureInitialized("zh-CN");
            Assert.Equal("首页", text.Text);
        }
        finally
        {
            window.Close();
            TestServices.EnsureInitialized("en");
        }
    }

    [AvaloniaFact]
    public void Every_localized_key_of_both_languages_has_a_non_empty_text()
    {
        // The two shipped languages must stay in step: a key in one is in the other with real text.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Filee.slnx")))
            dir = dir.Parent!;
        var root = Path.Combine(dir!.FullName, "src", "Filee.App", "Assets", "i18n");
        var english = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "en.json")))!;
        var chinese = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "zh-CN.json")))!;
        Assert.Equal(english.Count, chinese.Count);
        foreach (var (key, value) in english)
        {
            Assert.True(chinese.ContainsKey(key), key);
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.False(string.IsNullOrWhiteSpace(chinese[key]), key);
        }
    }
}
