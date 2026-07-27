using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroLaman
{
    /// <summary>按 CameraShow 框选区域的比例显示完整 X×Y 扫描矩阵，不显示平台坐标。</summary>
    internal sealed class ScanMatrixPreviewControl : Control
    {
        private int columnCount;
        private int rowCount;
        private float selectionAspectRatio = 1f;
        private string statusText = "等待检测开始";

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
            statusText = text;
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
            Rectangle gridBounds = new Rectangle(
                available.Left + (int)((available.Width - width) / 2),
                available.Top + (int)((available.Height - height) / 2),
                Math.Max(1, (int)width), Math.Max(1, (int)height));

            using (Pen border = new Pen(Color.DeepSkyBlue, 2f))
            using (Pen grid = new Pen(Color.FromArgb(110, Color.Gray), 1f))
            using (Brush point = new SolidBrush(Color.Red))
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

                const float radius = 2.5f;
                for (int row = 0; row < rowCount; row++)
                {
                    float y = rowCount == 1
                        ? gridBounds.Top + gridBounds.Height / 2f
                        : gridBounds.Top + gridBounds.Height * row / (float)(rowCount - 1);
                    for (int column = 0; column < columnCount; column++)
                    {
                        float x = columnCount == 1
                            ? gridBounds.Left + gridBounds.Width / 2f
                            : gridBounds.Left + gridBounds.Width * column / (float)(columnCount - 1);
                        e.Graphics.FillEllipse(point, x - radius, y - radius, radius * 2, radius * 2);
                    }
                }
            }

            TextRenderer.DrawText(e.Graphics, "X (mm)", Font,
                new Rectangle(gridBounds.Left, gridBounds.Bottom + 8, gridBounds.Width, 22), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "Y (mm)", Font,
                new Rectangle(2, gridBounds.Top - 2, leftMargin - 6, 22), Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static void AddDistinct(List<float> values, float value)
        {
            foreach (float existing in values)
                if (Math.Abs(existing - value) < 0.00001f)
                    return;
            values.Add(value);
        }
    }
}
