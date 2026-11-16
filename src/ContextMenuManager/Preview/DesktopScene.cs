using System.Drawing.Drawing2D;
using ContextMenuManager.Core;
using ContextMenuManager.Interop;
using ContextMenuManager.UI;

namespace ContextMenuManager.Preview;

internal sealed class PreviewClickEventArgs(PreviewItem item, PreviewTarget target) : EventArgs
{
    public PreviewItem Item { get; } = item;

    public PreviewTarget Target { get; } = target;
}

/// <summary>
/// A small, pretend Windows desktop. Right-clicking it (or one of its icons) opens a
/// simulated context menu built from Windows' items, other programs' items and the user's
/// entries. Nothing in here runs a command; clicks are reported through <see cref="ItemClicked"/>.
/// </summary>
internal sealed class DesktopScene : Control
{
    private sealed record DesktopIcon(PreviewTarget Target, string Label, int StockIcon);

    private sealed class OpenMenu
    {
        public required PreviewMenu Menu { get; init; }
        public Rectangle Bounds { get; set; }
        public List<Rectangle> ItemRects { get; } = [];
        public List<Rectangle> ToolbarRects { get; } = [];
        public int Hover { get; set; } = -1;
        public int ToolbarHover { get; set; } = -1;
        public int OpenChild { get; set; } = -1;
    }

    private static readonly DesktopIcon[] s_icons =
    [
        new(PreviewTarget.Folder, "Documents", NativeMethods.SIID_FOLDER),
        new(PreviewTarget.File, "Notes.txt", NativeMethods.SIID_DOCASSOC),
        new(PreviewTarget.Drive, "Local Disk (C:)", NativeMethods.SIID_DRIVEFIXED),
    ];

    private readonly List<OpenMenu> _menus = [];
    private readonly Rectangle[] _iconRects = new Rectangle[s_icons.Length];
    private MenuStyle _style = MenuStyle.Modern;
    private PreviewTarget _target;
    private Point _anchor;
    private bool _shift;
    private int _hoverIcon = -1;
    private int _selectedIcon = -1;
    private string? _notice;
    private Font? _boldFont;
    private Font? _glyphFont;
    private Font? _smallGlyphFont;
    private Font? _captionFont;

    public DesktopScene()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Banner = new ResultBanner { Visible = false };
        Controls.Add(Banner);
    }

    /// <summary>Raised when the user clicks an item in the simulated menu.</summary>
    public event EventHandler<PreviewClickEventArgs>? ItemClicked;

    /// <summary>Builds the full menu: (target, shift held, entry to point out) → menu.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<PreviewTarget, bool, MenuEntry?, MenuStyle, PreviewMenu>? ClassicMenuProvider { get; set; }

    /// <summary>Shows what a click did.</summary>
    public ResultBanner Banner { get; }

    /// <summary>Pretends Shift is held for every right-click.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ForceShift { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public MenuStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            CloseMenus();
        }
    }

    private bool IsWin10 => _style == MenuStyle.Classic10;

    private int S(int logical) => LogicalToDeviceUnits(logical);

    private int TaskbarHeight => S(IsWin10 ? 36 : 40);

    public bool HasOpenMenu => _menus.Count > 0;

    public void CloseMenus()
    {
        _menus.Clear();
        _notice = null;
        Invalidate();
    }

    /// <summary>Opens the full menu where <paramref name="entry"/> appears and points it out.</summary>
    public void ShowEntry(MenuEntry entry, MenuEntry? parent)
    {
        MenuEntry placement = parent ?? entry;
        PreviewTarget target = PreviewMenuBuilder.FromLocation(placement.Locations);
        bool shift = placement.ShiftOnly || ForceShift;

        int iconIndex = Array.FindIndex(s_icons, i => i.Target == target);
        _selectedIcon = iconIndex;
        Point anchor = iconIndex >= 0 && !_iconRects[iconIndex].IsEmpty
            ? new Point(_iconRects[iconIndex].Right - S(10), _iconRects[iconIndex].Top + S(24))
            : new Point(Math.Min(Width / 3, S(320)), S(40));

        MenuStyle style = _style == MenuStyle.Modern ? MenuStyle.Classic11 : _style;
        OpenRoot(target, anchor, shift, entry, style);

        if (_style == MenuStyle.Modern)
        {
            _notice = "On Windows 11 your entries are under “Show more options”";
        }
        else if (shift && !ForceShift)
        {
            _notice = "This entry only shows while Shift is held";
        }

        if (parent is not null && _menus.Count > 0)
        {
            int index = _menus[0].Menu.Items.FindIndex(i => i.Entry?.Id == parent.Id);
            if (index >= 0)
            {
                _menus[0].Hover = index;
                OpenSubmenu(0, index);
            }
        }

        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _boldFont?.Dispose();
        _glyphFont?.Dispose();
        _smallGlyphFont?.Dispose();
        _captionFont?.Dispose();
        _boldFont = _glyphFont = _smallGlyphFont = _captionFont = null;
        base.OnFontChanged(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CloseMenus();
        LayoutIcons();
        LayoutBanner();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        LayoutIcons();
        LayoutBanner();
    }

    private void LayoutIcons()
    {
        int cellW = S(84);
        int cellH = S(88);
        for (int i = 0; i < s_icons.Length; i++)
        {
            _iconRects[i] = new Rectangle(S(10), S(10) + i * (cellH + S(6)), cellW, cellH);
        }
    }

    private void LayoutBanner()
    {
        int width = Math.Min(S(600), Width - S(24));
        int height = Banner.PreferredHeight(width);
        Banner.Bounds = new Rectangle((Width - width) / 2, Height - TaskbarHeight - height - S(12), width, height);
    }

    public void ShowBanner()
    {
        LayoutBanner();
        Banner.Visible = true;
        Banner.BringToFront();
    }

    // ---------------------------------------------------------------- input

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Escape or Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_menus.Count == 0)
        {
            if (e.KeyCode == Keys.Apps || e.KeyCode == Keys.F10 && e.Shift)
            {
                PreviewTarget target = _selectedIcon >= 0 ? s_icons[_selectedIcon].Target : PreviewTarget.Background;
                Point anchor = _selectedIcon >= 0 ? new Point(_iconRects[_selectedIcon].Right, _iconRects[_selectedIcon].Top + S(20)) : new Point(S(200), S(40));
                OpenFromRightClick(target, anchor, e.Shift);
            }

            return;
        }

        OpenMenu menu = _menus[^1];
        switch (e.KeyCode)
        {
            case Keys.Escape or Keys.Left when _menus.Count > 1 || e.KeyCode == Keys.Escape:
                _menus.RemoveAt(_menus.Count - 1);
                if (_menus.Count > 0)
                {
                    _menus[^1].OpenChild = -1;
                }

                Invalidate();
                break;
            case Keys.Down or Keys.Up:
                menu.Hover = NextSelectable(menu, menu.Hover, e.KeyCode == Keys.Down ? 1 : -1);
                Invalidate();
                break;
            case Keys.Right when menu.Hover >= 0 && menu.Menu.Items[menu.Hover].HasChildren:
                OpenSubmenu(_menus.Count - 1, menu.Hover);
                _menus[^1].Hover = NextSelectable(_menus[^1], -1, 1);
                Invalidate();
                break;
            case Keys.Enter when menu.Hover >= 0:
                Activate(_menus.Count - 1, menu.Menu.Items[menu.Hover]);
                break;
        }

        e.Handled = true;
    }

    private static int NextSelectable(OpenMenu menu, int from, int step)
    {
        int count = menu.Menu.Items.Count;
        for (int i = 1; i <= count; i++)
        {
            int index = ((from + step * i) % count + count) % count;
            if (!menu.Menu.Items[index].IsSeparator)
            {
                return index;
            }
        }

        return -1;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        (int menuIndex, _, _) = HitTestMenus(e.Location);
        if (menuIndex >= 0)
        {
            return; // Handled on mouse up, like a real menu.
        }

        int icon = HitTestIcon(e.Location);
        CloseMenus();
        _selectedIcon = icon;

        if (e.Button == MouseButtons.Right && e.Y < Height - TaskbarHeight)
        {
            OpenFromRightClick(icon >= 0 ? s_icons[icon].Target : PreviewTarget.Background, e.Location, (ModifierKeys & Keys.Shift) != 0);
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        (int menuIndex, int item, int toolbar) = HitTestMenus(e.Location);
        if (menuIndex < 0 || e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
        {
            return;
        }

        if (toolbar >= 0)
        {
            Activate(menuIndex, _menus[menuIndex].Menu.ToolbarItems[toolbar]);
        }
        else if (item >= 0)
        {
            Activate(menuIndex, _menus[menuIndex].Menu.Items[item]);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        (int menuIndex, int item, int toolbar) = HitTestMenus(e.Location);

        if (menuIndex < 0)
        {
            int icon = _menus.Count == 0 ? HitTestIcon(e.Location) : -1;
            if (icon != _hoverIcon)
            {
                _hoverIcon = icon;
                Invalidate();
            }

            return;
        }

        OpenMenu menu = _menus[menuIndex];
        if (menu.Hover == item && menu.ToolbarHover == toolbar)
        {
            return;
        }

        menu.Hover = item;
        menu.ToolbarHover = toolbar;

        if (item >= 0 && menu.Menu.Items[item].HasChildren && !menu.Menu.Items[item].Disabled)
        {
            if (menu.OpenChild != item)
            {
                OpenSubmenu(menuIndex, item);
            }
        }
        else if (item >= 0)
        {
            CloseDeeperThan(menuIndex);
        }

        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIcon = -1;
        foreach (OpenMenu menu in _menus.Where(m => m.OpenChild < 0))
        {
            menu.Hover = -1;
            menu.ToolbarHover = -1;
        }

        Invalidate();
    }

    private void OpenFromRightClick(PreviewTarget target, Point anchor, bool shiftHeld)
    {
        bool shift = shiftHeld || ForceShift;

        // On Windows 11, Shift + right-click goes straight to the full menu.
        if (_style == MenuStyle.Modern && !shiftHeld)
        {
            _target = target;
            _anchor = anchor;
            _shift = shift;
            _menus.Add(LayoutMenu(PreviewMenuBuilder.BuildModern(target), anchor, null));
            _notice = "Your entries are under “Show more options”";
        }
        else
        {
            OpenRoot(target, anchor, shift, null, _style == MenuStyle.Modern ? MenuStyle.Classic11 : _style);
        }

        Invalidate();
    }

    private void OpenRoot(PreviewTarget target, Point anchor, bool shift, MenuEntry? highlight, MenuStyle style)
    {
        _menus.Clear();
        _notice = null;
        _target = target;
        _anchor = anchor;
        _shift = shift;

        if (ClassicMenuProvider is null)
        {
            return;
        }

        PreviewMenu menu = ClassicMenuProvider(target, shift, highlight, style);
        _menus.Add(LayoutMenu(menu, anchor, null));
    }

    private void Activate(int menuIndex, PreviewItem item)
    {
        if (item.IsSeparator || item.Disabled)
        {
            return;
        }

        if (item.Source == PreviewItemSource.ShowMoreOptions)
        {
            OpenRoot(_target, _anchor, _shift, null, MenuStyle.Classic11);
            _notice = "This is the full menu. Your entries live here";
            Invalidate();
            return;
        }

        if (item.HasChildren)
        {
            int index = _menus[menuIndex].Menu.Items.IndexOf(item);
            if (index >= 0 && _menus[menuIndex].OpenChild != index)
            {
                OpenSubmenu(menuIndex, index);
                Invalidate();
            }

            return;
        }

        PreviewTarget target = _target;
        CloseMenus();
        ItemClicked?.Invoke(this, new PreviewClickEventArgs(item, target));
    }

    private void OpenSubmenu(int menuIndex, int itemIndex)
    {
        CloseDeeperThan(menuIndex);
        OpenMenu parent = _menus[menuIndex];
        PreviewItem item = parent.Menu.Items[itemIndex];
        if (item.Children is null)
        {
            return;
        }

        var submenu = new PreviewMenu { Style = parent.Menu.Style };
        submenu.Items.AddRange(item.Children);
        parent.OpenChild = itemIndex;
        _menus.Add(LayoutMenu(submenu, Point.Empty, (parent, parent.ItemRects[itemIndex])));
    }

    private void CloseDeeperThan(int menuIndex)
    {
        if (_menus.Count > menuIndex + 1)
        {
            _menus.RemoveRange(menuIndex + 1, _menus.Count - menuIndex - 1);
        }

        _menus[menuIndex].OpenChild = -1;
    }

    private (int Menu, int Item, int Toolbar) HitTestMenus(Point point)
    {
        for (int m = _menus.Count - 1; m >= 0; m--)
        {
            OpenMenu menu = _menus[m];
            if (!menu.Bounds.Contains(point))
            {
                continue;
            }

            int toolbar = menu.ToolbarRects.FindIndex(r => r.Contains(point));
            int item = menu.ItemRects.FindIndex(r => r.Contains(point));
            if (item >= 0 && menu.Menu.Items[item].IsSeparator)
            {
                item = -1;
            }

            return (m, item, toolbar);
        }

        return (-1, -1, -1);
    }

    private int HitTestIcon(Point point) => Array.FindIndex(_iconRects, r => r.Contains(point));

    // ---------------------------------------------------------------- menu layout

    private (int ItemHeight, int SeparatorHeight, int Padding, int TextLeft, int Radius) Metrics(MenuStyle style) => style switch
    {
        MenuStyle.Classic10 => (S(24), S(9), S(3), S(34), 0),
        MenuStyle.Modern => (S(32), S(9), S(4), S(44), S(8)),
        _ => (S(30), S(9), S(4), S(42), S(8)),
    };

    private OpenMenu LayoutMenu(PreviewMenu menu, Point anchor, (OpenMenu Parent, Rectangle Item)? parent)
    {
        var open = new OpenMenu { Menu = menu };
        (int itemHeight, int separatorHeight, int pad, int textLeft, _) = Metrics(menu.Style);
        int toolbarHeight = menu.ToolbarItems.Count > 0 ? S(44) : 0;

        int available = Height - TaskbarHeight - S(8);
        int separators = menu.Items.Count(i => i.IsSeparator);
        int rows = menu.Items.Count - separators;
        int natural = pad * 2 + toolbarHeight + rows * itemHeight + separators * separatorHeight;
        if (natural > available && rows > 0)
        {
            // Small preview window: squeeze the rows so the whole menu stays visible.
            itemHeight = Math.Max(S(18), (available - pad * 2 - toolbarHeight - separators * separatorHeight) / rows);
        }

        _boldFont ??= new Font(Font, FontStyle.Bold);
        int textWidth = 0;
        foreach (PreviewItem item in menu.Items.Where(i => !i.IsSeparator))
        {
            textWidth = Math.Max(textWidth, TextRenderer.MeasureText(item.Text, item.Bold ? _boldFont : Font).Width);
        }

        int width = Math.Max(S(menu.Style == MenuStyle.Classic10 ? 190 : 210), textLeft + textWidth + S(48));
        width = Math.Max(width, menu.ToolbarItems.Count * S(42) + pad * 2);
        int height = pad * 2 + toolbarHeight + rows * itemHeight + separators * separatorHeight;

        int x, y;
        if (parent is { } p)
        {
            x = p.Parent.Bounds.Right - S(4);
            y = p.Item.Top - pad;
            if (x + width > Width - S(4))
            {
                x = p.Parent.Bounds.Left - width + S(4);
            }
        }
        else
        {
            x = anchor.X + 2;
            y = anchor.Y + 2;
            if (x + width > Width - S(4))
            {
                x = anchor.X - width - 2;
            }
        }

        x = Math.Clamp(x, S(4), Math.Max(S(4), Width - width - S(4)));
        y = Math.Clamp(y, S(4), Math.Max(S(4), Height - TaskbarHeight - height - S(4)));
        open.Bounds = new Rectangle(x, y, width, height);

        int cy = y + pad;
        if (toolbarHeight > 0)
        {
            int size = S(38);
            int gap = (width - pad * 2 - menu.ToolbarItems.Count * size) / Math.Max(1, menu.ToolbarItems.Count + 1);
            for (int i = 0; i < menu.ToolbarItems.Count; i++)
            {
                open.ToolbarRects.Add(new Rectangle(x + pad + gap + i * (size + gap), cy + (toolbarHeight - size) / 2, size, size));
            }

            cy += toolbarHeight;
        }

        foreach (PreviewItem item in menu.Items)
        {
            int h = item.IsSeparator ? separatorHeight : itemHeight;
            open.ItemRects.Add(new Rectangle(x + pad, cy, width - pad * 2, h));
            cy += h;
        }

        return open;
    }

    // ---------------------------------------------------------------- painting

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        PaintWallpaper(g);
        PaintIcons(g);
        PaintTaskbar(g);

        if (_menus.Count == 0)
        {
            PaintHint(g, "Right-click the desktop or an icon to try your menu");
        }
        else if (_notice is not null)
        {
            PaintHint(g, _notice);
        }

        foreach (OpenMenu menu in _menus)
        {
            PaintMenu(g, menu);
        }
    }

    private void PaintWallpaper(Graphics g)
    {
        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        Color baseColor = Theme.IsDark ? Color.FromArgb(0x10, 0x16, 0x2A) : Color.FromArgb(0xC9, 0xDC, 0xF2);
        Color accent = Theme.Accent;

        if (IsWin10)
        {
            using var brush = new LinearGradientBrush(bounds, Color.FromArgb(0x00, 0x2B, 0x5C), Color.FromArgb(0x00, 0x0E, 0x24), LinearGradientMode.Vertical);
            g.FillRectangle(brush, bounds);
            using var beam = new SolidBrush(Color.FromArgb(40, 0x3A, 0x9B, 0xFF));
            g.FillPolygon(beam, [new Point(bounds.Width / 2, 0), new Point(bounds.Width, 0), new Point(bounds.Width, bounds.Height / 2)]);
            return;
        }

        using (var brush = new LinearGradientBrush(bounds, Theme.Blend(accent, baseColor, 0.55), baseColor, 60f))
        {
            g.FillRectangle(brush, bounds);
        }

        // Soft "bloom" shapes, in the spirit of the Windows 11 wallpaper.
        using var path = new GraphicsPath();
        var bloom = new RectangleF(bounds.Width * 0.35f, bounds.Height * 0.15f, bounds.Width * 0.8f, bounds.Height * 1.1f);
        path.AddEllipse(bloom);
        using var glow = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(Theme.IsDark ? 120 : 150, Theme.Blend(accent, Color.White, Theme.IsDark ? 0.1 : 0.35)),
            SurroundColors = [Color.FromArgb(0, accent)],
        };
        g.FillEllipse(glow, bloom);
    }

    private void PaintIcons(Graphics g)
    {
        _captionFont ??= new Font(Font.FontFamily, Font.Size * 0.95f);
        int iconSize = S(40);

        for (int i = 0; i < s_icons.Length; i++)
        {
            Rectangle cell = _iconRects[i];
            bool selected = i == _selectedIcon;
            bool hover = i == _hoverIcon;

            if (selected || hover)
            {
                using var fill = new SolidBrush(Color.FromArgb(selected ? 90 : 50, 0xFF, 0xFF, 0xFF));
                using var border = new Pen(Color.FromArgb(selected ? 140 : 80, 0xFF, 0xFF, 0xFF));
                g.FillRectangle(fill, cell);
                g.DrawRectangle(border, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
            }

            var iconRect = new Rectangle(cell.X + (cell.Width - iconSize) / 2, cell.Y + S(6), iconSize, iconSize);
            if (IconLoader.Stock(s_icons[i].StockIcon, iconSize) is { } icon)
            {
                g.DrawImage(icon, iconRect);
            }

            var textRect = new Rectangle(cell.X + S(2), iconRect.Bottom + S(4), cell.Width - S(4), cell.Bottom - iconRect.Bottom - S(4));
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            var shadow = textRect;
            shadow.Offset(1, 1);
            TextRenderer.DrawText(g, s_icons[i].Label, _captionFont, shadow, Color.FromArgb(0x10, 0x10, 0x10), flags);
            TextRenderer.DrawText(g, s_icons[i].Label, _captionFont, textRect, Color.White, flags);
        }
    }

    private void PaintTaskbar(Graphics g)
    {
        var bar = new Rectangle(0, Height - TaskbarHeight, Width, TaskbarHeight);
        Color fill = IsWin10 ? Color.FromArgb(235, 0x10, 0x10, 0x10)
            : Theme.IsDark ? Color.FromArgb(225, 0x1C, 0x1C, 0x1C) : Color.FromArgb(225, 0xEE, 0xEE, 0xEE);
        using (var brush = new SolidBrush(fill))
        {
            g.FillRectangle(brush, bar);
        }

        Color text = IsWin10 || Theme.IsDark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
        _glyphFont ??= Theme.CreateGlyphFont(Font, 1.25f);
        int size = S(16);
        int startX = IsWin10 ? S(14) : Width / 2 - S(60);
        int y = bar.Top + (bar.Height - size) / 2;

        // A simple four-pane logo stands in for the Start button.
        Color logo = IsWin10 ? Color.White : Color.FromArgb(0x00, 0x78, 0xD4);
        using (var brush = new SolidBrush(logo))
        {
            int half = size / 2 - 1;
            g.FillRectangle(brush, startX, y, half, half);
            g.FillRectangle(brush, startX + half + 2, y, half, half);
            g.FillRectangle(brush, startX, y + half + 2, half, half);
            g.FillRectangle(brush, startX + half + 2, y + half + 2, half, half);
        }

        string[] glyphs = [Glyphs.Search, Glyphs.Folder, Glyphs.Settings];
        int x = startX + size + S(22);
        if (IsWin10)
        {
            var search = new Rectangle(x - S(8), bar.Top + S(4), S(170), bar.Height - S(8));
            using var searchBrush = new SolidBrush(Color.FromArgb(0x30, 0x30, 0x30));
            g.FillRectangle(searchBrush, search);
            TextRenderer.DrawText(g, "Type here to search", Font, Rectangle.Inflate(search, -S(8), 0), Color.FromArgb(0xB0, 0xB0, 0xB0),
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            x = search.Right + S(18);
            glyphs = [Glyphs.Folder, Glyphs.Settings];
        }

        foreach (string glyph in glyphs)
        {
            TextRenderer.DrawText(g, glyph, _glyphFont, new Rectangle(x, bar.Top, S(24), bar.Height), text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            x += S(40);
        }

        string clock = DateTime.Now.ToShortTimeString();
        TextRenderer.DrawText(g, clock, Font, new Rectangle(Width - S(90), bar.Top, S(80), bar.Height), text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
    }

    private void PaintHint(Graphics g, string text)
    {
        _captionFont ??= new Font(Font.FontFamily, Font.Size * 0.95f);
        _smallGlyphFont ??= Theme.CreateGlyphFont(Font, 1f);
        Size textSize = TextRenderer.MeasureText(g, text, _captionFont);
        int width = textSize.Width + S(48);
        int height = S(30);
        var pill = new Rectangle(Width - width - S(12), S(12), width, height);

        g.FillRoundedRectangle(Color.FromArgb(200, Theme.Surface), pill, height / 2f);
        g.DrawRoundedRectangle(Color.FromArgb(160, Theme.Border), pill, height / 2f);
        TextRenderer.DrawText(g, Glyphs.Info, _smallGlyphFont, new Rectangle(pill.X + S(12), pill.Y, S(18), height), Theme.Accent,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, text, _captionFont, new Rectangle(pill.X + S(36), pill.Y, width - S(40), height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    private void PaintMenu(Graphics g, OpenMenu open)
    {
        PreviewMenu menu = open.Menu;
        bool win10 = menu.Style == MenuStyle.Classic10;
        (_, _, _, int textLeft, int radius) = Metrics(menu.Style);
        Rectangle bounds = open.Bounds;

        Color background = win10
            ? Theme.IsDark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.FromArgb(0xF2, 0xF2, 0xF2)
            : Theme.IsDark ? Color.FromArgb(0x2C, 0x2C, 0x2C) : Color.FromArgb(0xF9, 0xF9, 0xF9);
        Color border = win10
            ? Theme.IsDark ? Color.FromArgb(0x80, 0x80, 0x80) : Color.FromArgb(0xA0, 0xA0, 0xA0)
            : Theme.IsDark ? Color.FromArgb(0x45, 0x45, 0x45) : Color.FromArgb(0xE0, 0xE0, 0xE0);
        Color hover = win10
            ? Theme.IsDark ? Color.FromArgb(0x41, 0x41, 0x41) : Color.FromArgb(0x91, 0xC9, 0xF7)
            : Theme.IsDark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.FromArgb(0xEA, 0xEA, 0xEA);
        Color separator = Theme.IsDark ? Color.FromArgb(0x50, 0x50, 0x50) : Color.FromArgb(0xD8, 0xD8, 0xD8);
        Color text = Theme.IsDark ? Color.White : Color.FromArgb(0x1A, 0x1A, 0x1A);
        Color disabled = Theme.IsDark ? Color.FromArgb(0x78, 0x78, 0x78) : Color.FromArgb(0x9A, 0x9A, 0x9A);

        // Shadow
        for (int i = 1; i <= 5; i++)
        {
            var shadow = bounds;
            shadow.Offset(0, i / 2 + 1);
            shadow.Inflate(i, i);
            g.FillRoundedRectangle(Color.FromArgb(14, 0, 0, 0), shadow, radius + i);
        }

        if (radius > 0)
        {
            g.FillRoundedRectangle(background, bounds, radius);
            g.DrawRoundedRectangle(border, bounds, radius);
        }
        else
        {
            using var brush = new SolidBrush(background);
            using var pen = new Pen(border);
            g.FillRectangle(brush, bounds);
            g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        _glyphFont ??= Theme.CreateGlyphFont(Font, 1.25f);
        _smallGlyphFont ??= Theme.CreateGlyphFont(Font, 1f);
        _boldFont ??= new Font(Font, FontStyle.Bold);

        for (int i = 0; i < menu.ToolbarItems.Count; i++)
        {
            Rectangle r = open.ToolbarRects[i];
            PreviewItem item = menu.ToolbarItems[i];
            if (i == open.ToolbarHover && !item.Disabled)
            {
                g.FillRoundedRectangle(hover, r, S(4));
            }

            TextRenderer.DrawText(g, item.Glyph, _glyphFont, r, item.Disabled ? disabled : text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (menu.ToolbarItems.Count > 0)
        {
            int lineY = open.ToolbarRects[0].Bottom + S(3);
            using var pen = new Pen(separator);
            g.DrawLine(pen, bounds.X + S(4), lineY, bounds.Right - S(5), lineY);
        }

        int iconSize = S(16);
        for (int i = 0; i < menu.Items.Count; i++)
        {
            PreviewItem item = menu.Items[i];
            Rectangle r = open.ItemRects[i];

            if (item.IsSeparator)
            {
                int y = r.Top + r.Height / 2;
                using var pen = new Pen(separator);
                g.DrawLine(pen, win10 ? r.Left + textLeft - S(6) : r.Left, y, r.Right - (win10 ? S(2) : 0), y);
                continue;
            }

            bool isHover = (i == open.Hover || i == open.OpenChild) && !item.Disabled;
            var highlightRect = win10 ? r : Rectangle.Inflate(r, 0, -S(1));
            if (item.Highlight)
            {
                FillItem(g, Theme.Blend(Theme.Accent, background, isHover ? 0.6 : 0.78), highlightRect, win10 ? 0 : S(4));
                if (win10)
                {
                    using var pen = new Pen(Theme.Accent, S(2));
                    g.DrawRectangle(pen, highlightRect.X + 1, highlightRect.Y + 1, highlightRect.Width - S(2), highlightRect.Height - S(2));
                }
                else
                {
                    g.DrawRoundedRectangle(Theme.Accent, highlightRect, S(4), S(2));
                }
            }
            else if (isHover)
            {
                FillItem(g, hover, highlightRect, win10 ? 0 : S(4));
            }

            Color color = item.Disabled ? disabled : text;
            int iconX = r.Left + (textLeft - iconSize) / 2 - (win10 ? S(2) : S(4));
            var iconRect = new Rectangle(iconX, r.Top + (r.Height - iconSize) / 2, iconSize, iconSize);
            if (!string.IsNullOrEmpty(item.IconLocation) && IconLoader.Load(item.IconLocation, iconSize) is { } icon)
            {
                g.DrawImage(icon, iconRect);
            }
            else if (item.Glyph is not null)
            {
                TextRenderer.DrawText(g, item.Glyph, _smallGlyphFont, Rectangle.Inflate(iconRect, S(4), S(4)), color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            var textRect = new Rectangle(r.Left + textLeft - S(4), r.Top, r.Width - textLeft - S(24), r.Height);
            TextRenderer.DrawText(g, item.Text, item.Bold ? _boldFont : Font, textRect, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            if (item.HasChildren)
            {
                TextRenderer.DrawText(g, Glyphs.ChevronRight, _smallGlyphFont, new Rectangle(r.Right - S(24), r.Top, S(16), r.Height), color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    private static void FillItem(Graphics g, Color color, Rectangle rect, int radius)
    {
        if (radius > 0)
        {
            g.FillRoundedRectangle(color, rect, radius);
        }
        else
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, rect);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _boldFont?.Dispose();
            _glyphFont?.Dispose();
            _smallGlyphFont?.Dispose();
            _captionFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
