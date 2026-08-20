using System;
using System.Globalization;
using System.Windows.Forms;

namespace MicroRaman
{
    public partial class FocusRangeForm : Form
    {
        internal FocusRangeForm(FocusSearchSetup setup)
        {
            InitializeComponent();
            zUnitValueLabel.Text = string.Format(
                "?dim z = {0}，输入单位：{1}",
                setup.ZDimension,
                setup.ZUnitDescription);
            negativeDistanceTextBox.Text = setup.DefaultNegativeDistance.ToString("G6", CultureInfo.CurrentCulture);
            positiveDistanceTextBox.Text = setup.DefaultPositiveDistance.ToString("G6", CultureInfo.CurrentCulture);
        }

        internal double MaximumNegativeTravel { get; private set; }

        internal double MaximumPositiveTravel { get; private set; }

        private void startButton_Click(object sender, EventArgs e)
        {
            double negativeDistance;
            double positiveDistance;
            if (!TryParsePositiveDistance(negativeDistanceTextBox.Text, out negativeDistance))
            {
                MessageBox.Show(this, "请在“向下最大距离”中填写大于 0 的数字。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                negativeDistanceTextBox.Focus();
                negativeDistanceTextBox.SelectAll();
                return;
            }
            if (!TryParsePositiveDistance(positiveDistanceTextBox.Text, out positiveDistance))
            {
                MessageBox.Show(this, "请在“向上最大距离”中填写大于 0 的数字。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                positiveDistanceTextBox.Focus();
                positiveDistanceTextBox.SelectAll();
                return;
            }

            MaximumNegativeTravel = negativeDistance;
            MaximumPositiveTravel = positiveDistance;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool TryParsePositiveDistance(string text, out double value)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;

            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }
    }
}
