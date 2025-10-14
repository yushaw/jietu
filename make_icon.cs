using System.Drawing;

using var bmp = new Bitmap(64, 64);
using (var g = Graphics.FromImage(bmp))
{
    g.Clear(Color.FromArgb(48, 86, 166));
    using var brush = new SolidBrush(Color.White);
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    g.FillEllipse(brush, 16, 16, 32, 32);
}
bmp.Save("/Users/yushaw/dev/jieti/tray.png", System.Drawing.Imaging.ImageFormat.Png);
