using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal sealed class RoundedPanel : Panel
{
    public RoundedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FillColor = Color.White;
        BorderColor = Color.FromArgb(226, 232, 240);
        CornerRadius = 14;
    }

    public Color FillColor { get; set; }
    public Color BorderColor { get; set; }
    public int CornerRadius { get; set; }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        eventArgs.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var path = CreateRoundedRectangle(bounds, CornerRadius);
        using var fill = new SolidBrush(FillColor);
        using var border = new Pen(BorderColor);
        eventArgs.Graphics.FillPath(fill, path);
        eventArgs.Graphics.DrawPath(border, path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(Math.Max(radius * 2, 2), Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            return path;
        }

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
