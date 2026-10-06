using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeGlow
{
    internal static class StatusIconPainter
    {
        private static readonly Dictionary<string, Icon> Cache = new Dictionary<string, Icon>();

        public static Icon Paint(Color left, Color right, bool connected)
        {
            string key = left.ToArgb() + "|" + right.ToArgb() + "|" + connected;
            Icon icon;
            if (Cache.TryGetValue(key, out icon)) return icon;
            icon = Create(left, right, connected);
            Cache[key] = icon;
            return icon;
        }

        private static Icon Create(Color left, Color right, bool connected)
        {
            Size size = SystemInformation.SmallIconSize;
            using (var bitmap = new Bitmap(size.Width, size.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                float inset = size.Width / 8f;
                var circle = new RectangleF(inset, inset, size.Width - 2 * inset - 1, size.Height - 2 * inset - 1);
                if (connected)
                {
                    using (var leftBrush = new SolidBrush(left))
                    using (var rightBrush = new SolidBrush(right))
                    {
                        graphics.FillPie(leftBrush, circle.X, circle.Y, circle.Width, circle.Height, 90f, 180f);
                        graphics.FillPie(rightBrush, circle.X, circle.Y, circle.Width, circle.Height, 270f, 180f);
                    }
                }
                using (var pen = new Pen(connected ? Color.FromArgb(160, 0, 0, 0) : left, connected ? 1f : size.Width / 8f))
                {
                    graphics.DrawEllipse(pen, circle);
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
