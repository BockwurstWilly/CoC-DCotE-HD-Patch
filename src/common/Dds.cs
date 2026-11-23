using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CoCHD
{
    /// <summary>Decoder for the game's DXT1/DXT3/DXT5 .dds menu images (only the top mip level).</summary>
    static class Dds
    {
        public static Bitmap Load(string path)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length < 128 || BitConverter.ToUInt32(b, 0) != 0x20534444) // "DDS "
                return null;
            int h = BitConverter.ToInt32(b, 12), w = BitConverter.ToInt32(b, 16);
            string fourcc = System.Text.Encoding.ASCII.GetString(b, 84, 4);
            int blockSize = fourcc == "DXT1" ? 8 : fourcc == "DXT3" || fourcc == "DXT5" ? 16 : 0;
            if (blockSize == 0 || w <= 0 || h <= 0 || w > 4096 || h > 4096)
                return null;
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            if (b.Length < 128 + bw * bh * blockSize)
                return null;

            var px = new int[w * h];
            var colors = new int[4];
            int at = 128;
            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++, at += blockSize)
                {
                    int colorAt = blockSize == 16 ? at + 8 : at;
                    ushort c0 = BitConverter.ToUInt16(b, colorAt), c1 = BitConverter.ToUInt16(b, colorAt + 2);
                    colors[0] = Rgb565(c0);
                    colors[1] = Rgb565(c1);
                    if (c0 > c1 || blockSize == 16)
                    {
                        colors[2] = Mix(colors[0], colors[1], 2, 1);
                        colors[3] = Mix(colors[0], colors[1], 1, 2);
                    }
                    else
                    {
                        colors[2] = Mix(colors[0], colors[1], 1, 1);
                        colors[3] = 0;
                    }
                    uint idx = BitConverter.ToUInt32(b, colorAt + 4);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (x >= w || y >= h)
                            continue;
                        int c = colors[(idx >> (i * 2)) & 3];
                        // the menu images are opaque photos: alpha is ignored
                        px[y * w + x] = (int)(0xFF000000 | (uint)c);
                    }
                }
            }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
            bmp.UnlockBits(data);
            return bmp;
        }

        static int Rgb565(ushort c)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, bl = c & 31;
            return ((r << 3 | r >> 2) << 16) | ((g << 2 | g >> 4) << 8) | (bl << 3 | bl >> 2);
        }

        static int Mix(int a, int b, int wa, int wb)
        {
            int r = (((a >> 16) & 255) * wa + ((b >> 16) & 255) * wb) / (wa + wb);
            int g = (((a >> 8) & 255) * wa + ((b >> 8) & 255) * wb) / (wa + wb);
            int bl = ((a & 255) * wa + (b & 255) * wb) / (wa + wb);
            return (r << 16) | (g << 8) | bl;
        }

        /// <summary>Old-photograph look: sepia tone, slight contrast lift and a dark vignette.</summary>
        public static void Sepia(Bitmap bmp, float vignette = 0.75f)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var px = new int[w * h];
            for (int y = 0; y < h; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w, w);
            var rnd = new Random(1928);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int c = px[y * w + x];
                    double r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                    double l = (0.3 * r + 0.59 * g + 0.11 * b) / 255.0;
                    l = Math.Pow(l, 1.1) * 1.08;
                    double dx = (x - w / 2.0) / (w / 2.0), dy = (y - h / 2.0) / (h / 2.0);
                    double v = 1.0 - vignette * Math.Min(1.0, (dx * dx + dy * dy) * 0.55);
                    double grain = (rnd.NextDouble() - 0.5) * 0.05;
                    l = Math.Max(0, Math.Min(1, l * v + grain));
                    int nr = (int)(255 * Math.Min(1, l * 1.07)), ng = (int)(255 * Math.Min(1, l * 0.93)), nb = (int)(255 * Math.Min(1, l * 0.72));
                    px[y * w + x] = (int)(0xFF000000 | (uint)(nr << 16 | ng << 8 | nb));
                }
            }
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
            bmp.UnlockBits(data);
        }
    }
}
