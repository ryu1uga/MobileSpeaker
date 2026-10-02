using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileSpeaker.UI;

/// <summary>Circulo de color que indica el estado del puente.</summary>
internal sealed class StatusDot : Control
{
    private Color _dotColor = Color.Gray;

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    public Color DotColor
    {
        get => _dotColor;
        set
        {
            if (_dotColor == value)
                return;
            _dotColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int size = Math.Min(Width, Height) - 2;
        if (size <= 0)
            return;
        int x = (Width - size) / 2;
        int y = (Height - size) / 2;
        using var brush = new SolidBrush(_dotColor);
        e.Graphics.FillEllipse(brush, x, y, size, size);
    }
}
