using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Blankedge.SWHoleTools.Utilities;

namespace Blankedge.SWHoleTools.UI
{
    public sealed class CommandIconSet
    {
        public string[] MainIcons { get; set; }
        public string[] CommandStrips { get; set; }
    }

    public static class CommandIconProvider
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };
        private static readonly string[] Glyphs = { "R", "+", "E", "×", "?", "N", "T" };
        private static readonly Color[] Colors =
        {
            Color.FromArgb(38, 120, 210),
            Color.FromArgb(20, 150, 95),
            Color.FromArgb(230, 145, 35),
            Color.FromArgb(205, 65, 65),
            Color.FromArgb(120, 85, 180),
            Color.FromArgb(35, 145, 165),
            Color.FromArgb(80, 95, 120)
        };

        public static CommandIconSet EnsureCreated()
        {
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "Blankedge.SWHoleTools", "Icons", "v2");
                Directory.CreateDirectory(directory);
                var main = new string[Sizes.Length];
                var strips = new string[Sizes.Length];

                for (int i = 0; i < Sizes.Length; i++)
                {
                    int size = Sizes[i];
                    main[i] = Path.Combine(directory, $"main-{size}.png");
                    strips[i] = Path.Combine(directory, $"commands-{size}.png");
                    if (!File.Exists(main[i]))
                    {
                        CreateMainIcon(main[i], size);
                    }
                    if (!File.Exists(strips[i]))
                    {
                        CreateCommandStrip(strips[i], size);
                    }
                }

                return new CommandIconSet { MainIcons = main, CommandStrips = strips };
            }
            catch (Exception ex)
            {
                Logger.Error("创建 CommandManager 临时图标失败。", ex);
                return null;
            }
        }

        private static void CreateMainIcon(string path, int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                Configure(graphics);
                graphics.Clear(Color.Transparent);
                float margin = Math.Max(1.0f, size * 0.08f);
                using (var brush = new SolidBrush(Color.FromArgb(38, 120, 210)))
                {
                    graphics.FillEllipse(brush, margin, margin, size - (2 * margin), size - (2 * margin));
                }
                DrawGlyph(graphics, "H", Color.White, 0, size, size);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void CreateCommandStrip(string path, int size)
        {
            using (var bitmap = new Bitmap(size * Glyphs.Length, size, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                Configure(graphics);
                graphics.Clear(Color.Transparent);
                float margin = Math.Max(1.0f, size * 0.08f);
                for (int i = 0; i < Glyphs.Length; i++)
                {
                    float left = (i * size) + margin;
                    using (var brush = new SolidBrush(Colors[i]))
                    {
                        graphics.FillRoundedRectangle(brush, left, margin, size - (2 * margin), size - (2 * margin), size * 0.18f);
                    }
                    DrawGlyph(graphics, Glyphs[i], Color.White, i * size, size, size);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static void Configure(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        private static void DrawGlyph(Graphics graphics, string glyph, Color color, int left, int width, int height)
        {
            float fontSize = Math.Max(8.0f, height * 0.52f);
            using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(glyph, font, brush, new RectangleF(left, 0, width, height), format);
            }
        }

        private static void FillRoundedRectangle(this Graphics graphics, Brush brush, float x, float y, float width, float height, float radius)
        {
            float diameter = radius * 2.0f;
            using (var path = new GraphicsPath())
            {
                path.AddArc(x, y, diameter, diameter, 180, 90);
                path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
                path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
                path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }
    }
}
