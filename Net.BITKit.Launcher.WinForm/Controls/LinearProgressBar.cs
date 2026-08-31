using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal sealed class LinearProgressBar : Control
{
    private int _value;

    public LinearProgressBar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 10;
        TrackColor = Color.FromArgb(226, 232, 240);
        BarColor = Color.FromArgb(37, 99, 235);
    }

    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (_value == normalized)
            {
                return;
            }

            _value = normalized;
            Invalidate();
        }
    }

    public Color TrackColor { get; set; }
    public Color BarColor { get; set; }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var radius = Math.Max(1, bounds.Height / 2);

        using var trackPath = CreateRoundedRectangle(bounds, radius);
        using var trackBrush = new SolidBrush(TrackColor);
        eventArgs.Graphics.FillPath(trackBrush, trackPath);

        if (Value <= 0)
        {
            return;
        }

        var fillWidth = Math.Max(bounds.Height, (int)Math.Round(bounds.Width * (Value / 100D)));
        var fillBounds = new Rectangle(bounds.X, bounds.Y, Math.Min(fillWidth, bounds.Width), bounds.Height);
        using var fillPath = CreateRoundedRectangle(fillBounds, radius);
        using var fillBrush = new SolidBrush(BarColor);
        eventArgs.Graphics.FillPath(fillBrush, fillPath);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
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
