using ContextMenuManager.Core;
using ContextMenuManager.UI;
using ContextMenuManager.UI.Controls;

namespace ContextMenuManager;

/// <summary>App settings, data and about. Changes apply immediately.</summary>
internal sealed class SettingsForm : Form
{
    private readonly List<Control> _items = [];
    private readonly ModernButton _doneButton;
    private readonly ToggleSwitch _classicToggle;

    public SettingsForm(AppServices services, Action removeAll)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Context Menu Manager settings";
        Icon = Program.AppIcon;
        Theme.ApplyTo(this);

        bool windows11 = ClassicContextMenu.IsWindows11;
        _classicToggle = new ToggleSwitch { Checked = windows11 && ClassicContextMenu.IsEnabled, Enabled = windows11 && !services.IsSandbox };
        _classicToggle.CheckedChanged += (_, _) => SetClassicMenu(_classicToggle.Checked);

        var restart = new ModernButton { Text = "Restart", Size = new Size(120, 32), Enabled = !services.IsSandbox };
        restart.Click += async (_, _) =>
        {
            restart.Enabled = false;
            await ExplorerRestarter.RestartAsync();
            restart.Enabled = true;
        };

        var openFolder = new ModernButton { Text = "Open", Size = new Size(120, 32) };
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(services.Store.DataDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{services.Store.DataDirectory}\"") { UseShellExecute = true })?.Dispose();
        };

        var remove = new ModernButton { Text = "Remove", Size = new Size(120, 32) };
        remove.Click += (_, _) => removeAll();

        var github = new ModernButton { Glyph = Glyphs.Link, Text = "GitHub", Size = new Size(120, 32) };
        github.Click += (_, _) => MainForm.OpenUrl(Program.RepositoryUrl);

        Heading("Context menu");
        _items.Add(new SettingRow(
            "Full context menu on Windows 11",
            windows11 ? "Skip “Show more options” and always show every item" : "Only needed on Windows 11",
            _classicToggle));
        _items.Add(new SettingRow("Restart Explorer", "Applies the setting above right away (the taskbar blinks)", restart));

        Heading("Your data");
        _items.Add(new SettingRow("Data folder", services.Store.DataDirectory, openFolder));
        _items.Add(new SettingRow("Remove everything this app added", "Takes all your entries out of the Windows menu", remove));

        Heading("About");
        _items.Add(new SettingRow(
            $"Context Menu Manager {Program.Version}",
            "Free and open source (MIT). No ads, no tracking, no internet access.",
            github));

        _doneButton = new ModernButton { Kind = ButtonKind.Accent, Text = "Done", DialogResult = DialogResult.OK };
        AcceptButton = _doneButton;
        CancelButton = _doneButton;

        Controls.AddRange([.. _items, _doneButton]);
        ResumeLayout(false);

        void Heading(string text) =>
            _items.Add(new Label { Text = text, AutoSize = false, ForeColor = Theme.Text, BackColor = Theme.Background });
    }

    private int S(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        foreach (Label label in _items.OfType<Label>())
        {
            label.Font = Theme.CreateStrongFont(Font, 1.3f);
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_doneButton is null)
        {
            return;
        }

        int pad = S(20);
        int width = ClientSize.Width - 2 * pad;
        int y = pad;
        foreach (Control item in _items)
        {
            if (item is Label && y > pad)
            {
                y += S(14);
            }

            item.Bounds = new Rectangle(pad, y, width, item is Label ? S(32) : S(62));
            y = item.Bottom + (item is Label ? S(6) : S(4));
        }

        _doneButton.Bounds = new Rectangle(ClientSize.Width - pad - S(110), ClientSize.Height - pad - S(34), S(110), S(34));
    }

    private async void SetClassicMenu(bool enabled)
    {
        try
        {
            ClassicContextMenu.SetEnabled(enabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            TaskDialog.ShowDialog(this, new TaskDialogPage { Caption = "Settings", Heading = "The setting couldn't be changed", Text = ex.Message, Icon = TaskDialogIcon.Error });
            return;
        }

        var page = new TaskDialogPage
        {
            Caption = "Settings",
            Heading = "Restart Explorer now?",
            Text = "Windows Explorer needs to restart before the change shows up. Your open folder windows will close and the taskbar will blink.",
            Icon = TaskDialogIcon.Information,
            Buttons = { new TaskDialogButton("Restart now"), new TaskDialogButton("Later") },
        };

        if (TaskDialog.ShowDialog(this, page).Text == "Restart now")
        {
            await ExplorerRestarter.RestartAsync();
        }
    }
}
