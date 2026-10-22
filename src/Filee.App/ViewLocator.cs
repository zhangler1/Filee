// Resolves a view for a view model: each page view model to its view. Mapped by hand instead of by the old
// name convention (Type.GetType + Activator.CreateInstance), because the lite exe is trimmed: a type and a
// constructor looked up at run time are gone there. A new page needs a line here too.

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using Filee.App.ViewModels.Pages;
using Filee.App.Views.Pages;

namespace Filee.App;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;
        return data switch
        {
            HomePageViewModel => new HomePage(),
            GeneralPageViewModel => new GeneralPage(),
            TriggersPageViewModel => new TriggersPage(),
            _ => new TextBlock { Text = "View not found: " + data.GetType().FullName },
        };
    }

    public bool Match(object? data) => data is ObservableObject && data.GetType().Name.EndsWith("PageViewModel", StringComparison.Ordinal);
}
