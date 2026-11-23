using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace CoCHD
{
    /// <summary>The title banner of launcher and setup: game title plus an aged photo from the main menu.</summary>
    static class Banner
    {
        /// <summary>One of the main menu's "engaging screens" (from the installed game), sepia-toned; null if none.</summary>
        public static Bitmap Load(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir))
                    return null;
                var screens = Directory.GetFiles(Path.Combine(dir, @"MainMenu\ENGAGINGSCREENS"), "*.dds");
                if (screens.Length == 0)
                    return null;
                var bmp = Dds.Load(screens[new Random().Next(screens.Length)]);
                if (bmp != null)
                    Dds.Sepia(bmp);
                return bmp;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Paint(Graphics g, Rectangle r, Bitmap photo, string tag)
        {
            g.Clear(Theme.Back);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (photo != null)
            {
                // the photograph on the right, faded into the dark page on the left
                int w = r.Height * 2;
                var dst = new Rectangle(r.Right - w, r.Top, w, r.Height);
                g.DrawImage(photo, dst, new Rectangle(0, photo.Height / 4, photo.Width, photo.Height / 2), GraphicsUnit.Pixel);
                using (var fade = new LinearGradientBrush(new Rectangle(dst.Left - 1, r.Top, w / 2 + 2, r.Height), Theme.Back, Color.FromArgb(0, Theme.Back), LinearGradientMode.Horizontal))
                    g.FillRectangle(fade, dst.Left, r.Top, w / 2, r.Height);
            }
            int fh = Theme.S(30);
            using (var fade = new LinearGradientBrush(new Rectangle(r.Left, r.Bottom - fh - 1, r.Width, fh + 2), Color.FromArgb(0, Theme.Back), Theme.Back, LinearGradientMode.Vertical))
                g.FillRectangle(fade, r.Left, r.Bottom - fh, r.Width, fh);

            // the title block is laid out for a 150-unit banner and shrinks with smaller ones
            float k = r.Height / (float)Theme.S(150);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var small = Theme.Display(11f * k, FontStyle.Regular))
            using (var big = Theme.Display(28f * k))
            using (var sub = Theme.Display(13f * k, FontStyle.Italic))
            using (var type = new Font("Courier New", 9f * Math.Max(k, 0.85f), FontStyle.Bold))
            using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var brass = new SolidBrush(Theme.Brass))
            using (var text = new SolidBrush(Theme.Text))
            using (var green = new SolidBrush(Theme.AccentLight))
            {
                Func<float, float> s = v => Theme.S(v * k);
                g.DrawString("C A L L   O F", small, brass, s(26), r.Top + s(20));
                g.DrawString("CTHULHU", big, shadow, s(24), r.Top + s(38));
                g.DrawString("CTHULHU", big, text, s(22), r.Top + s(36));
                g.DrawString("Dark Corners of the Earth", sub, brass, s(26), r.Top + s(84));
                Theme.Rule(g, (int)s(26), (int)s(330), r.Top + (int)s(114));
                g.DrawString(tag, type, green, s(26), r.Top + s(124));
            }
        }
    }
}
