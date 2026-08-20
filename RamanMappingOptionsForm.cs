using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MicroRaman
{
    internal enum RamanMappingMode
    {
        PeakHeight,
        PeakArea,
        PeakPosition,
        PeakWidth,
        FullSpectrumDifference,
        Pca
    }

    internal enum RamanPeakMetric
    {
        Height,
        Area
    }

    internal sealed class RamanPeakRange
    {
        internal RamanPeakRange(
            double rangeStart,
            double rangeEnd,
            Color color,
            RamanPeakMetric metric)
        {
            RangeStart = rangeStart;
            RangeEnd = rangeEnd;
            Color = color;
            Metric = metric;
        }

        internal double RangeStart { get; private set; }
        internal double RangeEnd { get; private set; }
        internal Color Color { get; private set; }
        internal RamanPeakMetric Metric { get; private set; }
    }

    /// <summary>
    /// 配置一个或多个目标拉曼峰，每一行保存为独立的伪彩色通道。
    /// </summary>
    internal sealed class RamanMappingOptionsForm : Form
    {
        private sealed class PeakRangeRow
        {
            internal Label NameLabel;
            internal NumericUpDown StartInput;
            internal NumericUpDown EndInput;
            internal ComboBox MetricInput;
            internal Button ColorButton;
            internal PeakColorChoice SelectedColor;
            internal Label Separator;
            internal Button DeleteButton;
        }

        private sealed class PeakColorChoice
        {
            internal PeakColorChoice(string name, Color color)
            {
                Name = name;
                Color = color;
            }

            internal string Name { get; private set; }
            internal Color Color { get; private set; }
            public override string ToString() { return Name; }
        }

        private readonly RamanMappingMode mappingMode;
        private readonly Panel rowsPanel;
        private readonly Button addButton;
        private readonly List<PeakRangeRow> rows = new List<PeakRangeRow>();
        private readonly List<RamanPeakRange> peakRanges = new List<RamanPeakRange>();
        private readonly List<PeakColorChoice> peakColorChoices = new List<PeakColorChoice>
        {
            new PeakColorChoice("红色", Color.FromArgb(235, 35, 35)),
            new PeakColorChoice("橙色", Color.FromArgb(245, 130, 20)),
            new PeakColorChoice("黄色", Color.FromArgb(235, 215, 20)),
            new PeakColorChoice("黄绿色", Color.FromArgb(145, 205, 35)),
            new PeakColorChoice("绿色", Color.FromArgb(20, 210, 75)),
            new PeakColorChoice("青色", Color.FromArgb(20, 185, 205)),
            new PeakColorChoice("蓝色", Color.FromArgb(25, 100, 230)),
            new PeakColorChoice("靛色", Color.FromArgb(75, 45, 165)),
            new PeakColorChoice("紫色", Color.FromArgb(160, 65, 205))
        };

        internal bool Accepted { get; private set; }

        internal RamanMappingOptionsForm(
            RamanMappingMode mappingMode,
            IList<RamanPeakRange> existingRanges)
        {
            this.mappingMode = mappingMode;
            Text = GetModeDisplayName(mappingMode);
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(830, 350);

            rowsPanel = new Panel
            {
                AutoScroll = true,
                Location = new Point(18, 54),
                Size = new Size(794, 230),
                BorderStyle = BorderStyle.FixedSingle
            };
            addButton = new Button
            {
                Location = new Point(758, 12),
                Size = new Size(54, 34),
                Text = "+"
            };
            addButton.Click += AddButton_Click;

            Button okButton = new Button
            {
                Location = new Point(626, 300),
                Size = new Size(86, 36),
                Text = "确定"
            };
            okButton.Click += OkButton_Click;
            Button cancelButton = new Button
            {
                Location = new Point(726, 300),
                Size = new Size(86, 36),
                Text = "取消"
            };
            cancelButton.Click += (sender, args) => Close();

            Controls.AddRange(new Control[] { rowsPanel, addButton, okButton, cancelButton });
            AcceptButton = okButton;

            bool requiresPeakRanges = RequiresPeakRanges(mappingMode);
            rowsPanel.Visible = requiresPeakRanges;
            addButton.Visible = requiresPeakRanges;
            if (requiresPeakRanges)
            {
                Label header = new Label
                {
                    AutoSize = true,
                    Location = new Point(18, 19),
                    Text = "设置目标波峰参数（cm⁻¹）"
                };
                Controls.Add(header);

                if (existingRanges != null && existingRanges.Count > 0)
                {
                    for (int index = 0; index < existingRanges.Count; index++)
                        AddPeakRangeRow(existingRanges[index].RangeStart,
                            existingRanges[index].RangeEnd, existingRanges[index].Color,
                            existingRanges[index].Metric);
                }
                else
                {
                    AddPeakRangeRow(500.0, 540.0, peakColorChoices[0].Color,
                        mappingMode == RamanMappingMode.PeakArea
                            ? RamanPeakMetric.Area
                            : RamanPeakMetric.Height);
                }
            }
        }

        internal IList<RamanPeakRange> PeakRanges
        {
            get { return peakRanges.AsReadOnly(); }
        }

        internal static bool RequiresPeakRanges(RamanMappingMode mode)
        {
            return mode == RamanMappingMode.PeakHeight
                || mode == RamanMappingMode.PeakArea
                || mode == RamanMappingMode.PeakPosition
                || mode == RamanMappingMode.PeakWidth;
        }

        internal static string GetModeDisplayName(RamanMappingMode mode)
        {
            switch (mode)
            {
                case RamanMappingMode.PeakHeight: return "峰高峰面积";
                case RamanMappingMode.PeakArea: return "峰面积";
                case RamanMappingMode.PeakPosition: return "峰位置";
                case RamanMappingMode.PeakWidth: return "半高宽 FWHM";
                case RamanMappingMode.FullSpectrumDifference: return "全谱差异（荧光）";
                case RamanMappingMode.Pca: return "PCA 全谱异常";
                default: return "Mapping 设置";
            }
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            double start = rows.Count > 0 ? (double)rows[rows.Count - 1].StartInput.Value : 500.0;
            double end = rows.Count > 0 ? (double)rows[rows.Count - 1].EndInput.Value : 540.0;
            if (mappingMode == RamanMappingMode.PeakPosition
                || mappingMode == RamanMappingMode.PeakWidth)
                end = start;
            Color nextColor;
            if (!TryGetFirstAvailableColor(out nextColor))
            {
                MessageBox.Show(this, "每个波峰必须使用不同颜色，最多可设置 9 个波峰。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RamanPeakMetric nextMetric = (mappingMode == RamanMappingMode.PeakPosition
                || mappingMode == RamanMappingMode.PeakWidth)
                ? RamanPeakMetric.Height
                : rows.Count > 0
                && rows[rows.Count - 1].MetricInput.SelectedIndex == 1
                    ? RamanPeakMetric.Area
                    : RamanPeakMetric.Height;
            AddPeakRangeRow(start, end, nextColor, nextMetric);
        }

        private void AddPeakRangeRow(
            double suggestedStart,
            double suggestedEnd,
            Color suggestedColor,
            RamanPeakMetric suggestedMetric)
        {
            // 峰位置和半高宽与峰高一样只接收一个目标位置；即使从峰面积模式
            // 切换过来，也不能沿用之前隐藏的范围输入和面积计算方式。
            if (mappingMode == RamanMappingMode.PeakPosition
                || mappingMode == RamanMappingMode.PeakWidth)
                suggestedMetric = RamanPeakMetric.Height;

            PeakRangeRow row = new PeakRangeRow
            {
                NameLabel = new Label
                {
                    AutoSize = true,
                    Location = new Point(12, 0)
                },
                StartInput = new NumericUpDown
                {
                    DecimalPlaces = 1,
                    Increment = 1M,
                    Minimum = -500M,
                    Maximum = 3500M,
                    Location = new Point(105, 0),
                    Size = new Size(150, 30),
                    Value = ClampDecimal(
                        suggestedMetric == RamanPeakMetric.Height
                            && suggestedEnd > suggestedStart
                                ? (suggestedStart + suggestedEnd) * 0.5
                                : suggestedStart,
                        -500M,
                        3500M)
                },
                EndInput = new NumericUpDown
                {
                    DecimalPlaces = 1,
                    Increment = 1M,
                    Minimum = -500M,
                    Maximum = 3500M,
                    Location = new Point(330, 0),
                    Size = new Size(150, 30),
                    Value = ClampDecimal(suggestedEnd, -500M, 3500M)
                },
                MetricInput = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(490, 0),
                    Size = new Size(92, 30)
                },
                ColorButton = new Button
                {
                    Location = new Point(590, 0),
                    Size = new Size(30, 30),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    AccessibleName = "选择颜色",
                    UseVisualStyleBackColor = false
                },
                DeleteButton = new Button
                {
                    Location = new Point(630, 0),
                    Size = new Size(54, 30),
                    Text = "删除"
                }
            };
            row.Separator = new Label
            {
                AutoSize = true,
                Location = new Point(278, 0),
                Text = "至"
            };
            rows.Add(row);
            row.MetricInput.Items.AddRange(new object[] { "峰高", "峰面积" });
            row.MetricInput.Visible = mappingMode == RamanMappingMode.PeakHeight
                || mappingMode == RamanMappingMode.PeakArea;
            SelectColor(row, suggestedColor);
            row.MetricInput.SelectedIndex = suggestedMetric == RamanPeakMetric.Area ? 1 : 0;
            UpdateMetricLayout(row, false);
            row.MetricInput.SelectedIndexChanged += MetricInput_SelectedIndexChanged;
            row.ColorButton.Click += ColorButton_Click;
            row.DeleteButton.Click += DeleteButton_Click;
            rowsPanel.Controls.AddRange(new Control[]
            {
                row.NameLabel, row.StartInput, row.Separator, row.EndInput,
                row.MetricInput, row.ColorButton, row.DeleteButton
            });
            LayoutRows();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            PeakRangeRow row = rows.Find(item => item.DeleteButton == sender);
            if (row == null || rows.Count <= 1)
                return;

            rows.Remove(row);
            foreach (Control control in new Control[]
            {
                row.NameLabel, row.StartInput, row.Separator, row.EndInput,
                row.MetricInput, row.ColorButton, row.DeleteButton
            })
            {
                rowsPanel.Controls.Remove(control);
                control.Dispose();
            }
            LayoutRows();
        }

        private void LayoutRows()
        {
            for (int index = 0; index < rows.Count; index++)
            {
                PeakRangeRow row = rows[index];
                int top = 12 + index * 46;
                bool useArea = row.MetricInput.SelectedIndex == 1;
                row.NameLabel.Text = "波峰" + (index + 1)
                    + (useArea ? " 范围" : " 位置");
                row.NameLabel.Location = new Point(12, top + 7);
                row.StartInput.Location = new Point(105, top);
                row.Separator.Location = new Point(278, top + 7);
                row.EndInput.Location = new Point(330, top);
                row.MetricInput.Location = new Point(490, top);
                row.ColorButton.Location = new Point(590, top);
                row.DeleteButton.Location = new Point(630, top);
                row.DeleteButton.Enabled = rows.Count > 1;
            }
            rowsPanel.AutoScrollMinSize = new Size(0, 12 + rows.Count * 46 + 4);
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            peakRanges.Clear();
            if (RequiresPeakRanges(mappingMode))
            {
                for (int index = 0; index < rows.Count; index++)
                {
                    double start = (double)rows[index].StartInput.Value;
                    RamanPeakMetric metric = (mappingMode == RamanMappingMode.PeakPosition
                        || mappingMode == RamanMappingMode.PeakWidth)
                        ? RamanPeakMetric.Height
                        : rows[index].MetricInput.SelectedIndex == 1
                        ? RamanPeakMetric.Area
                        : RamanPeakMetric.Height;
                    double end = metric == RamanPeakMetric.Area
                        ? (double)rows[index].EndInput.Value
                        : start;
                    if (metric == RamanPeakMetric.Area && end <= start)
                    {
                        MessageBox.Show(this,
                            "波峰" + (index + 1) + "的终点必须大于起点。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (metric == RamanPeakMetric.Area && end - start < 20.0)
                    {
                        MessageBox.Show(this,
                            "波峰" + (index + 1) + "的范围至少需要 20 cm⁻¹。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    PeakColorChoice choice = rows[index].SelectedColor;
                    if (choice == null)
                    {
                        MessageBox.Show(this, "请为波峰" + (index + 1) + "选择颜色。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    for (int previous = 0; previous < index; previous++)
                    {
                        PeakColorChoice previousChoice = rows[previous].SelectedColor;
                        if (previousChoice != null && previousChoice.Color.ToArgb() == choice.Color.ToArgb())
                        {
                            MessageBox.Show(this, "每个波峰必须选择不同颜色。",
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    peakRanges.Add(new RamanPeakRange(start, end, choice.Color, metric));
                }
            }
            Accepted = true;
            Close();
        }

        private void MetricInput_SelectedIndexChanged(object sender, EventArgs e)
        {
            PeakRangeRow row = rows.Find(item => item.MetricInput == sender);
            if (row == null)
                return;
            UpdateMetricLayout(row, true);
            LayoutRows();
        }

        private static void UpdateMetricLayout(PeakRangeRow row, bool convertValues)
        {
            bool useArea = row.MetricInput.SelectedIndex == 1;
            if (convertValues)
            {
                if (useArea)
                {
                    decimal center = row.StartInput.Value;
                    row.StartInput.Value = Math.Max(row.StartInput.Minimum, center - 20M);
                    row.EndInput.Value = Math.Min(row.EndInput.Maximum, center + 20M);
                }
                else
                {
                    row.StartInput.Value = (row.StartInput.Value + row.EndInput.Value) / 2M;
                }
            }
            row.Separator.Visible = useArea;
            row.EndInput.Visible = useArea;
            row.StartInput.Size = useArea ? new Size(150, 30) : new Size(375, 30);
            row.StartInput.AccessibleName = useArea ? "范围起点" : "波峰位置";
        }

        private static decimal ClampDecimal(double value, decimal minimum, decimal maximum)
        {
            decimal converted;
            try { converted = Convert.ToDecimal(value); }
            catch { converted = 0M; }
            return Math.Max(minimum, Math.Min(maximum, converted));
        }

        private void ColorButton_Click(object sender, EventArgs e)
        {
            PeakRangeRow row = rows.Find(item => item.ColorButton == sender);
            if (row == null)
                return;
            ShowColorPalette(row);
        }

        private void ShowColorPalette(PeakRangeRow row)
        {
            const int cellSize = 44;
            var palette = new TableLayoutPanel
            {
                BackColor = Color.White,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(6),
                Margin = Padding.Empty,
                Size = new Size(cellSize * 3 + 12, cellSize * 3 + 12)
            };
            for (int index = 0; index < 3; index++)
            {
                palette.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, cellSize));
                palette.RowStyles.Add(new RowStyle(SizeType.Absolute, cellSize));
            }

            var dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                Padding = Padding.Empty,
                DropShadowEnabled = true
            };
            for (int index = 0; index < peakColorChoices.Count; index++)
            {
                PeakColorChoice choice = peakColorChoices[index];
                bool selected = row.SelectedColor != null
                    && row.SelectedColor.Color.ToArgb() == choice.Color.ToArgb();
                var colorButton = new Button
                {
                    BackColor = choice.Color,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(3),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    AccessibleName = choice.Name,
                    UseVisualStyleBackColor = false
                };
                colorButton.FlatAppearance.BorderColor = selected
                    ? Color.FromArgb(35, 35, 35)
                    : Color.FromArgb(220, 220, 220);
                colorButton.FlatAppearance.BorderSize = selected ? 3 : 1;
                colorButton.FlatAppearance.MouseOverBackColor = choice.Color;
                colorButton.Click += delegate
                {
                    ApplyColorChoice(row, choice);
                };
                palette.Controls.Add(colorButton, index % 3, index / 3);
            }

            var host = new ToolStripControlHost(palette)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = palette.Size
            };
            dropDown.Items.Add(host);
            dropDown.Closed += delegate
            {
                // Closed 事件触发时 WinForms 仍在执行 ToolStripDropDown 的内部关闭流程，
                // 不能在这里同步 Dispose；投递到下一轮 UI 消息后再安全释放。
                if (IsDisposed || !IsHandleCreated)
                    return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!dropDown.IsDisposed)
                            dropDown.Dispose();
                    });
                }
                catch (InvalidOperationException)
                {
                    // 设置窗口正在关闭时，控件树会统一释放色盘。
                }
            };
            dropDown.Show(row.ColorButton, new Point(0, row.ColorButton.Height));
        }

        private void ApplyColorChoice(PeakRangeRow row, PeakColorChoice choice)
        {
            if (row.SelectedColor == choice)
                return;

            PeakRangeRow conflictingRow = rows.Find(item =>
                item != row
                && item.SelectedColor != null
                && item.SelectedColor.Color.ToArgb() == choice.Color.ToArgb());
            PeakColorChoice previousChoice = row.SelectedColor;
            row.SelectedColor = choice;
            UpdateColorButton(row);

            // 颜色必须保持唯一；点击已使用颜色时与对应波峰交换，保证所点即所得。
            if (conflictingRow != null)
            {
                conflictingRow.SelectedColor = previousChoice;
                UpdateColorButton(conflictingRow);
            }
        }

        private bool TryGetFirstAvailableColor(out Color color, PeakRangeRow ignoredRow = null)
        {
            for (int colorIndex = 0; colorIndex < peakColorChoices.Count; colorIndex++)
            {
                Color candidate = peakColorChoices[colorIndex].Color;
                bool used = false;
                foreach (PeakRangeRow row in rows)
                {
                    if (row == ignoredRow)
                        continue;
                    PeakColorChoice choice = row.SelectedColor;
                    if (choice != null && choice.Color.ToArgb() == candidate.ToArgb())
                    {
                        used = true;
                        break;
                    }
                }
                if (!used)
                {
                    color = candidate;
                    return true;
                }
            }
            color = Color.Empty;
            return false;
        }

        private void SelectColor(PeakRangeRow row, Color color)
        {
            for (int index = 0; index < peakColorChoices.Count; index++)
            {
                if (peakColorChoices[index].Color.ToArgb() == color.ToArgb())
                {
                    row.SelectedColor = peakColorChoices[index];
                    UpdateColorButton(row);
                    return;
                }
            }
            row.SelectedColor = peakColorChoices[0];
            UpdateColorButton(row);
        }

        private static void UpdateColorButton(PeakRangeRow row)
        {
            PeakColorChoice choice = row.SelectedColor;
            row.ColorButton.BackColor = choice == null ? Color.Black : choice.Color;
            row.ColorButton.AccessibleDescription = choice == null ? "未选择" : choice.Name;
            row.ColorButton.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
            row.ColorButton.FlatAppearance.BorderSize = 1;
        }
    }
}
