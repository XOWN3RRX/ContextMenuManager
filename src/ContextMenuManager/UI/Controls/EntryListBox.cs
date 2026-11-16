using System.Drawing.Drawing2D;
using ContextMenuManager.Core;

namespace ContextMenuManager.UI.Controls;

/// <summary>A section title or a hint line in <see cref="EntryListBox"/> (not selectable).</summary>
internal sealed record ListHeader(string Text, bool IsHint = false);

/// <summary>Where a dragged entry was dropped: into <see cref="ParentId"/> (null = top level), in front of <see cref="Before"/> (null = at the end).</summary>
internal sealed class EntryDropEventArgs(MenuEntry entry, Guid? parentId, MenuEntry? before) : EventArgs
{
    public MenuEntry Entry { get; } = entry;

    public Guid? ParentId { get; } = parentId;

    public MenuEntry? Before { get; } = before;
}

/// <summary>
/// Owner-drawn list of the user's entries (with an on/off switch) and, optionally, the
/// read-only items other programs added.
/// </summary>
/// <remarks>
/// The whole list is painted in <see cref="OnPaint"/> into a double buffer, and hovering only
/// repaints the rows that change, so it doesn't flicker.
/// <para>
/// Entries can be reordered by dragging them. The drag is handled here, not with OLE drag and
/// drop: while the button is down the mouse messages are kept from the native list box (which
/// would otherwise move the selection along with the mouse), and a line shows where the entry
/// will land. Dragging a command a little to the right at the end of a submenu puts it inside.
/// </para>
/// </remarks>
internal sealed class EntryListBox : ListBox
{
    private const int LogicalItemHeight = 52;
    private const int LogicalHeaderHeight = 34;
    private const int LogicalHintHeight = 44;
    private const int LogicalChildIndent = 24;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_CAPTURECHANGED = 0x0215;

    private int _hoverIndex = -1;
    private bool _hoverToggle;
    private Font? _titleFont;
    private Font? _captionFont;
    private Font? _glyphFont;
    private Font? _headerFont;

    /// <summary>The entry the left button went down on (a possible drag), until the button is released.</summary>
    private MenuEntry? _pressEntry;
    private Point _pressPoint;
    private bool _dragging;
    private bool _dragCancelled;
    private Point _dragPoint;
    private DropTarget? _dropTarget;
    private readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 60 };

    /// <summary>A place a dragged entry can go; <see cref="Y"/> is where the line is drawn.</summary>
    private sealed record DropTarget(int Y, bool Indented, Guid? ParentId, MenuEntry? Before);

    public EntryListBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        DrawMode = DrawMode.OwnerDrawVariable;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        _scrollTimer.Tick += (_, _) => AutoScrollWhileDragging();
    }

    /// <summary>Raised when the on/off switch of an entry is clicked.</summary>
    public event EventHandler<MenuEntry>? ToggleClicked;

    /// <summary>Raised when an entry is dragged to a new place.</summary>
    public event EventHandler<EntryDropEventArgs>? EntryDropped;

    /// <summary>Whether entries can be dragged (off while the list is filtered, where gaps hide rows).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowReorder { get; set; } = true;

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

    /// <summary>The card a row is drawn in: inset a little, and indented for items in a submenu.</summary>
    private Rectangle CardBounds(Rectangle item, bool isChild)
    {
        var card = Rectangle.Inflate(item, -S(2), -S(2));
        return isChild ? Rectangle.FromLTRB(card.Left + S(LogicalChildIndent), card.Top, card.Right, card.Bottom) : card;
    }

    /// <summary>The drag handle (a grid of dots) at the left of an entry's card.</summary>
    private Rectangle GripBounds(Rectangle card) =>
        new(card.Left + S(5), card.Top + (card.Height - S(16)) / 2, S(10), S(16));

    private bool OverGrip(int index, Point point) =>
        AllowReorder && Items[index] is MenuEntry entry && GripBounds(CardBounds(GetItemRectangle(index), entry.IsChild)).Contains(point);

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
        Cursor = overToggle ? Cursors.Hand : index >= 0 && OverGrip(index, e.Location) ? Cursors.SizeAll : Cursors.Default;
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

        if (e.Button == MouseButtons.Left && index >= 0 && Items[index] is MenuEntry entry)
        {
            if (ToggleBounds(GetItemRectangle(index)).Contains(e.Location))
            {
                ToggleClicked?.Invoke(this, entry);
            }
            else if (AllowReorder && e.Clicks == 1)
            {
                _pressEntry = entry;
                _pressPoint = e.Location;
                _dragging = _dragCancelled = false;
            }
        }
    }

    // ---------------------------------------------------------------- drag to reorder

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEMOVE when _pressEntry is not null:
                // Not passed on: the native list box would select the rows the mouse passes over.
                TrackDrag(PointFromLParam(m.LParam));
                return;

            case WM_LBUTTONUP when _pressEntry is not null:
                MenuEntry entry = _pressEntry;
                DropTarget? drop = _dragging ? _dropTarget : null;
                bool moved = _dragging || _dragCancelled;
                Point press = _pressPoint;
                EndDrag();

                if (moved)
                {
                    // Let the list box finish the click where it started, so the selection stays put.
                    m.LParam = (nint)((press.Y << 16) | (press.X & 0xFFFF));
                }

                base.WndProc(ref m);
                if (drop is not null)
                {
                    EntryDropped?.Invoke(this, new EntryDropEventArgs(entry, drop.ParentId, drop.Before));
                }

                return;

            case WM_CAPTURECHANGED when _pressEntry is not null && m.LParam != Handle:
                EndDrag(); // Another window took the mouse (for example Alt+Tab).
                break;
        }

        base.WndProc(ref m);
    }

    private static Point PointFromLParam(nint lParam) => new((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));

    private void TrackDrag(Point point)
    {
        if (_pressEntry is null || _dragCancelled)
        {
            return;
        }

        if (!_dragging)
        {
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(point.X - _pressPoint.X) <= threshold.Width && Math.Abs(point.Y - _pressPoint.Y) <= threshold.Height)
            {
                return;
            }

            _dragging = true;
            InvalidateRow(_hoverIndex);
            _hoverIndex = -1;
            _hoverToggle = false;
            _scrollTimer.Start();
        }

        _dragPoint = point;
        UpdateDropTarget();
    }

    private void UpdateDropTarget()
    {
        bool inside = _dragPoint.X >= 0 && _dragPoint.X < ClientSize.Width;
        DropTarget? target = inside && _pressEntry is not null ? FindDropTarget(_pressEntry, _dragPoint) : null;
        Cursor = target is null ? Cursors.No : Cursors.Default;
        if (target != _dropTarget)
        {
            InvalidateIndicator(_dropTarget);
            _dropTarget = target;
            InvalidateIndicator(_dropTarget);
        }
    }

    private void AutoScrollWhileDragging()
    {
        if (!_dragging)
        {
            return;
        }

        int edge = S(28);
        int step = _dragPoint.Y < edge ? -1 : _dragPoint.Y > ClientSize.Height - edge ? 1 : 0;
        int top = Math.Clamp(TopIndex + step, 0, Math.Max(0, Items.Count - 1));
        if (step != 0 && top != TopIndex)
        {
            TopIndex = top;
            _dropTarget = null;
            Invalidate();
            UpdateDropTarget();
        }
    }

    private void EndDrag()
    {
        _scrollTimer.Stop();
        InvalidateIndicator(_dropTarget);
        _pressEntry = null;
        _dropTarget = null;
        _dragging = _dragCancelled = false;
        Cursor = Cursors.Default;
    }

    /// <summary>Stops the drag but keeps ignoring the mouse until the button is released.</summary>
    private void CancelDrag()
    {
        _scrollTimer.Stop();
        InvalidateIndicator(_dropTarget);
        _dropTarget = null;
        _dragging = false;
        _dragCancelled = true;
        Cursor = Cursors.Default;
    }

    /// <summary>
    /// The place nearest to <paramref name="point"/> where <paramref name="dragged"/> can go. Between
    /// the rows of the list there are gaps; each gap allows a top-level place, a place in a
    /// submenu, or both (at the end of a submenu, where the mouse's X position decides).
    /// </summary>
    private DropTarget? FindDropTarget(MenuEntry dragged, Point point)
    {
        int first = -1, last = -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is MenuEntry)
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        int draggedIndex = Items.IndexOf(dragged);
        if (first < 0 || draggedIndex < 0)
        {
            return null;
        }

        // A submenu takes its items along, so the gaps inside it are not places it can go.
        int blockEnd = draggedIndex + 1;
        while (!dragged.IsChild && blockEnd <= last && Items[blockEnd] is MenuEntry { IsChild: true })
        {
            blockEnd++;
        }

        bool wantsIndent = point.X >= S(LogicalChildIndent + 16);
        DropTarget? best = null;
        int bestDistance = int.MaxValue;

        for (int gap = first; gap <= last + 1; gap++)
        {
            if (gap > draggedIndex && gap < blockEnd)
            {
                continue;
            }

            var above = gap > first ? (MenuEntry)Items[gap - 1] : null;
            var below = gap <= last ? (MenuEntry)Items[gap] : null;
            int y = below is not null ? GetItemRectangle(gap).Top : GetItemRectangle(last).Bottom;

            DropTarget? topLevel = below is null || !below.IsChild ? new DropTarget(y, false, null, below) : null;

            DropTarget? inSubmenu = null;
            MenuEntry? submenu = above is { Kind: EntryKind.Submenu, IsChild: false } ? above
                : above is { IsChild: true } ? FindParent(above) : null;
            if (submenu is not null && submenu != dragged && dragged.Kind == EntryKind.Command)
            {
                inSubmenu = new DropTarget(y, true, submenu.Id, below is { IsChild: true } && below.ParentId == submenu.Id ? below : null);
            }

            DropTarget? candidate = topLevel is not null && inSubmenu is not null
                ? (wantsIndent ? inSubmenu : topLevel)
                : topLevel ?? inSubmenu;

            int distance = Math.Abs(point.Y - y);
            if (candidate is not null && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private MenuEntry? FindParent(MenuEntry child) =>
        Items.OfType<MenuEntry>().FirstOrDefault(e => e.Id == child.ParentId);

    private Rectangle IndicatorBounds(DropTarget target)
    {
        int left = S(2) + (target.Indented ? S(LogicalChildIndent) : 0);
        return Rectangle.FromLTRB(left, target.Y - S(4), ClientSize.Width - S(2), target.Y + S(4));
    }

    private void InvalidateIndicator(DropTarget? target)
    {
        if (target is not null)
        {
            Invalidate(IndicatorBounds(target));
        }
    }

    private void DrawDropIndicator(Graphics g, DropTarget target)
    {
        Rectangle bounds = IndicatorBounds(target);
        int thickness = S(2);
        int dot = S(7);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillRoundedRectangle(Theme.Accent, new RectangleF(bounds.Left + dot / 2f, target.Y - thickness / 2f, bounds.Width - dot / 2f, thickness), thickness / 2f);
        using var brush = new SolidBrush(Theme.Accent);
        g.FillEllipse(brush, bounds.Left, target.Y - dot / 2f, dot, dot);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_dragging && e.KeyCode == Keys.Escape)
        {
            CancelDrag();
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

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

        if (_dragging && _dropTarget is not null)
        {
            DrawDropIndicator(g, _dropTarget);
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
        bool isChild = item is MenuEntry { IsChild: true };
        Rectangle card = CardBounds(bounds, isChild);

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

        // Every row leaves room for the drag handle, so icons and text line up.
        var content = Rectangle.FromLTRB(card.Left + S(12), card.Top, card.Right, card.Bottom);

        if (item is MenuEntry entry)
        {
            bool active = entry.Enabled && (!entry.IsChild || Items.OfType<MenuEntry>().FirstOrDefault(p => p.Id == entry.ParentId)?.Enabled != false);
            string glyph = entry.Kind == EntryKind.Submenu ? Glyphs.More : Glyphs.Play;
            string title = string.IsNullOrWhiteSpace(entry.Text) ? "(no name)" : entry.Text;
            Rectangle toggle = ToggleBounds(bounds);
            if (AllowReorder)
            {
                DrawGrip(g, GripBounds(card), hover || selected ? Theme.TextSecondary : Theme.TextDisabled);
            }

            DrawContent(g, content, entry.EffectiveIcon, glyph, title, Describe(entry), active, toggle.Left - S(10));
            DrawToggle(g, toggle, entry.Enabled, hover && _hoverToggle);
        }
        else if (item is ExternalItem external)
        {
            string glyph = external.Kind == ExternalItemKind.ShellExtension ? Glyphs.Library
                : external.Kind == ExternalItemKind.Submenu ? Glyphs.More : Glyphs.Play;
            int right = card.Right - S(40);
            DrawContent(g, content, external.Icon, glyph, external.Text, Describe(external), active: true, right);

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

    /// <summary>Two columns of three dots: the usual sign that a row can be dragged.</summary>
    private void DrawGrip(Graphics g, Rectangle bounds, Color color)
    {
        float dot = Math.Max(2f, S(2) + 0.5f);
        float step = S(5);
        float left = bounds.Left + (bounds.Width - step - dot) / 2f;
        float top = bounds.Top + (bounds.Height - 2 * step - dot) / 2f;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        for (int column = 0; column < 2; column++)
        {
            for (int row = 0; row < 3; row++)
            {
                g.FillEllipse(brush, left + column * step, top + row * step, dot, dot);
            }
        }
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
            _scrollTimer.Dispose();
            _titleFont?.Dispose();
            _captionFont?.Dispose();
            _glyphFont?.Dispose();
            _headerFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
