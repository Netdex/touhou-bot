using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EOSDBot
{
    class Screenfetch
    {
        public static Bitmap TakeScreenshot(Rectangle bounds)
        {
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height));
            }
            return bitmap;
        }

        public static Bitmap RescaleBitmap(Bitmap bitmap, Size newSize)
        {
            Bitmap newBitmap = new Bitmap(newSize.Width, newSize.Height);
            using (Graphics g = Graphics.FromImage(newBitmap))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.SmoothingMode = SmoothingMode.None;
                g.DrawImage(bitmap, 0, 0, newSize.Width, newSize.Height);
            }
            return newBitmap;
        }
    }
}
