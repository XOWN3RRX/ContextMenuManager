using System.Drawing.Drawing2D;
using ContextMenuManager.UI;
using ContextMenuManager.UI.Controls;

namespace ContextMenuManager.Preview;

/// <summary>The card around the live preview: a header with options and the pretend desktop.</summary>
internal sealed class PreviewCard : Control
{
    private Font? _titleFont;
    private Font? _captionFont;
    private readonly Label _shiftLabel;

    public PreviewCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;

        StyleSelector = new SegmentedControl("Windows 11", "Windows 10") { BackColor = Theme.Surface };
        ShiftToggle = new ToggleSwitch { BackColor = Theme.Surface, AccessibleName = "Hold Shift" };
        _shiftLabel = new Label
        {
            Text = "Hold Shift",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Surface,
        };
        _shiftLabel.Click += (_, _) => ShiftToggle.Checked = !ShiftToggle.Checked;
        Scene = new DesktopScene { AccessibleName = "Live preview desktop", AccessibleDescription = "Right-click to open the simulated context menu" };

        Controls.AddRange([StyleSelector, _shiftLabel, ShiftToggle, Scene]);
    }

    public SegmentedControl StyleSelector { get; }

    public ToggleSwitch ShiftToggle { get; }

    public DesktopScene Scene { get; }

    private int HeaderHeight => LogicalToDeviceUnits(60);

    private int S(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _captionFont?.Dispose();
        _titleFont = _captionFont = null;
        base.OnFontChanged(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Scene is null)
        {
            return;
        }

        int pad = S(12);
        int right = Width - S(16);
        int controlHeight = S(32);
        int top = (HeaderHeight - controlHeight) / 2;

        ShiftToggle.Bounds = new Rectangle(right - S(40), (HeaderHeight - S(20)) / 2, S(40), S(20));
        _shiftLabel.Bounds = new Rectangle(ShiftToggle.Left - S(80), top, S(74), controlHeight);
        int selectorWidth = StyleSelector.PreferredWidth;
        StyleSelector.Bounds = new Rectangle(_shiftLabel.Left - S(12) - selectorWidth, top, selectorWidth, controlHeight);

        Scene.Bounds = Rectangle.FromLTRB(pad, HeaderHeight, Math.Max(pad, Width - pad), Math.Max(HeaderHeight, Height - pad));
        if (Scene.Width > 0 && Scene.Height > 0)
        {
            using GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0, 0, Scene.Width, Scene.Height), S(6));
            Scene.Region?.Dispose();
            Scene.Region = new Region(path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0, 0, Width, Height);
        g.FillRoundedRectangle(Theme.Surface, bounds, S(8));
        g.DrawRoundedRectangle(Theme.Border, bounds, S(8));

        _titleFont ??= Theme.CreateStrongFont(Font, 1.15f);
        _captionFont ??= Theme.CreateCaptionFont(Font);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int pad = S(16);
        int width = Math.Max(0, StyleSelector.Left - pad * 2);
        int titleHeight = TextRenderer.MeasureText(g, "Ag", _titleFont, Size.Empty, flags).Height;
        int captionHeight = TextRenderer.MeasureText(g, "Ag", _captionFont, Size.Empty, flags).Height;
        int top = (HeaderHeight - titleHeight - captionHeight) / 2;

        TextRenderer.DrawText(g, "Live preview", _titleFont, new Rectangle(pad, top, width, titleHeight), Theme.Text, flags);
        TextRenderer.DrawText(g, "Try your menu here. Nothing runs unless you choose “Run it now”.", _captionFont,
            new Rectangle(pad, top + titleHeight, width, captionHeight), Theme.TextSecondary, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _captionFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
