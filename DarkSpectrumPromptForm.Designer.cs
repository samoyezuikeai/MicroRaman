namespace MicroRaman
{
    partial class DarkSpectrumPromptForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Panel contentPanel;
        private System.Windows.Forms.Label promptLabel;
        private System.Windows.Forms.Button captureDarkSpectrumButton;

        /// <summary>
        /// 释放正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.contentPanel = new System.Windows.Forms.Panel();
            this.captureDarkSpectrumButton = new System.Windows.Forms.Button();
            this.promptLabel = new System.Windows.Forms.Label();
            this.contentPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // contentPanel
            // 
            this.contentPanel.Controls.Add(this.captureDarkSpectrumButton);
            this.contentPanel.Controls.Add(this.promptLabel);
            this.contentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentPanel.Location = new System.Drawing.Point(0, 0);
            this.contentPanel.Name = "contentPanel";
            this.contentPanel.Padding = new System.Windows.Forms.Padding(26, 25, 26, 26);
            this.contentPanel.Size = new System.Drawing.Size(578, 238);
            this.contentPanel.TabIndex = 0;
            // 
            // captureDarkSpectrumButton
            // 
            this.captureDarkSpectrumButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.captureDarkSpectrumButton.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.captureDarkSpectrumButton.Location = new System.Drawing.Point(174, 134);
            this.captureDarkSpectrumButton.Name = "captureDarkSpectrumButton";
            this.captureDarkSpectrumButton.Size = new System.Drawing.Size(172, 46);
            this.captureDarkSpectrumButton.TabIndex = 1;
            this.captureDarkSpectrumButton.Text = "采集暗谱";
            this.captureDarkSpectrumButton.UseVisualStyleBackColor = true;
            // 
            // promptLabel
            // 
            this.promptLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.promptLabel.Location = new System.Drawing.Point(26, 25);
            this.promptLabel.Name = "promptLabel";
            this.promptLabel.Size = new System.Drawing.Size(468, 72);
            this.promptLabel.TabIndex = 0;
            this.promptLabel.Text = "请确认已关闭照明灯后，点击下方的“采集暗谱”按钮。\r\n采集完成后会自动打开 TEC 和激光器，再显示实时光谱。";
            this.promptLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // DarkSpectrumPromptForm
            // 
            this.AcceptButton = this.captureDarkSpectrumButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(578, 238);
            this.Controls.Add(this.contentPanel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DarkSpectrumPromptForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "实时光谱";
            this.contentPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }
    }
}

