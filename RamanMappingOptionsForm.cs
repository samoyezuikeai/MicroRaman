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

    internal sealed class RamanPeakRange
    {
        internal RamanPeakRange(double rangeStart, double rangeEnd)
            : this(rangeStart, rangeEnd, Color.Empty)
        {
        }

        internal RamanPeakRange(double rangeStart, double rangeEnd, Color color)
        {
            RangeStart = rangeStart;
            RangeEnd = rangeEnd;
            Color = color;
        }

        internal double RangeStart { get; private set; }
        internal double RangeEnd { get; private set; }
        internal Color Color { get; private set; }
    }

    /// <summary>
    /// Configures one or more target Raman peak ranges for the selected mapping mode.
    /// Each saved row becomes one independently coloured mapping channel.
    /// </summary>
    internal sealed class RamanMappingOptionsForm : Form
    {
        private sealed class PeakRangeRow
        {
            internal Label NameLabel;
            internal NumericUpDown StartInput;
            internal NumericUpDown EndInput;
            internal ComboBox ColorInput;
            internal Panel ColorPreview;
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
            new PeakColorChoice("蓝色", Color.FromArgb(30, 144, 255)),
            new PeakColorChoice("青色", Color.FromArgb(0, 200, 220)),
            new PeakColorChoice("绿色", Color.FromArgb(45, 180, 95)),
            new PeakColorChoice("黄色", Color.FromArgb(255, 215, 0)),
            new PeakColorChoice("橙色", Color.FromArgb(255, 128, 0)),
            new PeakColorChoice("红色", Color.FromArgb(220, 45, 40))
        };
        private bool changingColors;

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
                DialogResult = DialogResult.Cancel,
                Location = new Point(726, 300),
                Size = new Size(86, 36),
                Text = "取消"
            };

            Controls.AddRange(new Control[] { rowsPanel, addButton, okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;

            bool requiresPeakRanges = RequiresPeakRanges(mappingMode);
            rowsPanel.Visible = requiresPeakRanges;
            addButton.Visible = requiresPeakRanges;
            if (requiresPeakRanges)
            {
                Label header = new Label
                {
                    AutoSize = true,
                    Location = new Point(18, 19),
                    Text = "设置目标波峰范围（cm⁻¹）"
                };
                Controls.Add(header);

                if (existingRanges != null && existingRanges.Count > 0)
                {
                    for (int index = 0; index < existingRanges.Count; index++)
                        AddPeakRangeRow(existingRanges[index].RangeStart,
                            existingRanges[index].RangeEnd, existingRanges[index].Color);
                }
                else
                {
                    AddPeakRangeRow(500.0, 540.0, peakColorChoices[0].Color);
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
                case RamanMappingMode.PeakHeight: return "峰高（强度）";
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
            Color nextColor;
            if (!TryGetFirstAvailableColor(out nextColor))
            {
                MessageBox.Show(this, "每个波峰必须使用不同颜色，最多可设置 6 个波峰。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddPeakRangeRow(start, end, nextColor);
        }

        private void AddPeakRangeRow(double suggestedStart, double suggestedEnd, Color suggestedColor)
        {
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
                    Value = ClampDecimal(suggestedStart, -500M, 3500M)
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
                ColorInput = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(510, 0),
                    Size = new Size(118, 30)
                },
                ColorPreview = new Panel
                {
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(642, 0),
                    Size = new Size(48, 24)
                },
                DeleteButton = new Button
                {
                    Location = new Point(704, 0),
                    Size = new Size(70, 30),
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
            for (int index = 0; index < peakColorChoices.Count; index++)
                row.ColorInput.Items.Add(peakColorChoices[index]);
            SelectColor(row, suggestedColor);
            row.ColorInput.SelectedIndexChanged += ColorInput_SelectedIndexChanged;
            row.DeleteButton.Click += DeleteButton_Click;
            rowsPanel.Controls.AddRange(new Control[]
            {
                row.NameLabel, row.StartInput, row.Separator, row.EndInput,
                row.ColorInput, row.ColorPreview, row.DeleteButton
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
                row.ColorInput, row.ColorPreview, row.DeleteButton
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
                row.NameLabel.Text = "波峰" + (index + 1);
                row.NameLabel.Location = new Point(12, top + 7);
                row.StartInput.Location = new Point(105, top);
                row.Separator.Location = new Point(278, top + 7);
                row.EndInput.Location = new Point(330, top);
                row.ColorInput.Location = new Point(510, top);
                row.ColorPreview.Location = new Point(642, top + 3);
                row.DeleteButton.Location = new Point(704, top);
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
                    double end = (double)rows[index].EndInput.Value;
                    if (end <= start)
                    {
                        MessageBox.Show(this,
                            "波峰" + (index + 1) + "的终点必须大于起点。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (end - start < 2.0)
                    {
                        MessageBox.Show(this,
                            "波峰" + (index + 1) + "的范围至少需要 2 cm⁻¹。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    PeakColorChoice choice = rows[index].ColorInput.SelectedItem as PeakColorChoice;
                    if (choice == null)
                    {
                        MessageBox.Show(this, "请为波峰" + (index + 1) + "选择颜色。",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    for (int previous = 0; previous < index; previous++)
                    {
                        PeakColorChoice previousChoice = rows[previous].ColorInput.SelectedItem as PeakColorChoice;
                        if (previousChoice != null && previousChoice.Color.ToArgb() == choice.Color.ToArgb())
                        {
                            MessageBox.Show(this, "每个波峰必须选择不同颜色。",
                                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }
                    }
                    peakRanges.Add(new RamanPeakRange(start, end, choice.Color));
                }
            }
            DialogResult = DialogResult.OK;
        }

        private static decimal ClampDecimal(double value, decimal minimum, decimal maximum)
        {
            decimal converted;
            try { converted = Convert.ToDecimal(value); }
            catch { converted = 0M; }
            return Math.Max(minimum, Math.Min(maximum, converted));
        }

        private void ColorInput_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (changingColors)
                return;

            PeakRangeRow changedRow = rows.Find(row => row.ColorInput == sender);
            if (changedRow == null)
                return;
            PeakColorChoice changedChoice = changedRow.ColorInput.SelectedItem as PeakColorChoice;
            if (changedChoice == null)
                return;

            foreach (PeakRangeRow row in rows)
            {
                if (row == changedRow)
                    continue;
                PeakColorChoice otherChoice = row.ColorInput.SelectedItem as PeakColorChoice;
                if (otherChoice == null || otherChoice.Color.ToArgb() != changedChoice.Color.ToArgb())
                    continue;

                Color replacement;
                if (TryGetFirstAvailableColor(out replacement, changedRow))
                {
                    changingColors = true;
                    SelectColor(changedRow, replacement);
                    changingColors = false;
                }
                else
                {
                    MessageBox.Show(this, "每个波峰必须选择不同颜色。", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }
            UpdateColorPreview(changedRow);
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
                    PeakColorChoice choice = row.ColorInput.SelectedItem as PeakColorChoice;
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
                    row.ColorInput.SelectedIndex = index;
                    UpdateColorPreview(row);
                    return;
                }
            }
            row.ColorInput.SelectedIndex = 0;
            UpdateColorPreview(row);
        }

        private static void UpdateColorPreview(PeakRangeRow row)
        {
            PeakColorChoice choice = row.ColorInput.SelectedItem as PeakColorChoice;
            row.ColorPreview.BackColor = choice == null ? Color.Black : choice.Color;
        }
    }
}
