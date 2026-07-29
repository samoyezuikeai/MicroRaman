using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroLaman
{
    /// <summary>按 CameraShow 框选区域的比例显示完整 X×Y 扫描矩阵，不显示平台坐标。</summary>
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
        private int hoveredScanIndex = -1;
        private int selectedScanIndex = -1;

        /// <summary>用户点击扫描矩阵中的一个点。</summary>
        internal event EventHandler<ScanPointSelectedEventArgs> ScanPointSelected;

        internal ScanMatrixPreviewControl()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            ResizeRedraw = true;
        }

        /// <summary>按 CameraShow 框选的实际宽高比显示网格；仅允许整体等比缩放。</summary>
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
            hoveredScanIndex = -1;
            selectedScanIndex = -1;
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

        private void SetStatus(string text)
        {
            columnCount = 0;
            rowCount = 0;
            scanPoints.Clear();
            spectrumAvailableIndexes.Clear();
            hoveredScanIndex = -1;
            selectedScanIndex = -1;
            statusText = text;
            Invalidate();
        }

        /// <summary>把已完成开激光采谱的点标记为可点击回看。</summary>
        internal void SetSpectrumAvailable(int scanIndex)
        {
            if (scanIndex < 0 || scanIndex >= scanPoints.Count)
                return;
            spectrumAvailableIndexes.Add(scanIndex);
            Invalidate();
        }

        /// <summary>保留当前网格，但清除上一轮扫描的已保存光谱和选中状态。</summary>
        internal void ClearSpectrumAvailability()
        {
            spectrumAvailableIndexes.Clear();
            hoveredScanIndex = -1;
            selectedScanIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (columnCount == 0 || rowCount == 0)
            {
                TextRenderer.DrawText(e.Graphics, statusText ?? "等待检测开始", Font, ClientRectangle,
                    Color.DimGray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            Rectangle gridBounds;
            if (!TryGetGridBounds(out gridBounds))
                return;

            using (Pen border = new Pen(Color.DeepSkyBlue, 2f))
            using (Pen grid = new Pen(Color.FromArgb(110, Color.Gray), 1f))
            {
                e.Graphics.DrawRectangle(border, gridBounds);
                for (int column = 1; column < columnCount - 1; column++)
                {
                    float x = gridBounds.Left + gridBounds.Width * column / (float)(columnCount - 1);
                    e.Graphics.DrawLine(grid, x, gridBounds.Top, x, gridBounds.Bottom);
                }
                for (int row = 1; row < rowCount - 1; row++)
                {
                    float y = gridBounds.Top + gridBounds.Height * row / (float)(rowCount - 1);
                    e.Graphics.DrawLine(grid, gridBounds.Left, y, gridBounds.Right, y);
                }

                foreach (ScanPoint scanPoint in scanPoints)
                {
                    PointF pointLocation = GetPointLocation(gridBounds, scanPoint);
                    bool selected = scanPoint.ScanIndex == selectedScanIndex;
                    bool hovered = scanPoint.ScanIndex == hoveredScanIndex;
                    bool available = spectrumAvailableIndexes.Contains(scanPoint.ScanIndex);
                    float radius = selected || hovered ? 5.5f : available ? 4.5f : 2.5f;
                    Color color = selected ? Color.RoyalBlue
                        : hovered ? Color.Gold
                        : available ? Color.ForestGreen
                        : Color.Red;
                    using (Brush point = new SolidBrush(color))
                        e.Graphics.FillEllipse(point, pointLocation.X - radius, pointLocation.Y - radius,
                            radius * 2, radius * 2);
                }
            }

            TextRenderer.DrawText(e.Graphics, "X (mm)", Font,
                new Rectangle(gridBounds.Left, gridBounds.Bottom + 8, gridBounds.Width, 22), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "Y (mm)", Font,
                new Rectangle(2, gridBounds.Top - 2, Math.Max(1, gridBounds.Left - 6), 22), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static void AddDistinct(List<float> values, float value)
        {
            foreach (float existing in values)
                if (Math.Abs(existing - value) < 0.00001f)
                    return;
            values.Add(value);
        }

        private static int GetDistinctIndex(List<float> values, float value)
        {
            for (int index = 0; index < values.Count; index++)
                if (Math.Abs(values[index] - value) < 0.00001f)
                    return index;
            return -1;
        }

        private bool TryGetGridBounds(out Rectangle gridBounds)
        {
            const int leftMargin = 50;
            const int topMargin = 20;
            const int rightMargin = 20;
            const int bottomMargin = 44;
            Rectangle available = Rectangle.FromLTRB(leftMargin, topMargin,
                Math.Max(leftMargin + 1, ClientSize.Width - rightMargin),
                Math.Max(topMargin + 1, ClientSize.Height - bottomMargin));
            float width = available.Width;
            float height = width / selectionAspectRatio;
            if (height > available.Height)
            {
                height = available.Height;
                width = height * selectionAspectRatio;
            }
            gridBounds = new Rectangle(
                available.Left + (int)((available.Width - width) / 2),
                available.Top + (int)((available.Height - height) / 2),
                Math.Max(1, (int)width), Math.Max(1, (int)height));
            return true;
        }

        private PointF GetPointLocation(Rectangle gridBounds, ScanPoint scanPoint)
        {
            float x = columnCount == 1
                ? gridBounds.Left + gridBounds.Width / 2f
                : gridBounds.Left + gridBounds.Width * scanPoint.Column / (float)(columnCount - 1);
            float y = rowCount == 1
                ? gridBounds.Top + gridBounds.Height / 2f
                : gridBounds.Top + gridBounds.Height * scanPoint.Row / (float)(rowCount - 1);
            return new PointF(x, y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int scanIndex = HitTest(e.Location);
            if (hoveredScanIndex == scanIndex)
                return;
            hoveredScanIndex = scanIndex;
            Cursor = scanIndex >= 0 && spectrumAvailableIndexes.Contains(scanIndex)
                ? Cursors.Hand
                : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredScanIndex < 0)
                return;
            hoveredScanIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;
            int scanIndex = HitTest(e.Location);
            if (scanIndex < 0 || !spectrumAvailableIndexes.Contains(scanIndex))
                return;
            selectedScanIndex = scanIndex;
            Invalidate();
            EventHandler<ScanPointSelectedEventArgs> handler = ScanPointSelected;
            if (handler != null)
                handler(this, new ScanPointSelectedEventArgs(scanIndex));
        }

        private int HitTest(Point location)
        {
            if (scanPoints.Count == 0)
                return -1;
            Rectangle gridBounds;
            if (!TryGetGridBounds(out gridBounds))
                return -1;

            const float hitRadius = 9f;
            float hitRadiusSquared = hitRadius * hitRadius;
            foreach (ScanPoint scanPoint in scanPoints)
            {
                PointF pointLocation = GetPointLocation(gridBounds, scanPoint);
                float dx = location.X - pointLocation.X;
                float dy = location.Y - pointLocation.Y;
                if (dx * dx + dy * dy <= hitRadiusSquared)
                    return scanPoint.ScanIndex;
            }
            return -1;
        }
    }

    /// <summary>扫描矩阵中被点击的点的蛇形路径序号。</summary>
    internal sealed class ScanPointSelectedEventArgs : EventArgs
    {
        internal ScanPointSelectedEventArgs(int scanIndex)
        {
            ScanIndex = scanIndex;
        }

        internal int ScanIndex { get; private set; }
    }
}
