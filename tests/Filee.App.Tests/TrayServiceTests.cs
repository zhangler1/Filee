// The tray icon: its menu follows the language and the pause state.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public sealed class TrayServiceTests
{
    [AvaloniaFact]
    public void The_tray_icon_is_created_with_a_menu_that_follows_the_pause_state()
    {
        TestServices.EnsureInitialized("en");
        var app = Application.Current!;
        var directory = Directory.CreateTempSubdirectory("filee-tray-").FullName;
        try
        {
            var store = new UserDataStore(directory);
            store.Load();
            var service = new TrayService(store, AppHost.Get<ILocalizer>(), AppHost.Get<WindowService>());
            try
            {
                service.Create(app);
                var icon = Assert.Single(TrayIcon.GetIcons(app)!);
                Assert.True(icon.IsVisible);

                var pause = Assert.IsType<NativeMenuItem>(icon.Menu!.Items[1]);
                Assert.Equal(AppHost.Get<ILocalizer>()["tray.pause"], pause.Header);

                store.Settings.Paused = true;
                store.SaveSettings();
                Assert.Equal(AppHost.Get<ILocalizer>()["tray.resume"], pause.Header);
            }
            finally
            {
                service.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
