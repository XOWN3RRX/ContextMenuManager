namespace ContextMenuManager.Core;

/// <summary>Checks an entry before it is saved and explains problems in plain words.</summary>
public static class EntryValidator
{
    public static IReadOnlyList<string> Validate(MenuEntry entry)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(entry.Text))
        {
            problems.Add("Give the entry a name. It is the text you see in the menu.");
        }

        if (entry.Kind == EntryKind.Command)
        {
            if (string.IsNullOrWhiteSpace(entry.Program))
            {
                problems.Add("Choose the program to run when the entry is clicked.");
            }
            else if (entry.Program.Contains('"') && !(entry.Program.Trim().StartsWith('"') && entry.Program.Trim().EndsWith('"')))
            {
                problems.Add("The program path has a stray quote (\"). Remove it or quote the whole path.");
            }
        }

        if (!entry.IsChild)
        {
            if (entry.Locations == MenuLocations.None)
            {
                problems.Add("Pick at least one place where the entry should appear.");
            }

            if (entry.Locations.HasFlag(MenuLocations.Files) && entry.FileTypes.Trim().Length > 0 && entry.FileTypeList.Count == 0)
            {
                problems.Add("The file types aren't valid. Use extensions like .txt; .md");
            }
        }

        return problems;
    }

    /// <summary>Friendly warnings that don't block saving.</summary>
    public static IReadOnlyList<string> Warnings(MenuEntry entry)
    {
        var warnings = new List<string>();
        if (entry.Kind != EntryKind.Command || string.IsNullOrWhiteSpace(entry.Program))
        {
            return warnings;
        }

        string program = Environment.ExpandEnvironmentVariables(CommandBuilder.Unquote(entry.Program.Trim()));
        if (Path.IsPathFullyQualified(program) && !File.Exists(program))
        {
            warnings.Add("The program wasn't found at this path on this PC.");
        }

        if (!entry.Arguments.Contains(CommandBuilder.PathToken, StringComparison.OrdinalIgnoreCase)
            && !entry.Arguments.Contains("%1") && !entry.Arguments.Contains("%V", StringComparison.OrdinalIgnoreCase)
            && !entry.IsChild && entry.Locations != MenuLocations.Background)
        {
            warnings.Add("The arguments don't use {path}, so the program won't know which item you clicked.");
        }

        return warnings;
    }
}
