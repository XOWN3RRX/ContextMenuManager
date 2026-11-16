using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;

namespace ContextMenuManager.UI.Controls;

/// <summary>A rounded, themed text box with an optional leading glyph (used for search and text fields).</summary>
internal sealed class InputBox : Control
{
    private Font? _glyphFont;
    private string? _glyph;

    public InputBox(string placeholder = "", string? glyph = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        _glyph = glyph;

        TextBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            PlaceholderText = placeholder,
        };

        TextBox.GotFocus += (_, _) => Invalidate();
        TextBox.LostFocus += (_, _) => Invalidate();
        TextBox.TextChanged += (_, e) => OnTextChanged(e);
        Controls.Add(TextBox);
        Cursor = Cursors.IBeam;
        Height = 32;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextBox TextBox { get; }

    [AllowNull]
    public override string Text
    {
        get => TextBox.Text;
        set => TextBox.Text = value;
    }

    /// <summary>Uses a monospaced font, for command lines.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Monospace { get; set; }

    protected override void OnFontChanged(EventArgs e)
    {
        _glyphFont?.Dispose();
        _glyphFont = null;
        base.OnFontChanged(e);
        if (Monospace)
        {
            TextBox.Font = new Font(Theme.MonoFontName, Font.Size);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        TextBox.Enabled = Enabled;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        TextBox.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = LogicalToDeviceUnits(_glyph is null ? 10 : 34);
        int right = LogicalToDeviceUnits(10);
        TextBox.Bounds = new Rectangle(left, (Height - TextBox.Height) / 2, Math.Max(0, Width - left - right), TextBox.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float radius = LogicalToDeviceUnits(4);
        var bounds = new RectangleF(0, 0, Width, Height);
        g.FillRoundedRectangle(Theme.Surface, bounds, radius);
        g.DrawRoundedRectangle(Theme.Border, bounds, radius);

        if (TextBox.Focused)
        {
            // Fluent style: accent underline when focused.
            float thickness = LogicalToDeviceUnits(2);
            using var clip = Theme.RoundedRectangle(bounds, radius);
            g.SetClip(clip);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillRectangle(brush, 0, Height - thickness, Width, thickness);
            g.ResetClip();
        }

        if (_glyph is not null)
        {
            _glyphFont ??= Theme.CreateGlyphFont(Font, 1f);
            TextRenderer.DrawText(g, _glyph, _glyphFont,
                new Rectangle(LogicalToDeviceUnits(10), 0, LogicalToDeviceUnits(20), Height),
                Theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _glyphFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
