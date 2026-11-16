using System.Drawing.Drawing2D;
using ContextMenuManager.Core;

namespace ContextMenuManager.UI.Controls;

/// <summary>A section title or a hint line in <see cref="EntryListBox"/> (not selectable).</summary>
internal sealed record ListHeader(string Text, bool IsHint = false);

/// <summary>
/// Owner-drawn list of the user's entries (with an on/off switch) and, optionally, the
/// read-only items other programs added.
/// </summary>
/// <remarks>
/// The whole list is painted in <see cref="OnPaint"/> into a double buffer, and hovering only
/// repaints the rows that change, so it doesn't flicker.
/// </remarks>
internal sealed class EntryListBox : ListBox
{
    private const int LogicalItemHeight = 52;
    private const int LogicalHeaderHeight = 34;
    private const int LogicalHintHeight = 44;

    private int _hoverIndex = -1;
    private bool _hoverToggle;
    private Font? _titleFont;
    private Font? _captionFont;
    private Font? _glyphFont;
    private Font? _headerFont;

    public EntryListBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        DrawMode = DrawMode.OwnerDrawVariable;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
    }

    /// <summary>Raised when the on/off switch of an entry is clicked.</summary>
    public event EventHandler<MenuEntry>? ToggleClicked;

    public MenuEntry? SelectedEntry => SelectedItem as MenuEntry;

    public ExternalItem? SelectedExternal => SelectedItem as ExternalItem;

    private int S(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnMeasureItem(MeasureItemEventArgs e)
    {
        base.OnMeasureItem(e);
        e.ItemHeight = e.Index >= 0 && e.Index < Items.Count && Items[e.Index] is ListHeader header
            ? S(header.IsHint ? LogicalHintHeight : LogicalHeaderHeight)
            : S(LogicalItemHeight);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _captionFont?.Dispose();
        _glyphFont?.Dispose();
        _headerFont?.Dispose();
        _titleFont = _captionFont = _glyphFont = _headerFont = null;
        base.OnFontChanged(e);
    }

    private Rectangle ToggleBounds(Rectangle item) =>
        new(item.Right - S(56), item.Top + (item.Height - S(20)) / 2, S(40), S(20));

    private int HitTest(Point point)
    {
        int index = IndexFromPoint(point);
        return index >= 0 && index < Items.Count && GetItemRectangle(index).Contains(point) ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location);
        if (index >= 0 && Items[index] is ListHeader)
        {
            index = -1;
        }

        bool overToggle = index >= 0 && Items[index] is MenuEntry && ToggleBounds(GetItemRectangle(index)).Contains(e.Location);
        Cursor = overToggle ? Cursors.Hand : Cursors.Default;
        if (index != _hoverIndex || overToggle != _hoverToggle)
        {
            InvalidateRow(_hoverIndex);
            _hoverIndex = index;
            _hoverToggle = overToggle;
            InvalidateRow(_hoverIndex);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateRow(_hoverIndex);
        _hoverIndex = -1;
        _hoverToggle = false;
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            Invalidate(GetItemRectangle(index));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = HitTest(e.Location);
        if (index >= 0 && Items[index] is ListHeader)
        {
            return; // Headers and hints can't be selected.
        }

        if (e.Button == MouseButtons.Right && index >= 0)
        {
            SelectedIndex = index; // Right-click selects, so the context menu acts on this row.
        }

        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && index >= 0 && Items[index] is MenuEntry entry
            && ToggleBounds(GetItemRectangle(index)).Contains(e.Location))
        {
            ToggleClicked?.Invoke(this, entry);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Skip over headers with the arrow keys.
        if (e.KeyCode is Keys.Up or Keys.Down && !e.Alt)
        {
            int step = e.KeyCode == Keys.Down ? 1 : -1;
            for (int i = SelectedIndex + step; i >= 0 && i < Items.Count; i += step)
            {
                if (Items[i] is not ListHeader)
                {
                    SelectedIndex = i;
                    break;
                }
            }

            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        using (var background = new SolidBrush(BackColor))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        for (int i = Math.Max(0, TopIndex); i < Items.Count; i++)
        {
            Rectangle bounds = GetItemRectangle(i);
            if (bounds.Top > ClientSize.Height)
            {
                break;
            }

            if (bounds.IntersectsWith(e.ClipRectangle))
            {
                DrawRow(g, i, bounds, i == SelectedIndex);
            }
        }
    }

    private void DrawRow(Graphics g, int index, Rectangle bounds, bool selected)
    {
        object item = Items[index];
        if (item is ListHeader header)
        {
            DrawHeader(g, header, bounds);
            return;
        }

        bool hover = index == _hoverIndex;
        int pad = S(2);
        var card = Rectangle.Inflate(bounds, -pad, -pad);
        bool isChild = item is MenuEntry { IsChild: true };
        if (isChild)
        {
            card = Rectangle.FromLTRB(card.Left + S(24), card.Top, card.Right, card.Bottom);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (selected || hover)
        {
            g.FillRoundedRectangle(selected ? Theme.SurfaceSelected : Theme.SurfaceHover, card, S(4));
        }

        if (selected)
        {
            int pillHeight = S(16);
            var pill = new RectangleF(card.Left, card.Top + (card.Height - pillHeight) / 2f, S(3), pillHeight);
            g.FillRoundedRectangle(Theme.Accent, pill, pill.Width / 2f);
        }

        if (isChild)
        {
            using var pen = new Pen(Theme.Border, S(1));
            int x = card.Left - S(12);
            int middle = bounds.Top + bounds.Height / 2;
            g.DrawLine(pen, x, bounds.Top, x, middle);
            g.DrawLine(pen, x, middle, card.Left - S(4), middle);
        }

        if (item is MenuEntry entry)
        {
            bool active = entry.Enabled && (!entry.IsChild || Items.OfType<MenuEntry>().FirstOrDefault(p => p.Id == entry.ParentId)?.Enabled != false);
            string glyph = entry.Kind == EntryKind.Submenu ? Glyphs.More : Glyphs.Play;
            string title = string.IsNullOrWhiteSpace(entry.Text) ? "(no name)" : entry.Text;
            Rectangle toggle = ToggleBounds(bounds);
            DrawContent(g, card, entry.EffectiveIcon, glyph, title, Describe(entry), active, toggle.Left - S(10));
            DrawToggle(g, toggle, entry.Enabled, hover && _hoverToggle);
        }
        else if (item is ExternalItem external)
        {
            string glyph = external.Kind == ExternalItemKind.ShellExtension ? Glyphs.Library
                : external.Kind == ExternalItemKind.Submenu ? Glyphs.More : Glyphs.Play;
            int right = card.Right - S(40);
            DrawContent(g, card, external.Icon, glyph, external.Text, Describe(external), active: true, right);

            _glyphFont ??= Theme.CreateGlyphFont(Font, 1.15f);
            TextRenderer.DrawText(g, Glyphs.Lock, _glyphFont, new Rectangle(right + S(8), card.Top, S(24), card.Height), Theme.TextDisabled,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawContent(Graphics g, Rectangle card, string icon, string glyph, string title, string caption, bool active, int right)
    {
        int iconSize = S(20);
        var iconBounds = new Rectangle(card.Left + S(12), card.Top + (card.Height - iconSize) / 2, iconSize, iconSize);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        _glyphFont ??= Theme.CreateGlyphFont(Font, 1.15f);
        if (IconLoader.Load(icon, iconSize) is { } bitmap)
        {
            if (active)
            {
                g.DrawImage(bitmap, iconBounds);
            }
            else
            {
                ControlPaint.DrawImageDisabled(g, bitmap, iconBounds.X, iconBounds.Y, BackColor);
            }
        }
        else
        {
            TextRenderer.DrawText(g, glyph, _glyphFont, iconBounds, active ? Theme.TextSecondary : Theme.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        int textX = iconBounds.Right + S(12);
        int textWidth = Math.Max(0, right - textX);
        _titleFont ??= new Font(Font.FontFamily, Font.Size);
        _captionFont ??= Theme.CreateCaptionFont(Font);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int titleHeight = TextRenderer.MeasureText(g, "Ag", _titleFont, Size.Empty, flags).Height;
        int captionHeight = TextRenderer.MeasureText(g, "Ag", _captionFont, Size.Empty, flags).Height;
        int top = card.Top + (card.Height - titleHeight - captionHeight) / 2;

        TextRenderer.DrawText(g, title, _titleFont, new Rectangle(textX, top, textWidth, titleHeight), active ? Theme.Text : Theme.TextSecondary, flags);
        TextRenderer.DrawText(g, caption, _captionFont, new Rectangle(textX, top + titleHeight, textWidth, captionHeight), Theme.TextSecondary, flags);
    }

    private void DrawHeader(Graphics g, ListHeader header, Rectangle bounds)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var text = Rectangle.FromLTRB(bounds.Left + S(8), bounds.Top, bounds.Right - S(8), bounds.Bottom);

        if (header.IsHint)
        {
            _captionFont ??= Theme.CreateCaptionFont(Font);
            TextRenderer.DrawText(g, header.Text, _captionFont, text, Theme.TextSecondary,
                TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        _headerFont ??= Theme.CreateStrongFont(Font, 0.95f);
        text = Rectangle.FromLTRB(text.Left, bounds.Bottom - S(26), text.Right, bounds.Bottom - S(4));
        TextRenderer.DrawText(g, header.Text, _headerFont, text, Theme.TextSecondary, flags | TextFormatFlags.Bottom);
    }

    private static string Describe(MenuEntry entry)
    {
        if (entry.IsChild)
        {
            return entry.Kind == EntryKind.Submenu ? "Submenu" : "Item in submenu";
        }

        var parts = new List<string>();
        if (entry.Kind == EntryKind.Submenu)
        {
            parts.Add("Submenu");
        }

        parts.Add(MenuEntry.DescribeLocations(entry.Locations));
        if (entry.Locations.HasFlag(MenuLocations.Files) && entry.FileTypeList.Count > 0)
        {
            parts.Add(string.Join(" ", entry.FileTypeList));
        }

        if (entry.ShiftOnly)
        {
            parts.Add("Shift");
        }

        return string.Join("  ·  ", parts);
    }

    private static string Describe(ExternalItem item)
    {
        var parts = new List<string>();
        if (item.Kind == ExternalItemKind.ShellExtension)
        {
            parts.Add("Extension");
        }
        else if (item.Kind == ExternalItemKind.Submenu)
        {
            parts.Add("Submenu");
        }

        parts.Add(MenuEntry.DescribeLocations(item.Locations));
        if (item.ShiftOnly)
        {
            parts.Add("Shift");
        }

        if (!string.IsNullOrEmpty(item.Publisher))
        {
            parts.Add(item.Publisher);
        }

        return string.Join("  ·  ", parts);
    }

    private static void DrawToggle(Graphics g, Rectangle bounds, bool on, bool hover)
    {
        var track = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1.5f, bounds.Height - 1.5f);
        float radius = track.Height / 2f;
        float knob = track.Height * (hover ? 0.6f : 0.5f);
        float margin = (track.Height - knob) / 2f;

        if (on)
        {
            g.FillRoundedRectangle(hover ? Theme.AccentHover : Theme.Accent, track, radius);
        }
        else
        {
            g.FillRoundedRectangle(hover ? Theme.SurfaceHover : Theme.Surface, track, radius);
            g.DrawRoundedRectangle(Theme.TextSecondary, track, radius);
        }

        float knobX = on ? track.Right - margin - knob : track.Left + margin;
        using var brush = new SolidBrush(on ? Theme.OnAccent : Theme.TextSecondary);
        g.FillEllipse(brush, knobX, track.Top + margin, knob, knob);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _captionFont?.Dispose();
            _glyphFont?.Dispose();
            _headerFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
