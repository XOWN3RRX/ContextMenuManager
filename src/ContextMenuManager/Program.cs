using ContextMenuManager.Core;
using ContextMenuManager.UI;

namespace ContextMenuManager;

internal static class Program
{
    public const string RepositoryUrl = "https://github.com/XOWN3RRX/ContextMenuManager";

    private static Icon? s_appIcon;

    public static Icon AppIcon => s_appIcon ??= LoadAppIcon();

    public static string Version =>
        typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    [STAThread]
    private static void Main(string[] args)
    {
        // --sandbox: try everything without touching the real context menu (see CONTRIBUTING.md).
        bool sandbox = args.Any(a => string.Equals(a, "--sandbox", StringComparison.OrdinalIgnoreCase));
        string suffix = sandbox ? ".Sandbox" : string.Empty;

        using var mutex = new Mutex(initiallyOwned: true, @"Local\ContextMenuManager.SingleInstance" + suffix, out bool isFirstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ContextMenuManager.Show" + suffix);

        if (!isFirstInstance)
        {
            // Already running: bring that window to the front. Two instances could overwrite each other's changes.
            Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.ASFW_ANY);
            showEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Theme.Initialize();

#pragma warning disable WFO5001 // Dark mode for common controls (scroll bars, text boxes, dialogs).
        Application.SetColorMode(Theme.IsDark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001

        string dataDirectory = sandbox
            ? Path.Combine(Path.GetTempPath(), "ContextMenuManager.Sandbox")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ContextMenuManager");

        var services = new AppServices(
            new EntryStore(dataDirectory),
            sandbox ? new RegistryWriter(@"Software\ContextMenuManager.Sandbox\Classes", notifyShell: false) : new RegistryWriter(),
            AppSettings.Load(dataDirectory),
            sandbox);

        services.Store.Load();

        var mainForm = new MainForm(services);

        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) =>
        {
            if (mainForm.IsHandleCreated && !mainForm.IsDisposed)
            {
                mainForm.BeginInvoke(mainForm.BringToFrontFromOtherInstance);
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(mainForm);
    }

    private static Icon LoadAppIcon()
    {
        using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream("ContextMenuManager.AppIcon.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }
}

/// <summary>Everything the windows share: the entry list, the registry writer and settings.</summary>
internal sealed record AppServices(EntryStore Store, RegistryWriter Writer, AppSettings Settings, bool IsSandbox);
