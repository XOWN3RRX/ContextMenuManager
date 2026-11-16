using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ContextMenuManager.UI.Controls;

/// <summary>A settings card: title and description on the left, a control on the right.</summary>
internal sealed class SettingRow : Control
{
    private Font? _captionFont;

    public SettingRow(string title, string description, Control action)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Text = title;
        Description = description;
        Action = action;
        Controls.Add(action);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Description { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control Action { get; }

    /// <summary>
    /// When above zero, the text column gets this many logical pixels and the action fills
    /// the rest of the row (used for text fields).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LabelWidth { get; set; }

    /// <summary>Controls placed left of the action (for example a Browse button next to a text field).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<Control> Extras { get; } = [];

    public SettingRow WithExtras(params Control[] extras)
    {
        Extras.AddRange(extras);
        Controls.AddRange(extras);
        return this;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _captionFont?.Dispose();
        _captionFont = null;
        base.OnFontChanged(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int pad = LogicalToDeviceUnits(16);
        int gap = LogicalToDeviceUnits(6);
        int right = Width - pad;

        for (int i = Extras.Count - 1; i >= 0; i--)
        {
            Control extra = Extras[i];
            extra.Location = new Point(right - extra.Width, (Height - extra.Height) / 2);
            right = extra.Left - gap;
        }

        if (LabelWidth > 0)
        {
            int left = LogicalToDeviceUnits(LabelWidth);
            Action.Width = Math.Max(LogicalToDeviceUnits(60), right - left);
        }

        Action.Location = new Point(right - Action.Width, (Height - Action.Height) / 2);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float radius = LogicalToDeviceUnits(6);
        var bounds = new RectangleF(0, 0, Width, Height);
        g.FillRoundedRectangle(Theme.Surface, bounds, radius);
        g.DrawRoundedRectangle(Theme.Border, bounds, radius);

        _captionFont ??= Theme.CreateCaptionFont(Font);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int pad = LogicalToDeviceUnits(16);
        int textRight = LabelWidth > 0 ? LogicalToDeviceUnits(LabelWidth) - LogicalToDeviceUnits(8) : Action.Left - pad;
        int width = Math.Max(0, textRight - pad);
        int titleHeight = TextRenderer.MeasureText(g, "Ag", Font, Size.Empty, flags).Height;
        int captionHeight = TextRenderer.MeasureText(g, "Ag", _captionFont, Size.Empty, flags).Height;
        int top = (Height - titleHeight - captionHeight) / 2;

        TextRenderer.DrawText(g, Text, Font, new Rectangle(pad, top, width, titleHeight), Enabled ? Theme.Text : Theme.TextDisabled, flags);
        TextRenderer.DrawText(g, Description, _captionFont, new Rectangle(pad, top + titleHeight, width, captionHeight), Enabled ? Theme.TextSecondary : Theme.TextDisabled, flags);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captionFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
