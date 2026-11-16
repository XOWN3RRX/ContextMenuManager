using ContextMenuManager.Core;
using ContextMenuManager.UI;
using ContextMenuManager.UI.Controls;

namespace ContextMenuManager;

/// <summary>Ready-made entries, grouped by category. "Add" puts one in the menu right away.</summary>
internal sealed class TemplatesForm : Form
{
    private readonly Label _heading;
    private readonly Label _subheading;
    private readonly Panel _scroll;
    private readonly List<Control> _items = [];
    private readonly ModernButton _doneButton;

    public TemplatesForm(Action<IReadOnlyList<MenuEntry>> add)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 700);
        MinimumSize = new Size(520, 420);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Templates";
        Icon = Program.AppIcon;
        Theme.ApplyTo(this);

        _heading = new Label { Text = "Templates", AutoSize = false, ForeColor = Theme.Text, BackColor = Theme.Background };
        _subheading = new Label
        {
            Text = "Ready-made entries. Click Add and the entry is in your menu right away; you can edit or remove it later.",
            AutoSize = false,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Background,
        };
        _scroll = new Panel { AutoScroll = true, BackColor = Theme.Background };

        foreach (IGrouping<string, EntryTemplate> group in Templates.All.GroupBy(t => t.Category))
        {
            var label = new Label { Text = group.Key, AutoSize = false, ForeColor = Theme.Text, BackColor = Theme.Background, Tag = "heading" };
            _items.Add(label);

            foreach (EntryTemplate template in group)
            {
                bool available = template.IsAvailable;
                var button = new ModernButton
                {
                    Kind = available ? ButtonKind.Accent : ButtonKind.Standard,
                    Glyph = available ? Glyphs.Add : null,
                    Text = available ? "Add" : "Not installed",
                    Size = new Size(120, 32),
                    Enabled = available,
                };

                button.Click += (_, _) =>
                {
                    add(Templates.Instantiate(template));
                    button.Text = "Added";
                    button.Glyph = Glyphs.CheckMark;
                    button.Kind = ButtonKind.Standard;
                    button.Invalidate();
                };

                string description = available || template.RequiredProgram is null
                    ? template.Description
                    : $"Needs {template.RequiredProgram}, which wasn't found on this PC.";
                _items.Add(new SettingRow(template.Name, description, button));
            }
        }

        _doneButton = new ModernButton { Kind = ButtonKind.Standard, Text = "Done", DialogResult = DialogResult.OK };
        AcceptButton = _doneButton;
        CancelButton = _doneButton;

        _scroll.Controls.AddRange([.. _items]);
        Controls.AddRange([_heading, _subheading, _scroll, _doneButton]);
        ResumeLayout(false);
    }

    private int S(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _heading.Font = Theme.CreateStrongFont(Font, 1.45f);
        foreach (Label label in _items.OfType<Label>())
        {
            label.Font = Theme.CreateStrongFont(Font, 1.1f);
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
        _heading.Bounds = new Rectangle(pad, pad, width, S(36));
        _subheading.Bounds = new Rectangle(pad, _heading.Bottom, width, S(40));
        _doneButton.Bounds = new Rectangle(ClientSize.Width - pad - S(110), ClientSize.Height - pad - S(34), S(110), S(34));
        _scroll.Bounds = Rectangle.FromLTRB(pad, _subheading.Bottom + S(4), ClientSize.Width - pad + S(12), _doneButton.Top - S(12));

        int rowWidth = _scroll.ClientSize.Width - S(14);
        int y = _scroll.AutoScrollPosition.Y;
        foreach (Control item in _items)
        {
            bool heading = item is Label;
            if (heading && y > _scroll.AutoScrollPosition.Y)
            {
                y += S(12);
            }

            item.Bounds = new Rectangle(0, y, rowWidth, heading ? S(30) : S(62));
            y = item.Bottom + S(4);
        }

        _scroll.AutoScrollMinSize = new Size(0, y - _scroll.AutoScrollPosition.Y);
    }
}
