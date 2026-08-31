using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal sealed class GradientPanel : Panel
{
    public GradientPanel()
    {
        DoubleBuffered = true;
        StartColor = Color.FromArgb(30, 64, 175);
        EndColor = Color.FromArgb(37, 99, 235);
    }

    public Color StartColor { get; set; }
    public Color EndColor { get; set; }

    protected override void OnPaintBackground(PaintEventArgs eventArgs)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, 15F);
        eventArgs.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
