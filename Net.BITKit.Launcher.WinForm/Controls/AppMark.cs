using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal sealed class AppMark : Control
{
    public AppMark()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Size = new Size(46, 46);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var shape = CreateRoundedRectangle(bounds, 12);
        using var gradient = new LinearGradientBrush(
            bounds,
            Color.FromArgb(37, 99, 235),
            Color.FromArgb(79, 70, 229),
            45F);
        eventArgs.Graphics.FillPath(gradient, shape);

        const int tile = 8;
        const int gap = 3;
        var left = (Width - (tile * 2 + gap)) / 2;
        var top = (Height - (tile * 2 + gap)) / 2;
        using var tileBrush = new SolidBrush(Color.White);
        eventArgs.Graphics.FillRectangle(tileBrush, left, top, tile, tile);
        eventArgs.Graphics.FillRectangle(tileBrush, left + tile + gap, top, tile, tile);
        eventArgs.Graphics.FillRectangle(tileBrush, left, top + tile + gap, tile, tile);
        eventArgs.Graphics.FillRectangle(tileBrush, left + tile + gap, top + tile + gap, tile, tile);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
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
