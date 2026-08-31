using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal sealed class PrimaryButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public PrimaryButton()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Height = 42;
        Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        ForeColor = Color.White;
        PrimaryColor = Color.FromArgb(37, 99, 235);
        HoverColor = Color.FromArgb(29, 78, 216);
        PressedColor = Color.FromArgb(30, 64, 175);
        DisabledColor = Color.FromArgb(166, 175, 191);
        BorderColor = Color.Transparent;
        BorderWidth = 0;
        CornerRadius = 9;
    }

    public Color PrimaryColor { get; set; }
    public Color HoverColor { get; set; }
    public Color PressedColor { get; set; }
    public Color DisabledColor { get; set; }
    public Color BorderColor { get; set; }
    public int BorderWidth { get; set; }
    public int CornerRadius { get; set; }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        Invalidate();
        base.OnEnabledChanged(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (BackColor == Color.Transparent)
        {
            base.OnPaintBackground(eventArgs);
        }
        else
        {
            eventArgs.Graphics.Clear(BackColor);
        }
        var color = !Enabled
            ? DisabledColor
            : _pressed
                ? PressedColor
                : _hovered
                    ? HoverColor
                    : PrimaryColor;

        using var path = CreateRoundedRectangle(ClientRectangle, CornerRadius);
        using var brush = new SolidBrush(color);
        eventArgs.Graphics.FillPath(brush, path);
        if (BorderWidth > 0)
        {
            using var border = new Pen(BorderColor, BorderWidth);
            eventArgs.Graphics.DrawPath(border, path);
        }

        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(ClientRectangle, -5, -5);
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focusBounds, ForeColor, color);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Min(Math.Max(radius * 2, 1), Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
