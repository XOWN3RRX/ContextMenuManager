using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ContextMenuManager.UI.Controls;

/// <summary>A row of mutually exclusive options (like a Windows 11 selector bar).</summary>
internal sealed class SegmentedControl : Control
{
    private readonly string[] _options;
    private int _selectedIndex;
    private int _hoverIndex = -1;

    public SegmentedControl(params string[] options)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _options = options;
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? SelectedIndexChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            value = Math.Clamp(value, 0, _options.Length - 1);
            if (value != _selectedIndex)
            {
                _selectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int PreferredWidth
    {
        get
        {
            int width = LogicalToDeviceUnits(8);
            foreach (string option in _options)
            {
                width += TextRenderer.MeasureText(option, Font).Width + LogicalToDeviceUnits(24);
            }

            return width;
        }
    }

    private Rectangle SegmentBounds(int index)
    {
        int pad = LogicalToDeviceUnits(3);
        int x = pad;
        for (int i = 0; i < _options.Length; i++)
        {
            int width = TextRenderer.MeasureText(_options[i], Font).Width + LogicalToDeviceUnits(24);
            if (i == _options.Length - 1)
            {
                width = Width - pad - x;
            }

            if (i == index)
            {
                return new Rectangle(x, pad, width, Height - pad * 2);
            }

            x += width;
        }

        return Rectangle.Empty;
    }

    private int HitTest(Point point)
    {
        for (int i = 0; i < _options.Length; i++)
        {
            if (SegmentBounds(i).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = HitTest(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverIndex = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int index = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            SelectedIndex = index;
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex--;
        if (e.KeyCode == Keys.Right) SelectedIndex++;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float radius = LogicalToDeviceUnits(5);
        var bounds = new RectangleF(0, 0, Width, Height);
        g.FillRoundedRectangle(Theme.Well, bounds, radius);
        g.DrawRoundedRectangle(Theme.Border, bounds, radius);

        for (int i = 0; i < _options.Length; i++)
        {
            Rectangle segment = SegmentBounds(i);
            bool selected = i == _selectedIndex;
            if (selected)
            {
                g.FillRoundedRectangle(Theme.Surface, segment, radius - 1);
                g.DrawRoundedRectangle(Theme.Border, segment, radius - 1);
                int barWidth = LogicalToDeviceUnits(16);
                var bar = new RectangleF(segment.Left + (segment.Width - barWidth) / 2f, segment.Bottom - LogicalToDeviceUnits(3), barWidth, LogicalToDeviceUnits(3));
                g.FillRoundedRectangle(Theme.Accent, bar, bar.Height / 2f);
            }
            else if (i == _hoverIndex)
            {
                g.FillRoundedRectangle(Theme.SurfaceHover, segment, radius - 1);
            }

            TextRenderer.DrawText(g, _options[i], Font, segment, selected ? Theme.Text : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.Text) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
