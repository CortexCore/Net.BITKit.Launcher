using System.Drawing.Drawing2D;

namespace Net.BITKit.Launcher.WinForm.Controls;

internal enum WindowCommand
{
    Minimize,
    Close
}

internal sealed class WindowCommandButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public WindowCommandButton()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(44, 40);
        ForeColor = Color.White;
    }

    public WindowCommand Command { get; init; }

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

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hovered)
        {
            var color = Command == WindowCommand.Close
                ? Color.FromArgb(_pressed ? 215 : 185, 220, 38, 38)
                : Color.FromArgb(_pressed ? 70 : 48, 255, 255, 255);
            using var hover = new SolidBrush(color);
            eventArgs.Graphics.FillRectangle(hover, ClientRectangle);
        }

        using var pen = new Pen(ForeColor, 1.6F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        var centerX = Width / 2F;
        var centerY = Height / 2F;
        if (Command == WindowCommand.Minimize)
        {
            eventArgs.Graphics.DrawLine(pen, centerX - 6, centerY + 3, centerX + 6, centerY + 3);
        }
        else
        {
            eventArgs.Graphics.DrawLine(pen, centerX - 5, centerY - 5, centerX + 5, centerY + 5);
            eventArgs.Graphics.DrawLine(pen, centerX + 5, centerY - 5, centerX - 5, centerY + 5);
        }
    }
}
