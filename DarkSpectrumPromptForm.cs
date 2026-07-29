using System.Drawing;
using System.Windows.Forms;

namespace MicroLaman
{
    /// <summary>实时光谱启动前的暗谱采集提示，仅保留明确的采集操作。</summary>
    internal sealed class DarkSpectrumPromptForm : Form
    {
        internal DarkSpectrumPromptForm()
        {
            Text = "实时光谱";
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(520, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            Label message = new Label
            {
                AutoSize = false,
                Location = new Point(26, 25),
                Size = new Size(468, 72),
                Font = new Font("Microsoft YaHei UI", 10F),
                Text = "请确认已关闭照明灯后，点击下方的“采集暗谱”按钮。\r\n采集完成后会自动打开 TEC 和激光器，再显示实时光谱。",
                TextAlign = ContentAlignment.MiddleLeft
            };

            Button captureButton = new Button
            {
                Text = "采集暗谱",
                Font = new Font("Microsoft YaHei UI", 10F),
                Location = new Point(174, 118),
                Size = new Size(172, 46),
                DialogResult = DialogResult.OK
            };

            Controls.Add(message);
            Controls.Add(captureButton);
            AcceptButton = captureButton;
        }
    }
}
