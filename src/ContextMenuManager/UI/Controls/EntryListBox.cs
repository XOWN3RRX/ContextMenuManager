using System.Drawing.Drawing2D;
using ContextMenuManager.Core;

namespace ContextMenuManager.UI.Controls;

/// <summary>Owner-drawn list of the user's entries: icon, name, where it shows, and an on/off switch.</summary>
internal sealed class EntryListBox : ListBox
{
    private const int LogicalItemHeight = 52;

    private int _hoverIndex = -1;
    private bool _hoverToggle;
    private Font? _titleFont;
    private Font? _captionFont;
    private Font? _glyphFont;

    public EntryListBox()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        ItemHeight = LogicalItemHeight;
    }

    /// <summary>Raised when the on/off switch of an entry is clicked.</summary>
    public event EventHandler<MenuEntry>? ToggleClicked;

    public MenuEntry? SelectedEntry => SelectedItem as MenuEntry;

    private int S(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = S(LogicalItemHeight);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ItemHeight = S(LogicalItemHeight);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _captionFont?.Dispose();
        _glyphFont?.Dispose();
        _titleFont = _captionFont = _glyphFont = null;
        base.OnFontChanged(e);
    }

    private Rectangle ToggleBounds(Rectangle item) =>
        new(item.Right - S(56), item.Top + (item.Height - S(20)) / 2, S(40), S(20));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = IndexFromPoint(e.Location);
        if (index >= 0 && !GetItemRectangle(index).Contains(e.Location))
        {
            index = -1;
        }

        bool overToggle = index >= 0 && ToggleBounds(GetItemRectangle(index)).Contains(e.Location);
        Cursor = overToggle ? Cursors.Hand : Cursors.Default;
        if (index != _hoverIndex || overToggle != _hoverToggle)
        {
            _hoverIndex = index;
            _hoverToggle = overToggle;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        _hoverToggle = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Right && index >= 0 && GetItemRectangle(index).Contains(e.Location))
        {
            SelectedIndex = index; // Right-click selects, so the context menu acts on this entry.
        }

        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && index >= 0 && Items[index] is MenuEntry entry
            && ToggleBounds(GetItemRectangle(index)).Contains(e.Location))
        {
            ToggleClicked?.Invoke(this, entry);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        Graphics g = e.Graphics;
        using (var background = new SolidBrush(BackColor))
        {
            g.FillRectangle(background, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not MenuEntry entry)
        {
            return;
        }

        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool hover = e.Index == _hoverIndex;
        int pad = S(2);
        var card = Rectangle.Inflate(e.Bounds, -pad, -pad);
        if (entry.IsChild)
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

        if (entry.IsChild)
        {
            using var pen = new Pen(Theme.Border, S(1));
            int x = card.Left - S(12);
            g.DrawLine(pen, x, e.Bounds.Top, x, e.Bounds.Top + e.Bounds.Height / 2);
            g.DrawLine(pen, x, e.Bounds.Top + e.Bounds.Height / 2, card.Left - S(4), e.Bounds.Top + e.Bounds.Height / 2);
        }

        bool active = entry.Enabled && (!entry.IsChild || Items.OfType<MenuEntry>().FirstOrDefault(p => p.Id == entry.ParentId)?.Enabled != false);
        int iconSize = S(20);
        int x0 = card.Left + S(12);
        var iconBounds = new Rectangle(x0, card.Top + (card.Height - iconSize) / 2, iconSize, iconSize);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        _glyphFont ??= Theme.CreateGlyphFont(Font, 1.15f);
        Bitmap? icon = IconLoader.Load(entry.EffectiveIcon, iconSize);
        if (icon is not null)
        {
            if (active)
            {
                g.DrawImage(icon, iconBounds);
            }
            else
            {
                ControlPaint.DrawImageDisabled(g, icon, iconBounds.X, iconBounds.Y, BackColor);
            }
        }
        else
        {
            string glyph = entry.Kind == EntryKind.Submenu ? Glyphs.More : Glyphs.Play;
            TextRenderer.DrawText(g, glyph, _glyphFont, iconBounds, active ? Theme.TextSecondary : Theme.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        Rectangle toggle = ToggleBounds(e.Bounds);
        int textX = iconBounds.Right + S(12);
        int textWidth = Math.Max(0, toggle.Left - S(10) - textX);
        _titleFont ??= new Font(Font.FontFamily, Font.Size);
        _captionFont ??= Theme.CreateCaptionFont(Font);

        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int titleHeight = TextRenderer.MeasureText(g, "Ag", _titleFont, Size.Empty, flags).Height;
        int captionHeight = TextRenderer.MeasureText(g, "Ag", _captionFont, Size.Empty, flags).Height;
        int top = card.Top + (card.Height - titleHeight - captionHeight) / 2;

        string title = string.IsNullOrWhiteSpace(entry.Text) ? "(no name)" : entry.Text;
        TextRenderer.DrawText(g, title, _titleFont, new Rectangle(textX, top, textWidth, titleHeight), active ? Theme.Text : Theme.TextSecondary, flags);
        TextRenderer.DrawText(g, Describe(entry), _captionFont, new Rectangle(textX, top + titleHeight, textWidth, captionHeight), Theme.TextSecondary, flags);

        DrawToggle(g, toggle, entry.Enabled, hover && _hoverToggle);
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
        }

        base.Dispose(disposing);
    }
}
