using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgentGlow
{
    internal static class StatusIconPainter
    {
        private static readonly Color ErrorColor = Color.FromArgb(218, 54, 51);
        private static readonly Color RepairColor = Color.FromArgb(232, 140, 0);
        private static readonly Dictionary<string, Icon> Cache = new Dictionary<string, Icon>();

        public static Icon Paint(Color left, Color right, TrayBadge badge)
        {
            string key = left.ToArgb() + "|" + right.ToArgb() + "|" + badge;
            Icon icon;
            if (Cache.TryGetValue(key, out icon)) return icon;
            icon = Create(left, right, badge);
            Cache[key] = icon;
            return icon;
        }

        public static Bitmap Render(int side, Color left, Color right, TrayBadge badge)
        {
            var bitmap = new Bitmap(side, side);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                float inset = side / 8f;
                var circle = new RectangleF(inset, inset, side - 2 * inset - 1, side - 2 * inset - 1);
                bool filled = badge == TrayBadge.None;
                if (filled)
                {
                    using (var leftBrush = new SolidBrush(left))
                    using (var rightBrush = new SolidBrush(right))
                    {
                        graphics.FillPie(leftBrush, circle.X, circle.Y, circle.Width, circle.Height, 90f, 180f);
                        graphics.FillPie(rightBrush, circle.X, circle.Y, circle.Width, circle.Height, 270f, 180f);
                    }
                }
                using (var pen = new Pen(filled ? Color.FromArgb(160, 0, 0, 0) : left, filled ? 1f : side / 8f))
                {
                    graphics.DrawEllipse(pen, circle);
                }
                if (badge == TrayBadge.Error) DrawErrorBadge(graphics, side);
                else if (badge == TrayBadge.Repairing) DrawRepairBadge(graphics, side);
            }
            return bitmap;
        }

        private static RectangleF BadgeBounds(int side)
        {
            float diameter = side * 0.62f;
            return new RectangleF(side - diameter, side - diameter, diameter - 0.5f, diameter - 0.5f);
        }

        private static void DrawBadgeBackground(Graphics graphics, RectangleF bounds, Color color, int side)
        {
            using (var brush = new SolidBrush(color))
            using (var outline = new Pen(Color.White, Math.Max(1f, side / 16f)))
            {
                graphics.FillEllipse(brush, bounds);
                graphics.DrawEllipse(outline, bounds);
            }
        }

        private static void DrawErrorBadge(Graphics graphics, int side)
        {
            RectangleF bounds = BadgeBounds(side);
            DrawBadgeBackground(graphics, bounds, ErrorColor, side);
            float centerX = bounds.X + bounds.Width / 2f;
            using (var pen = new Pen(Color.White, Math.Max(1.5f, side / 9f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLine(pen, centerX, bounds.Y + bounds.Height * 0.24f, centerX, bounds.Y + bounds.Height * 0.56f);
                graphics.DrawLine(pen, centerX, bounds.Y + bounds.Height * 0.76f, centerX, bounds.Y + bounds.Height * 0.77f);
            }
        }

        private static void DrawRepairBadge(Graphics graphics, int side)
        {
            RectangleF bounds = BadgeBounds(side);
            DrawBadgeBackground(graphics, bounds, RepairColor, side);
            float margin = bounds.Width * 0.26f;
            var arc = new RectangleF(bounds.X + margin, bounds.Y + margin, bounds.Width - 2 * margin, bounds.Height - 2 * margin);
            const float startAngle = -30f;
            const float sweepAngle = 250f;
            using (var pen = new Pen(Color.White, Math.Max(1.2f, side / 12f)))
            {
                graphics.DrawArc(pen, arc, startAngle, sweepAngle);
            }
            float radius = arc.Width / 2f;
            double end = (startAngle + sweepAngle) * Math.PI / 180.0;
            float centerX = arc.X + radius;
            float centerY = arc.Y + radius;
            var normal = new PointF((float)Math.Cos(end), (float)Math.Sin(end));
            var tangent = new PointF(-normal.Y, normal.X);
            var point = new PointF(centerX + radius * normal.X, centerY + radius * normal.Y);
            float head = Math.Max(2.5f, bounds.Width * 0.34f);
            using (var brush = new SolidBrush(Color.White))
            {
                graphics.FillPolygon(brush, new[]
                {
                    new PointF(point.X + tangent.X * head * 0.7f, point.Y + tangent.Y * head * 0.7f),
                    new PointF(point.X + normal.X * head * 0.55f - tangent.X * head * 0.2f, point.Y + normal.Y * head * 0.55f - tangent.Y * head * 0.2f),
                    new PointF(point.X - normal.X * head * 0.55f - tangent.X * head * 0.2f, point.Y - normal.Y * head * 0.55f - tangent.Y * head * 0.2f)
                });
            }
        }

        private static Icon Create(Color left, Color right, TrayBadge badge)
        {
            using (Bitmap bitmap = Render(SystemInformation.SmallIconSize.Width, left, right, badge))
            {
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
