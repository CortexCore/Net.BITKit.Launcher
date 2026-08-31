using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal sealed class HeroCanvas : Panel
{
    private Image? _artwork;

    public HeroCanvas()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.FromArgb(8, 15, 34);
    }

    public Image? Artwork
    {
        get => _artwork;
        set
        {
            if (ReferenceEquals(_artwork, value))
            {
                return;
            }

            _artwork?.Dispose();
            _artwork = value;
            Invalidate(true);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _artwork?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.Clear(BackColor);
        if (_artwork is not null)
        {
            DrawCover(eventArgs.Graphics, _artwork, ClientRectangle);
        }

        DrawOverlay(eventArgs.Graphics);
    }

    private void DrawOverlay(Graphics graphics)
    {
        var titleShadeWidth = Math.Max(1, (int)(Width * 0.6));
        var titleShadeHeight = Math.Max(1, (int)(Height * 0.38));
        using (var titleShadePath = new GraphicsPath())
        {
            titleShadePath.AddEllipse(
                -titleShadeWidth,
                -titleShadeHeight,
                titleShadeWidth * 2,
                titleShadeHeight * 2);
            using var titleShade = new PathGradientBrush(titleShadePath)
            {
                CenterPoint = new PointF(0, 0),
                CenterColor = Color.FromArgb(175, 4, 10, 25),
                SurroundColors = [Color.FromArgb(0, 4, 10, 25)]
            };
            graphics.FillPath(titleShade, titleShadePath);
        }

        var bottomTop = (int)(Height * 0.56);
        var bottomHeight = Math.Max(1, Height - bottomTop);
        using (var bottomShade = new LinearGradientBrush(
                   new Rectangle(0, bottomTop, Width, bottomHeight),
                   Color.FromArgb(0, 2, 7, 18),
                   Color.FromArgb(232, 2, 7, 18),
                   LinearGradientMode.Vertical))
        {
            graphics.FillRectangle(bottomShade, 0, bottomTop, Width, bottomHeight);
        }

        using var topShade = new LinearGradientBrush(
            new Rectangle(0, 0, Width, Math.Max(1, Height / 5)),
            Color.FromArgb(115, 1, 5, 16),
            Color.FromArgb(0, 1, 5, 16),
            LinearGradientMode.Vertical);
        graphics.FillRectangle(topShade, 0, 0, Width, Height / 5);
    }

    private static void DrawCover(Graphics graphics, Image image, Rectangle destination)
    {
        var scale = Math.Max(
            destination.Width / (double)image.Width,
            destination.Height / (double)image.Height);
        var width = (int)Math.Ceiling(image.Width * scale);
        var height = (int)Math.Ceiling(image.Height * scale);
        var target = new Rectangle(
            destination.X + (destination.Width - width) / 2,
            destination.Y + (destination.Height - height) / 2,
            width,
            height);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(image, target);
    }
}
