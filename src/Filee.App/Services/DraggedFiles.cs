// The files in a drag. Avalonia reads file URLs (what Explorer offers). Lite: no browser drops, no macOS pasteboard.

using Avalonia.Input;
using Avalonia.Platform.Storage;
using Filee.Core.Platform;

namespace Filee.App.Services;

internal static class DraggedFiles
{
    /// <summary>
    /// Whether the drag offers files or folders in a form Filee reads (they may still be missing, or a link to a web
    /// page).
    /// </summary>
    public static bool Offered(IDataTransfer data) => data.Contains(DataFormat.File);

    /// <summary>The files in the drag: the local files (an empty list when it brought none).</summary>
    public static IReadOnlyList<string> Read(IDataTransfer data, bool forDrop) => Local(data);

    /// <summary>
    /// Local file and folder paths in the drag (virtual items are ignored). A shortcut counts as the file or folder it
    /// points to. A trailing separator is removed, and a drive or the file system root is ignored.
    /// </summary>
    public static IReadOnlyList<string> Local(IDataTransfer data) =>
        Local(data, ConversionService.Exists, AppHost.Get<IPlatformServices>().ResolveShortcuts);

    internal static IReadOnlyList<string> Local(IDataTransfer data, Func<string, bool> exists,
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? resolveShortcuts = null)
    {
        IReadOnlyList<string> Usable(IReadOnlyList<string> paths) =>
            (resolveShortcuts?.Invoke(paths) ?? paths)
                .Select(path => Path.TrimEndingDirectorySeparator(path))
                .Where(path => exists(path) && !IsRoot(path))
                .Distinct(FileSystemPaths.Comparer).ToList();

        return Usable((data.TryGetFiles() ?? []).Select(item => item.TryGetLocalPath()).OfType<string>().ToList());
    }

    /// <summary>Whether <paramref name="path"/> is a drive or the file system root ("C:\", "/").</summary>
    internal static bool IsRoot(string path) =>
        string.Equals(Path.GetPathRoot(path), path, FileSystemPaths.Comparison);

    /// <summary>The formats a drag offers, for the log when Filee finds no files in it.</summary>
    public static string Describe(IDataTransfer data) =>
        data.Formats.Count == 0 ? "nothing" : string.Join(", ", data.Formats.Select(format => format.Identifier).Distinct());
}
