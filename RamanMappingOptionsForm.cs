using System;
using System.Drawing;
using System.Windows.Forms;

namespace MicroLaman
{
    internal enum RamanMappingMode
    {
        Automatic,
        PeakHeight,
        PeakArea,
        PeakPosition,
        PeakWidth,
        FullSpectrumDifference,
        Pca
    }

    /// <summary>LabSpec 风格的单变量峰面积 Mapping 参数窗口。</summary>
    internal sealed class RamanMappingOptionsForm : Form
    {
        private readonly NumericUpDown targetShiftInput;
        private readonly NumericUpDown halfWidthInput;
        private readonly CheckBox siliconNormalizationCheckBox;

        internal RamanMappingOptionsForm(double suggestedTargetShift, RamanMappingMode mappingMode)
        {
            Text = "拉曼 Mapping 参数";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(470, 245);

            Label description = new Label
            {
                AutoSize = false,
                Location = new Point(18, 14),
                Size = new Size(435, 48),
                Text = "设置需要分析的目标拉曼峰。当前指标：" + GetModeDisplayName(mappingMode) + "。"
            };
            Label targetLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 76),
                Text = "目标峰中心 (cm⁻¹)："
            };
            targetShiftInput = new NumericUpDown
            {
                DecimalPlaces = 1,
                Increment = 1M,
                Minimum = 100M,
                Maximum = 3200M,
                Location = new Point(215, 72),
                Size = new Size(130, 30),
                Value = ClampDecimal(suggestedTargetShift, 100M, 3200M)
            };
            Label widthLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 117),
                Text = "积分半宽 (cm⁻¹)："
            };
            halfWidthInput = new NumericUpDown
            {
                DecimalPlaces = 1,
                Increment = 1M,
                Minimum = 5M,
                Maximum = 100M,
                Location = new Point(215, 113),
                Size = new Size(130, 30),
                Value = 20M
            };
            siliconNormalizationCheckBox = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Enabled = mappingMode == RamanMappingMode.PeakHeight
                    || mappingMode == RamanMappingMode.PeakArea,
                Location = new Point(21, 157),
                Text = "使用软件自动识别的稳定参考峰归一化"
            };
            Button okButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(270, 198),
                Size = new Size(82, 34),
                Text = "生成"
            };
            Button cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(365, 198),
                Size = new Size(82, 34),
                Text = "取消"
            };

            Controls.AddRange(new Control[]
            {
                description, targetLabel, targetShiftInput, widthLabel, halfWidthInput,
                siliconNormalizationCheckBox, okButton, cancelButton
            });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        internal double TargetShift { get { return (double)targetShiftInput.Value; } }
        internal double HalfWidth { get { return (double)halfWidthInput.Value; } }
        internal bool NormalizeToSilicon { get { return siliconNormalizationCheckBox.Checked; } }

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
        private static decimal ClampDecimal(double value, decimal minimum, decimal maximum)
        {
            decimal converted;
            try { converted = Convert.ToDecimal(value); }
            catch { converted = 960M; }
            return Math.Max(minimum, Math.Min(maximum, converted));
        }
    }
}
