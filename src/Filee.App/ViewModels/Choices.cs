// Combo-box helpers shared by the settings pages (General, Triggers).

namespace Filee.App.ViewModels;

/// <summary>What item templates show of a <see cref="Choice{T}"/> (XAML can't name a generic type).</summary>
public interface IChoice
{
    string Label { get; }

    /// <summary>Secondary text shown right-aligned, e.g. the category of a format.</summary>
    string? Detail { get; }
}

/// <summary>A value with a localized label for combo boxes.</summary>
public sealed record Choice<T>(T Value, string Label, string? Detail = null) : IChoice
{
    public override string ToString() => Label;
}

/// <summary>Selections bound to properties that always hold a value (a choice, the open page).</summary>
public static class Choices
{
    /// <summary>
    /// For the OnXxxChanged(oldValue, newValue) hook of such a property. A combo box or list box sets null when it
    /// loses its selection (the selected item toggled off, its items replaced): the previous value is put back through
    /// <paramref name="restore"/>, which also shows it in the control again. Returns true then, so the hook stops.
    /// </summary>
    public static bool Restore<T>(T? oldValue, T? newValue, Action<T> restore) where T : class
    {
        if (newValue is not null || oldValue is null)
            return false;
        restore(oldValue);
        return true;
    }
}
