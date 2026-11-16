using System.Text;
using System.Text.RegularExpressions;

namespace ContextMenuManager.Core;

/// <summary>
/// Turns an entry's program and arguments into the command line Explorer runs, and back.
/// </summary>
/// <remarks>
/// Users write <c>{path}</c> for "the thing I right-clicked". Explorer uses <c>%V</c> for the
/// folder of a background click and <c>%1</c> for a clicked file, folder or drive.
/// </remarks>
public static partial class CommandBuilder
{
    public const string PathToken = "{path}";

    /// <summary>The Explorer placeholder that <see cref="PathToken"/> becomes for a location.</summary>
    public static string ShellPlaceholder(MenuLocations location) => location == MenuLocations.Background ? "%V" : "%1";

    /// <summary>The command line that is stored in the registry for one location.</summary>
    public static string BuildRegistryCommand(MenuEntry entry, MenuLocations location)
    {
        string program = Quote(entry.Program.Trim());
        string arguments = ReplacePathToken(entry.Arguments.Trim(), ShellPlaceholder(location));
        return arguments.Length == 0 ? program : program + " " + arguments;
    }

    /// <summary>
    /// The command line with every placeholder replaced by <paramref name="path"/>, as Explorer
    /// would run it after the user clicked the entry.
    /// </summary>
    public static string BuildPreviewCommand(MenuEntry entry, MenuLocations location, string path)
    {
        string command = BuildRegistryCommand(entry, location);
        return ShellPlaceholderRegex().Replace(command, path.Replace("$", "$$"));
    }

    /// <summary>Splits a command line into the program and the rest, for <c>Process.Start</c>.</summary>
    public static (string FileName, string Arguments) Split(string commandLine)
    {
        string text = commandLine.Trim();
        if (text.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (text[0] == '"')
        {
            int end = text.IndexOf('"', 1);
            if (end > 0)
            {
                return (text[1..end], text[(end + 1)..].Trim());
            }

            return (text.Trim('"'), string.Empty);
        }

        int space = text.IndexOf(' ');
        return space < 0 ? (text, string.Empty) : (text[..space], text[(space + 1)..].Trim());
    }

    /// <summary>Adds quotes around a program path that contains spaces (and isn't quoted already).</summary>
    public static string Quote(string program)
    {
        if (program.Length == 0 || program.StartsWith('"'))
        {
            return program;
        }

        return program.Contains(' ') || program.Contains('%') ? "\"" + program + "\"" : program;
    }

    public static string Unquote(string program) =>
        program.Length >= 2 && program[0] == '"' && program[^1] == '"' ? program[1..^1] : program;

    public static bool ContainsEnvironmentVariable(string value) => EnvironmentVariableRegex().IsMatch(value);

    private static string ReplacePathToken(string arguments, string placeholder)
    {
        var builder = new StringBuilder(arguments.Length + 8);
        int index = 0;
        while (true)
        {
            int next = arguments.IndexOf(PathToken, index, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
            {
                builder.Append(arguments, index, arguments.Length - index);
                return builder.ToString();
            }

            builder.Append(arguments, index, next - index).Append(placeholder);
            index = next + PathToken.Length;
        }
    }

    // %1, %V, %v, %L, %W: the shell placeholders we show as the clicked item in previews.
    [GeneratedRegex("%[1vVlLwW](?![A-Za-z0-9_])")]
    private static partial Regex ShellPlaceholderRegex();

    [GeneratedRegex("%[A-Za-z_][A-Za-z0-9_()]*%")]
    private static partial Regex EnvironmentVariableRegex();
}
