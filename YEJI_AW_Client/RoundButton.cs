using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YEJI_AW_Client;

internal class RoundButton : Button
{
    public int Radius { get; set; } = 8;
    private bool _hovered;

    public RoundButton()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode   = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rc = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPath(rc, Radius);

        Color fill = !Enabled
            ? Color.FromArgb(214, 218, 226)
            : _hovered ? Darken(BackColor, 18) : BackColor;
        Color textColor = !Enabled ? Color.FromArgb(150, 152, 155) : ForeColor;

        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);

        if (FlatAppearance.BorderSize > 0)
        {
            var borderColor = !Enabled ? Color.FromArgb(200, 202, 206) : FlatAppearance.BorderColor;
            using var pen = new Pen(borderColor, FlatAppearance.BorderSize);
            g.DrawPath(pen, path);
        }

        var flags = TextFormatFlags.HorizontalCenter
                  | TextFormatFlags.VerticalCenter
                  | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor, flags);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    private static Color Darken(Color c, int amount)
        => Color.FromArgb(c.A,
            Math.Max(0, c.R - amount),
            Math.Max(0, c.G - amount),
            Math.Max(0, c.B - amount));

    private static GraphicsPath RoundedPath(Rectangle r, int rad)
    {
        int d = rad * 2;
        var gp = new GraphicsPath();
        gp.AddArc(r.Left,       r.Top,        d, d, 180, 90);
        gp.AddArc(r.Right - d,  r.Top,        d, d, 270, 90);
        gp.AddArc(r.Right - d,  r.Bottom - d, d, d,   0, 90);
        gp.AddArc(r.Left,       r.Bottom - d, d, d,  90, 90);
        gp.CloseFigure();
        return gp;
    }
}
