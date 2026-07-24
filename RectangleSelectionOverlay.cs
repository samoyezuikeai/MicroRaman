using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroLaman
{
    /// <summary>
    /// 保存并绘制与相机帧合成的框选区域、扫描网格和已访问点。
    /// </summary>
    internal sealed class RectangleSelectionOverlay
    {
        private readonly object stateSync = new object();
        private RectangleF normalizedSelection = RectangleF.Empty;
        private int xPointCount = 3;
        private int yPointCount = 3;
        private readonly List<PointF> recordedScanPoints = new List<PointF>();
        private Bitmap staticLayer;
        private Size staticLayerSize;
        private int staticLayerXCount;
        private int staticLayerYCount;

        /// <summary>
        /// 更新预览控件归一化坐标中的框选区域。
        /// </summary>
        internal void SetSelection(RectangleF selection)
        {
            lock (stateSync)
            {
                if (normalizedSelection == selection)
                    return;
                normalizedSelection = selection;
                InvalidateStaticLayer();
            }
        }

        /// <summary>
        /// 清除当前框选区域。
        /// </summary>
        internal void ClearSelection()
        {
            SetSelection(RectangleF.Empty);
        }

        /// <summary>
        /// 设置网格在 X、Y 方向上的点数。
        /// </summary>
        internal void SetGridSize(int xCount, int yCount)
        {
            lock (stateSync)
            {
                int normalizedXCount = Math.Max(1, xCount);
                int normalizedYCount = Math.Max(1, yCount);
                if (xPointCount == normalizedXCount && yPointCount == normalizedYCount)
                    return;
                xPointCount = normalizedXCount;
                yPointCount = normalizedYCount;
                InvalidateStaticLayer();
            }
        }

        /// <summary>
        /// 用归一化预览坐标更新扫描过程中记录的红点。
        /// </summary>
        internal void SetRecordedScanPoints(IEnumerable<PointF> points)
        {
            lock (stateSync)
            {
                recordedScanPoints.Clear();
                if (points != null)
                    recordedScanPoints.AddRange(points);
            }
        }

        /// <summary>
        /// 将全部标注直接绘制到当前相机预览帧表面。
        /// </summary>
        internal void Draw(Graphics graphics, Size clientSize)
        {
            RectangleF selection;
            int xCount;
            int yCount;
            List<PointF> recordedPoints;
            lock (stateSync)
            {
                selection = normalizedSelection;
                xCount = xPointCount;
                yCount = yPointCount;
                recordedPoints = new List<PointF>(recordedScanPoints);
            }

            if (!selection.IsEmpty && clientSize.Width > 0 && clientSize.Height > 0)
            {
                Rectangle rectangle = new Rectangle(
                    (int)Math.Round(selection.X * clientSize.Width),
                    (int)Math.Round(selection.Y * clientSize.Height),
                    (int)Math.Round(selection.Width * clientSize.Width),
                    (int)Math.Round(selection.Height * clientSize.Height));

                rectangle = Rectangle.Intersect(rectangle, new Rectangle(Point.Empty, clientSize));
                if (rectangle.Width >= 2 && rectangle.Height >= 2)
                {
                    rectangle.Width -= 1;
                    rectangle.Height -= 1;
                    DrawStaticLayer(graphics, rectangle, xCount, yCount);
                }
            }

            DrawRecordedScanPoints(graphics, clientSize, recordedPoints);
        }

        /// <summary>绘制已缓存的框线与完整黄色网格，只有尺寸或点数改变时才重新生成。</summary>
        private void DrawStaticLayer(Graphics graphics, Rectangle rectangle, int xCount, int yCount)
        {
            Bitmap layer;
            lock (stateSync)
            {
                if (staticLayer == null
                    || staticLayerSize != rectangle.Size
                    || staticLayerXCount != xCount
                    || staticLayerYCount != yCount)
                    CreateStaticLayer(rectangle.Size, xCount, yCount);
                layer = staticLayer;
                graphics.DrawImageUnscaled(layer, rectangle.Location);
            }
        }

        /// <summary>释放缓存的完整网格位图。</summary>
        internal void Dispose()
        {
            lock (stateSync)
                InvalidateStaticLayer();
        }

        /// <summary>创建包含完整边框和全部黄色目标点的透明缓存图层。</summary>
        private void CreateStaticLayer(Size size, int xCount, int yCount)
        {
            InvalidateStaticLayer();
            staticLayer = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height));
            staticLayerSize = size;
            staticLayerXCount = xCount;
            staticLayerYCount = yCount;

            using (Graphics graphics = Graphics.FromImage(staticLayer))
            using (Pen shadow = new Pen(Color.Black, 4f))
            using (Pen border = new Pen(Color.DeepSkyBlue, 2f))
            using (Brush pointBrush = new SolidBrush(Color.Yellow))
            using (Pen pointOutline = new Pen(Color.Black, 1f))
            {
                Rectangle rectangle = new Rectangle(0, 0, staticLayer.Width - 1, staticLayer.Height - 1);
                graphics.DrawRectangle(shadow, rectangle);
                graphics.DrawRectangle(border, rectangle);
                const int radius = 3;
                for (int yIndex = 0; yIndex < yCount; yIndex++)
                {
                    float y = yCount == 1
                        ? rectangle.Top + rectangle.Height / 2f
                        : rectangle.Top + yIndex * rectangle.Height / (float)(yCount - 1);
                    for (int xIndex = 0; xIndex < xCount; xIndex++)
                    {
                        float x = xCount == 1
                            ? rectangle.Left + rectangle.Width / 2f
                            : rectangle.Left + xIndex * rectangle.Width / (float)(xCount - 1);
                        RectangleF marker = new RectangleF(x - radius, y - radius, radius * 2, radius * 2);
                        graphics.FillEllipse(pointBrush, marker);
                        graphics.DrawEllipse(pointOutline, marker);
                    }
                }
            }
        }

        /// <summary>释放过期的静态网格缓存。</summary>
        private void InvalidateStaticLayer()
        {
            if (staticLayer != null)
            {
                staticLayer.Dispose();
                staticLayer = null;
            }
            staticLayerSize = Size.Empty;
        }

        /// <summary>
        /// 绘制扫描后记录的红色实际到达点。
        /// </summary>
        private static void DrawRecordedScanPoints(Graphics graphics, Size clientSize, IList<PointF> points)
        {
            if (points.Count == 0)
                return;

            const float radius = 3f;
            using (Brush fill = new SolidBrush(Color.Red))
            using (Pen outline = new Pen(Color.White, 1f))
            {
                foreach (PointF point in points)
                {
                    float x = point.X * clientSize.Width;
                    float y = point.Y * clientSize.Height;
                    RectangleF marker = new RectangleF(x - radius, y - radius, radius * 2, radius * 2);
                    graphics.FillEllipse(fill, marker);
                    graphics.DrawEllipse(outline, marker);
                }
            }
        }
    }
}
