using System.ComponentModel;
using System.Diagnostics;
using ContextMenuManager.Core;
using ContextMenuManager.Preview;

namespace ContextMenuManager;

/// <summary>Runs an entry's command for a test, exactly as Explorer would after a click.</summary>
internal static class CommandRunner
{
    // Words that hint a command could close programs, end the session or delete things.
    private static readonly string[] s_riskyWords =
    [
        "shutdown", "logoff", "taskkill", "stop-computer", "restart-computer", "stop-process",
        "remove-item", " del ", " erase ", "rmdir", " rd ", "format", "reg delete", "reg.exe delete", "lockworkstation",
    ];

    /// <summary>The command line for a test run of <paramref name="entry"/> on <paramref name="target"/>.</summary>
    public static string BuildTestCommand(MenuEntry entry, PreviewTarget target) =>
        CommandBuilder.BuildPreviewCommand(entry, PreviewMenuBuilder.ToLocation(target), PreviewMenuBuilder.SamplePath(target));

    public static bool LooksRisky(string command)
    {
        string text = " " + command.ToLowerInvariant() + " ";
        return s_riskyWords.Any(text.Contains);
    }

    /// <summary>Runs the command. Returns an error message, or null when it started.</summary>
    public static string? Run(MenuEntry entry, PreviewTarget target)
    {
        string path = PreviewMenuBuilder.SamplePath(target);
        if (target == PreviewTarget.File)
        {
            EnsureSampleFile(path);
        }

        string command = Environment.ExpandEnvironmentVariables(BuildTestCommand(entry, target));
        (string fileName, string arguments) = CommandBuilder.Split(command);
        if (fileName.Length == 0)
        {
            return "The entry has no program to run.";
        }

        try
        {
            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = target == PreviewTarget.File ? Path.GetDirectoryName(path)! : path,
            };

            using Process? _ = Process.Start(startInfo);
            return null;
        }
        catch (Win32Exception ex)
        {
            return $"Windows couldn't start “{fileName}”: {ex.Message}";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private static void EnsureSampleFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "This is a sample file created by Context Menu Manager for test runs.\r\nIt is safe to delete.\r\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The command will report the missing file itself.
        }
    }
}

/// <summary>Restarts Windows Explorer so it picks up the classic/new menu setting.</summary>
internal static class ExplorerRestarter
{
    public static async Task RestartAsync()
    {
        int session = Process.GetCurrentProcess().SessionId;
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                if (process.SessionId != session)
                {
                    continue;
                }

                try
                {
                    process.Kill();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or TimeoutException)
                {
                    // Already gone or not ours.
                }
            }
        }

        // Windows usually restarts the shell by itself; start it if it didn't.
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (!Process.GetProcessesByName("explorer").Any(p => p.SessionId == session))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
        }
    }
}
