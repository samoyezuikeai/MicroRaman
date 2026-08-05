using System;
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

    /// <summary>
    /// 收集峰高或峰面积 Mapping 的目标拉曼位移范围。
    /// 系统只在用户指定的范围内寻找峰并计算指标。
    /// </summary>
    internal sealed class RamanMappingOptionsForm : Form
    {
        private readonly NumericUpDown rangeStartInput;
        private readonly NumericUpDown rangeEndInput;

        /// <summary>
        /// 初始化目标峰范围输入窗口。
        /// 峰高和峰面积将只使用该闭区间中的最大峰。
        /// </summary>
        internal RamanMappingOptionsForm(
            RamanMappingMode mappingMode,
            double suggestedStart,
            double suggestedEnd)
        {
            Text = "拉曼 Mapping 参数";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(490, 215);

            Label description = new Label
            {
                AutoSize = false,
                Location = new Point(18, 14),
                Size = new Size(450, 48),
                Text = "填写目标峰所在的拉曼位移范围。当前指标："
                    + GetModeDisplayName(mappingMode)
                    + "。系统只在这个范围内寻找峰，不会使用整条光谱的最大峰。"
            };
            Label rangeStartLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 76),
                Text = "范围起点 (cm⁻¹)："
            };
            rangeStartInput = new NumericUpDown
            {
                DecimalPlaces = 1,
                Increment = 1M,
                Minimum = -500M,
                Maximum = 3500M,
                Location = new Point(220, 72),
                Size = new Size(130, 30),
                Value = ClampDecimal(suggestedStart, -500M, 3500M)
            };
            Label rangeEndLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 117),
                Text = "范围终点 (cm⁻¹)："
            };
            rangeEndInput = new NumericUpDown
            {
                DecimalPlaces = 1,
                Increment = 1M,
                Minimum = -500M,
                Maximum = 3500M,
                Location = new Point(220, 113),
                Size = new Size(130, 30),
                Value = ClampDecimal(suggestedEnd, -500M, 3500M)
            };
            Button okButton = new Button
            {
                Location = new Point(285, 164),
                Size = new Size(82, 34),
                Text = "生成"
            };
            okButton.Click += OkButton_Click;
            Button cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(382, 164),
                Size = new Size(82, 34),
                Text = "取消"
            };

            Controls.AddRange(new Control[]
            {
                description, rangeStartLabel, rangeStartInput, rangeEndLabel, rangeEndInput,
                okButton, cancelButton
            });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        internal double RangeStart { get { return (double)rangeStartInput.Value; } }
        internal double RangeEnd { get { return (double)rangeEndInput.Value; } }

        /// <summary>
        /// 验证用户给出的目标峰范围。
        /// 起点必须小于终点，且范围至少覆盖一个有效光谱采样点。
        /// </summary>
        private void OkButton_Click(object sender, EventArgs e)
        {
            if (RangeEnd <= RangeStart)
            {
                MessageBox.Show(this, "范围终点必须大于范围起点。", "拉曼 Mapping 参数",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (RangeEnd - RangeStart < 2.0)
            {
                MessageBox.Show(this, "拉曼位移范围过窄，请至少设置 2 cm⁻¹。", "拉曼 Mapping 参数",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
        }

        /// <summary>
        /// 获取ModeDisplayName相关的内部处理。
        /// </summary>
        private static string GetModeDisplayName(RamanMappingMode mode)
        {
            switch (mode)
            {
                case RamanMappingMode.PeakHeight: return "峰高（强度）";
                case RamanMappingMode.PeakPosition: return "峰位置";
                case RamanMappingMode.PeakWidth: return "半高宽 FWHM";
                default: return "峰面积";
            }
        }
        /// <summary>
        /// 限制Decimal相关的内部处理。
        /// </summary>
        private static decimal ClampDecimal(double value, decimal minimum, decimal maximum)
        {
            decimal converted;
            try { converted = Convert.ToDecimal(value); }
            catch { converted = 0M; }
            return Math.Max(minimum, Math.Min(maximum, converted));
        }
    }
}
