using System.Drawing;
using System.Drawing.Drawing2D;

namespace MicroLaman
{
    /// <summary>为主工具栏生成与现有按钮一致的 44×44 透明线稿图标。</summary>
    internal static class ToolbarIconFactory
    {
        internal static Bitmap CreateRealtimeSpectrumIcon()
        {
            Bitmap bitmap = CreateCanvas();
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen axis = new Pen(Color.FromArgb(85, 85, 85), 2f))
            using (Pen spectrum = new Pen(Color.FromArgb(220, 45, 45), 2.4f))
            {
                PrepareGraphics(graphics);
                axis.StartCap = LineCap.Round;
                axis.EndCap = LineCap.Round;
                spectrum.StartCap = LineCap.Round;
                spectrum.EndCap = LineCap.Round;
                spectrum.LineJoin = LineJoin.Round;

                graphics.DrawLine(axis, 6, 6, 6, 37);
                graphics.DrawLine(axis, 6, 37, 39, 37);
                graphics.DrawLines(spectrum, new[]
                {
                    new PointF(8, 31),
                    new PointF(12, 29),
                    new PointF(15, 31),
                    new PointF(19, 23),
                    new PointF(22, 27),
                    new PointF(26, 12),
                    new PointF(29, 27),
                    new PointF(33, 20),
                    new PointF(38, 22)
                });
            }
            return bitmap;
        }

        internal static Bitmap CreateRamanMappingIcon()
        {
            Bitmap bitmap = CreateCanvas();
            Color[,] colors =
            {
                { Color.FromArgb(42, 92, 190), Color.FromArgb(0, 168, 220), Color.FromArgb(66, 190, 125), Color.FromArgb(242, 205, 55) },
                { Color.FromArgb(0, 151, 218), Color.FromArgb(62, 191, 130), Color.FromArgb(246, 213, 55), Color.FromArgb(225, 92, 55) },
                { Color.FromArgb(47, 178, 170), Color.FromArgb(225, 211, 58), Color.FromArgb(231, 117, 50), Color.FromArgb(190, 49, 58) },
                { Color.FromArgb(36, 128, 206), Color.FromArgb(67, 187, 126), Color.FromArgb(239, 184, 51), Color.FromArgb(208, 55, 55) }
            };

            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen outline = new Pen(Color.FromArgb(75, 75, 75), 1.8f))
            {
                PrepareGraphics(graphics);
                const int left = 6;
                const int top = 6;
                const int cell = 8;
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        using (Brush brush = new SolidBrush(colors[row, column]))
                            graphics.FillRectangle(brush, left + column * cell, top + row * cell, cell, cell);
                    }
                }

                graphics.DrawRectangle(outline, left, top, cell * 4, cell * 4);
                for (int index = 1; index < 4; index++)
                {
                    graphics.DrawLine(outline, left + index * cell, top, left + index * cell, top + cell * 4);
                    graphics.DrawLine(outline, left, top + index * cell, left + cell * 4, top + index * cell);
                }
            }
            return bitmap;
        }

        private static Bitmap CreateCanvas()
        {
            return new Bitmap(44, 44, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        }

        private static void PrepareGraphics(Graphics graphics)
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }
    }
}
