using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroRaman
{
    /// <summary>
    /// 按 CameraShow 框选区域的比例显示完整 X×Y 扫描矩阵，不显示平台坐标。
    /// </summary>
    internal sealed class ScanMatrixPreviewControl : Control
    {
        private sealed class ScanPoint
        {
            internal int ScanIndex;
            internal int Column;
            internal int Row;
        }

        private int columnCount;
        private int rowCount;
        private float selectionAspectRatio = 1f;
        private string statusText = "等待检测开始";
        private readonly List<ScanPoint> scanPoints = new List<ScanPoint>();
        private readonly HashSet<int> spectrumAvailableIndexes = new HashSet<int>();
        private readonly Dictionary<int, Color> mappingColors = new Dictionary<int, Color>();

        /// <summary>
        /// 用户点击扫描矩阵中的一个点。
        /// </summary>
        internal event EventHandler<ScanPointSelectedEventArgs> ScanPointSelected;

        internal ScanMatrixPreviewControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            ResizeRedraw = true;
        }

        /// <summary>
        /// 按 CameraShow 框选的实际宽高比显示网格；仅允许整体等比缩放。
        /// </summary>
        internal void SetScanGrid(IList<PointF> points, float cameraSelectionAspectRatio)
        {
            if (points == null || points.Count == 0)
            {
                SetStatus("等待检测开始");
                return;
            }

            List<float> columns = new List<float>();
            List<float> rows = new List<float>();
            foreach (PointF point in points)
            {
                AddDistinct(columns, point.X);
                AddDistinct(rows, point.Y);
            }
            columns.Sort();
            rows.Sort();
            columnCount = columns.Count;
            rowCount = rows.Count;
            scanPoints.Clear();
            spectrumAvailableIndexes.Clear();
            mappingColors.Clear();
            for (int index = 0; index < points.Count; index++)
            {
                PointF point = points[index];
                scanPoints.Add(new ScanPoint
                {
                    ScanIndex = index,
                    Column = GetDistinctIndex(columns, point.X),
                    Row = GetDistinctIndex(rows, point.Y)
                });
            }
            float width = columnCount > 1 ? columns[columnCount - 1] - columns[0] : 1f;
            float height = rowCount > 1 ? rows[rowCount - 1] - rows[0] : 1f;
            float measuredAspectRatio = width / Math.Max(0.0001f, height);
            selectionAspectRatio = cameraSelectionAspectRatio > 0f
                ? cameraSelectionAspectRatio
                : Math.Max(0.05f, measuredAspectRatio);
            statusText = null;
            Invalidate();
        }

        /// <summary>
        /// 设置Status相关的内部处理。
        /// </summary>
        private void SetStatus(string text)
        {
            columnCount = 0;
            rowCount = 0;
            scanPoints.Clear();
            spectrumAvailableIndexes.Clear();
            mappingColors.Clear();
            statusText = text;
            Invalidate();
        }

        /// <summary>
        /// 把已完成开激光采谱的点标记为可点击回看。
        /// </summary>
        /// <summary>
        /// 批量标记后台扫描刚完成的点，只触发一次重绘。
        /// </summary>
        internal void SetSpectraAvailable(IEnumerable<int> scanIndexes)
        {
            if (scanIndexes == null)
                return;
            bool changed = false;
            foreach (int scanIndex in scanIndexes)
            {
                if (scanIndex >= 0 && scanIndex < scanPoints.Count)
                    changed |= spectrumAvailableIndexes.Add(scanIndex);
            }
            if (changed)
                Invalidate();
        }

        /// <summary>
        /// 保留当前网格，但清除上一轮扫描的已保存光谱和选中状态。
        /// </summary>
        internal void ClearSpectrumAvailability()
        {
            spectrumAvailableIndexes.Clear();
            mappingColors.Clear();
            Cursor = Cursors.Default;
            Invalidate();
        }

        /// <summary>
        /// 显示每个扫描点的拉曼 Mapping 伪彩色；颜色区域由相邻点中心的中点分隔。
        /// </summary>
        internal void SetMappingColors(IDictionary<int, Color> colors)
        {
            mappingColors.Clear();
            Cursor = Cursors.Default;
            if (colors != null)
            {
                foreach (KeyValuePair<int, Color> pair in colors)
                {
                    if (pair.Key >= 0 && pair.Key < scanPoints.Count)
                        mappingColors[pair.Key] = pair.Value;
                }
            }
            Invalidate();
        }

        /// <summary>
        /// 清空MappingColors相关的内部处理。
        /// </summary>
        internal void ClearMappingColors()
        {
            if (mappingColors.Count == 0)
                return;
            mappingColors.Clear();
            Invalidate();
        }

        /// <summary>
        /// 处理Paint相关的内部处理。
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (columnCount == 0 || rowCount == 0)
            {
                TextRenderer.DrawText(e.Graphics, statusText ?? "等待检测开始", Font, ClientRectangle,
                    Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            Rectangle gridBounds = GetGridBounds();
            bool showMapping = mappingColors.Count > 0;
            // 扫描点始终位于格子中心；Mapping 前后都保留外侧半格，避免视图切换时几何位置跳动。
            if (showMapping)
            {
                DrawMappingCells(e.Graphics, gridBounds);
            }
            else
            {
                DrawMappingGrid(e.Graphics, gridBounds);
            }

            TextRenderer.DrawText(e.Graphics, "X", Font,
                new Rectangle(gridBounds.Left, gridBounds.Bottom + 8, gridBounds.Width, 22), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "Y", Font,
                new Rectangle(2, gridBounds.Top - 2, Math.Max(1, gridBounds.Left - 6), 22), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>
        /// 绘制MappingCells相关的内部处理。
        /// </summary>
        private void DrawMappingCells(Graphics graphics, Rectangle gridBounds)
        {
            foreach (ScanPoint scanPoint in scanPoints)
            {
                Color color;
                if (!mappingColors.TryGetValue(scanPoint.ScanIndex, out color))
                    color = Color.White;
                RectangleF cellBounds = GetMappingCellBounds(gridBounds, scanPoint);
                using (Brush brush = new SolidBrush(color))
                    graphics.FillRectangle(brush, cellBounds);
            }
        }

        /// <summary>
        /// 使用绿色绘制所有格线，并用更醒目的绿色绘制外边框。
        /// </summary>
        private void DrawMappingGrid(Graphics graphics, Rectangle gridBounds)
        {
            using (Pen grid = new Pen(Color.FromArgb(180, Color.ForestGreen), 1f))
            {
                for (int column = 1; column < columnCount; column++)
                {
                    float x = gridBounds.Left
                        + gridBounds.Width * column / (float)columnCount;
                    graphics.DrawLine(grid, x, gridBounds.Top, x, gridBounds.Bottom);
                }
                for (int row = 1; row < rowCount; row++)
                {
                    float y = gridBounds.Top
                        + gridBounds.Height * row / (float)rowCount;
                    graphics.DrawLine(grid, gridBounds.Left, y, gridBounds.Right, y);
                }
            }

            using (Pen border = new Pen(Color.ForestGreen, 2f))
                graphics.DrawRectangle(border, gridBounds);
        }

        /// <summary>
        /// 返回以扫描点为中心的完整格子；边缘格子向最外侧点中心之外延伸半个间距。
        /// </summary>
        private RectangleF GetMappingCellBounds(Rectangle gridBounds, ScanPoint scanPoint)
        {
            float cellWidth = gridBounds.Width / (float)Math.Max(1, columnCount);
            float cellHeight = gridBounds.Height / (float)Math.Max(1, rowCount);
            return new RectangleF(
                gridBounds.Left + scanPoint.Column * cellWidth,
                gridBounds.Top + scanPoint.Row * cellHeight,
                cellWidth,
                cellHeight);
        }

        /// <summary>仅在坐标尚未存在时添加。</summary>
        private static void AddDistinct(List<float> values, float value)
        {
            foreach (float existing in values)
                if (Math.Abs(existing - value) < 0.00001f)
                    return;
            values.Add(value);
        }

        /// <summary>查找容差范围内匹配的坐标索引。</summary>
        private static int GetDistinctIndex(List<float> values, float value)
        {
            for (int index = 0; index < values.Count; index++)
                if (Math.Abs(values[index] - value) < 0.00001f)
                    return index;
            return -1;
        }

        /// <summary>将完整的外层格子矩形等比放入控件。</summary>
        private Rectangle GetGridBounds()
        {
            const int leftMargin = 50;
            const int topMargin = 20;
            const int rightMargin = 20;
            const int bottomMargin = 44;
            Rectangle available = Rectangle.FromLTRB(leftMargin, topMargin,
                Math.Max(leftMargin + 1, ClientSize.Width - rightMargin),
                Math.Max(topMargin + 1, ClientSize.Height - bottomMargin));
            float displayAspectRatio = selectionAspectRatio
                * columnCount / (float)Math.Max(1, columnCount - 1)
                / (rowCount / (float)Math.Max(1, rowCount - 1));

            float width = available.Width;
            float height = width / displayAspectRatio;
            if (height > available.Height)
            {
                height = available.Height;
                width = height * displayAspectRatio;
            }
            return new Rectangle(
                available.Left + (int)((available.Width - width) / 2),
                available.Top + (int)((available.Height - height) / 2),
                Math.Max(1, (int)width), Math.Max(1, (int)height));
        }

        /// <summary>
        /// 处理MouseMove相关的内部处理。
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (spectrumAvailableIndexes.Count == 0)
            {
                Cursor = Cursors.Default;
                return;
            }
            int scanIndex = HitTest(e.Location);
            Cursor = scanIndex >= 0 && spectrumAvailableIndexes.Contains(scanIndex)
                ? Cursors.Hand
                : Cursors.Default;
        }

        /// <summary>
        /// 处理MouseLeave相关的内部处理。
        /// </summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Cursor = Cursors.Default;
        }

        /// <summary>
        /// 处理MouseClick相关的内部处理。
        /// </summary>
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;
            int scanIndex = HitTest(e.Location);
            if (scanIndex < 0 || !spectrumAvailableIndexes.Contains(scanIndex))
                return;
            EventHandler<ScanPointSelectedEventArgs> handler = ScanPointSelected;
            if (handler != null)
                handler(this, new ScanPointSelectedEventArgs(scanIndex));
        }

        /// <summary>
        /// 执行 HitTest 相关的内部处理。
        /// </summary>
        private int HitTest(Point location)
        {
            if (scanPoints.Count == 0)
                return -1;
            Rectangle gridBounds = GetGridBounds();
            foreach (ScanPoint scanPoint in scanPoints)
            {
                if (GetMappingCellBounds(gridBounds, scanPoint).Contains(location))
                    return scanPoint.ScanIndex;
            }
            return -1;
        }
    }

    /// <summary>
    /// 扫描矩阵中被点击的点的蛇形路径序号。
    /// </summary>
    internal sealed class ScanPointSelectedEventArgs : EventArgs
    {
        internal ScanPointSelectedEventArgs(int scanIndex)
        {
            ScanIndex = scanIndex;
        }

        internal int ScanIndex { get; private set; }
    }
}
