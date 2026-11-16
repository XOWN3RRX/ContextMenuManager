using System.Drawing.Drawing2D;
using ContextMenuManager.UI;
using ContextMenuManager.UI.Controls;

namespace ContextMenuManager.Preview;

/// <summary>A card that explains what clicking a menu item in the preview would do.</summary>
internal sealed class ResultBanner : Control
{
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string? _command;
    private Font? _titleFont;
    private Font? _monoFont;
    private Font? _glyphFont;

    public ResultBanner()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;

        RunButton = new ModernButton { Kind = ButtonKind.Accent, Glyph = Glyphs.Play, Text = "Run it now" };
        CopyButton = new ModernButton { Glyph = Glyphs.Copy, Text = "Copy command" };
        CloseButton = new ModernButton { Kind = ButtonKind.Subtle, Glyph = Glyphs.Close, TabStop = false, AccessibleName = "Close" };
        CloseButton.Click += (_, _) => Visible = false;
        CopyButton.Click += (_, _) =>
        {
            if (_command is not null)
            {
                Clipboard.SetText(_command);
                CopyButton.Text = "Copied";
            }
        };

        Controls.AddRange([RunButton, CopyButton, CloseButton]);
    }

    public ModernButton RunButton { get; }

    public ModernButton CopyButton { get; }

    public ModernButton CloseButton { get; }

    private int S(int logical) => LogicalToDeviceUnits(logical);

    /// <summary>Shows a message, optionally with the command line and a Run button.</summary>
    public void Show(string title, string message, string? command, bool canRun)
    {
        _title = title;
        _message = message;
        _command = string.IsNullOrWhiteSpace(command) ? null : command;
        RunButton.Visible = canRun && _command is not null;
        CopyButton.Visible = _command is not null;
        CopyButton.Text = "Copy command";
        PerformLayout();
        Invalidate();
    }

    public int PreferredHeight(int width)
    {
        // Matches OnPaint/OnLayout: padding, title, message, command box, buttons, padding.
        int height = S(16) + S(26) + MessageHeight(width) + S(8);
        if (_command is not null)
        {
            height += S(38) + S(12);
        }

        if (RunButton.Visible || CopyButton.Visible)
        {
            height += S(32);
        }

        return height + S(16);
    }

    private int MessageHeight(int width)
    {
        if (string.IsNullOrEmpty(_message))
        {
            return 0;
        }

        Size size = TextRenderer.MeasureText(_message, Font, new Size(Math.Max(1, width - S(72)), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return size.Height;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _monoFont?.Dispose();
        _glyphFont?.Dispose();
        _titleFont = _monoFont = _glyphFont = null;
        base.OnFontChanged(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width > 0 && Height > 0)
        {
            using GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0, 0, Width, Height), S(8));
            Region?.Dispose();
            Region = new Region(path);
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (CloseButton is null)
        {
            return;
        }

        int pad = S(16);
        CloseButton.Bounds = new Rectangle(Width - S(8) - S(30), S(8), S(30), S(30));

        int y = Height - pad - S(32);
        int x = Width - pad;
        foreach (ModernButton button in new[] { RunButton, CopyButton }.Where(b => b.Visible))
        {
            int width = S(button == RunButton ? 130 : 140);
            x -= width;
            button.Bounds = new Rectangle(x, y, width, S(32));
            x -= S(8);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        g.FillRoundedRectangle(Theme.Surface, bounds, S(8));
        g.DrawRoundedRectangle(Theme.Border, bounds, S(8));

        _titleFont ??= Theme.CreateStrongFont(Font, 1.05f);
        _monoFont ??= new Font(Theme.MonoFontName, Font.Size * 0.95f);
        _glyphFont ??= Theme.CreateGlyphFont(Font, 1.4f);

        int pad = S(16);
        TextRenderer.DrawText(g, Glyphs.Mouse, _glyphFont, new Rectangle(pad, pad, S(28), S(24)), Theme.Accent,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

        int textLeft = pad + S(36);
        int textWidth = Width - textLeft - S(44);
        TextRenderer.DrawText(g, _title, _titleFont, new Rectangle(textLeft, pad, textWidth, S(24)), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        int y = pad + S(26);
        int messageHeight = MessageHeight(Width);
        TextRenderer.DrawText(g, _message, Font, new Rectangle(textLeft, y, Width - textLeft - pad, messageHeight), Theme.TextSecondary,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        y += messageHeight + S(8);

        if (_command is not null)
        {
            var box = new Rectangle(textLeft, y, Width - textLeft - pad, S(38));
            g.FillRoundedRectangle(Theme.Well, box, S(4));
            TextRenderer.DrawText(g, _command, _monoFont, Rectangle.Inflate(box, -S(10), 0), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _monoFont?.Dispose();
            _glyphFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
