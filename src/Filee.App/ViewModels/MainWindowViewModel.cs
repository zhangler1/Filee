// Main (settings) window: left navigation + current page.
// Lite: Home, General and Triggers only.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Filee.App.Services;
using Filee.App.ViewModels.Pages;
using Filee.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Filee.App.ViewModels;

/// <summary>Navigation entry. <see cref="Title"/> follows the UI language.</summary>
/// <remarks>
/// Pages are made by a factory rather than from a <see cref="Type"/>: the lite exe is trimmed, and a type only
/// known at run time loses its constructors, so ActivatorUtilities could no longer create it.
/// </remarks>
public sealed partial class NavItem(string key, string titleKey, string iconKey, Func<IServiceProvider, ObservableObject> createPage) : ObservableObject
{
    public string Key { get; } = key;
    public string TitleKey { get; } = titleKey;
    public Geometry? Icon { get; } = Application.Current?.FindResource(iconKey) as Geometry;
    public Func<IServiceProvider, ObservableObject> CreatePage { get; } = createPage;

    [ObservableProperty] private string _title = "";

    public void Refresh(ILocalizer loc) => Title = loc[TitleKey];
}

/// <summary>
/// One per main window (<see cref="WindowService.ShowMain"/>); disposed with it, which drops its subscriptions on the
/// long-lived services and the current page.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILocalizer _loc;
    private bool _disposed;

    public MainWindowViewModel(IServiceProvider services, ILocalizer loc)
    {
        _services = services;
        _loc = loc;
        Items =
        [
            new("home", "nav.home", "Icon.Home", sp => ActivatorUtilities.CreateInstance<HomePageViewModel>(sp)),
            new("general", "nav.general", "Icon.Settings", sp => ActivatorUtilities.CreateInstance<GeneralPageViewModel>(sp)),
            new("triggers", "nav.triggers", "Icon.Keyboard", sp => ActivatorUtilities.CreateInstance<TriggersPageViewModel>(sp)),
        ];
        foreach (var item in Items)
            item.Refresh(loc);
        VersionText = _loc.Format("about.version", Version);
        loc.LanguageChanged += OnLanguageChanged;
        _selectedItem = Items[0];
        _currentPage = CreatePage(Items[0]);
    }

    public IReadOnlyList<NavItem> Items { get; }

    [ObservableProperty] private NavItem _selectedItem;
    [ObservableProperty]
    private ObservableObject _currentPage;

    /// <summary>
    /// Pages scroll as a whole, except pages that fit the window height and scroll their own parts.
    /// </summary>
    public ScrollBarVisibility PageScroll => ScrollBarVisibility.Auto;

    [ObservableProperty] private string _versionText = "";

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var item in Items)
            item.Refresh(_loc);
        VersionText = _loc.Format("about.version", Version);
        CurrentPage = CreatePage(SelectedItem); // rebuild so code-generated labels update too
    }

    /// <summary>The running Filee's version.</summary>
    public static string Version => typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";

    public void Navigate(string key)
    {
        var item = Items.FirstOrDefault(i => i.Key == key);
        if (item is not null)
            SelectedItem = item;
    }

    // Pages are recreated on every visit so they always reflect the latest settings. A navigation list that loses its
    // selection keeps the open page (Choices.Restore).
    partial void OnSelectedItemChanged(NavItem? oldValue, NavItem newValue)
    {
        if (!Choices.Restore(oldValue, newValue, v => SelectedItem = v))
            CurrentPage = CreatePage(newValue);
    }

    // Old pages may hold event subscriptions on long-lived services.
    partial void OnCurrentPageChanged(ObservableObject? oldValue, ObservableObject newValue) => (oldValue as IDisposable)?.Dispose();

    private ObservableObject CreatePage(NavItem item) => item.CreatePage(_services);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loc.LanguageChanged -= OnLanguageChanged;
        (CurrentPage as IDisposable)?.Dispose();
    }
}
