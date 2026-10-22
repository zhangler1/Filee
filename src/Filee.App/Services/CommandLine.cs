// Command line options:
//   Filee.exe                          open the main window
//   Filee.exe --background             start in the tray only (used by "start with Windows")
//   Filee.exe --settings               open the main window
//   Filee.exe --convert a b ...        open the donut for these files (a shortcut, another program)
//   Filee.exe --convert-list <file>    same, with one path per line in a UTF-8 file, for selections too long for a
//                                      command line; the app deletes the file afterwards
//   Filee.exe --quit                   ask a running Filee to exit, and wait until it has (no window otherwise)

using System.Text;

namespace Filee.App.Services;

/// <summary>Parsed command line.</summary>
/// <param name="ListFile">The <c>--convert-list</c> file, if any (see <see cref="DeleteListFile"/>).</param>
public sealed record CommandLine(
    bool Background,
    bool ShowSettings,
    IReadOnlyList<string> ConvertFiles,
    string? ListFile = null,
    bool Quit = false)
{
    /// <summary>File name prefix of list files written into %TEMP%.</summary>
    public const string ListFilePrefix = "Filee-convert-";

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        var background = false;
        var settings = false;
        var files = new List<string>();
        string? listFile = null;
        var collecting = false;
        var expectList = false;
        var quit = false;

        foreach (var arg in args)
        {
            if (expectList)
            {
                expectList = false;
                listFile = arg;
                files.AddRange(ReadListFile(arg).Where(Exists).Select(Path.GetFullPath));
                continue;
            }
            var (name, value) = SplitOption(arg);
            switch (name)
            {
                case "--quit":
                    quit = true;
                    break;
                case "--background":
                    background = true;
                    break;
                case "--settings":
                    settings = true;
                    break;
                case "--convert":
                    collecting = true;
                    break;
                case "--convert-list":
                    expectList = true;
                    break;
                default:
                    // Ignore anything unknown that isn't a file (e.g. options of a newer version).
                    if (collecting && !arg.StartsWith("--", StringComparison.Ordinal) && Exists(arg))
                        files.Add(Path.GetFullPath(arg));
                    break;
            }
        }
        return new CommandLine(background, settings, files, listFile, quit);
    }

    /// <summary>"--Name=Value" → ("--name", "Value"); "--name" → ("--name", null).</summary>
    private static (string Name, string? Value) SplitOption(string arg)
    {
        var equals = arg.StartsWith("--", StringComparison.Ordinal) ? arg.IndexOf('=') : -1;
        return equals < 0 ? (arg.ToLowerInvariant(), null) : (arg[..equals].ToLowerInvariant(), arg[(equals + 1)..]);
    }

    /// <summary>
    /// Deletes the list file once its paths were taken over. Only files written into the temp folder with the known
    /// prefix are deleted, so a crafted command line cannot make Filee delete anything else.
    /// </summary>
    public void DeleteListFile()
    {
        if (ListFile is null || !IsOwnListFile(ListFile))
            return;
        try
        {
            File.Delete(ListFile);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>True for <c>%TEMP%\Filee-convert-*.txt</c>.</summary>
    internal static bool IsOwnListFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return Path.GetFileName(full).StartsWith(ListFilePrefix, StringComparison.OrdinalIgnoreCase)
                   && full.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(full) ?? ""),
                       Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), Filee.Core.Platform.FileSystemPaths.Comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>One path per line (UTF-8, with or without BOM); blank lines are skipped. Unreadable → empty.</summary>
    internal static IEnumerable<string> ReadListFile(string path)
    {
        try
        {
            return File.ReadAllLines(path, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
