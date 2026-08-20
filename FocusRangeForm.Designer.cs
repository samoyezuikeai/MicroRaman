namespace MicroRaman
{
    partial class FocusRangeForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.instructionLabel = new System.Windows.Forms.Label();
            this.zUnitValueLabel = new System.Windows.Forms.Label();
            this.negativeDistanceLabel = new System.Windows.Forms.Label();
            this.positiveDistanceLabel = new System.Windows.Forms.Label();
            this.negativeDistanceTextBox = new System.Windows.Forms.TextBox();
            this.positiveDistanceTextBox = new System.Windows.Forms.TextBox();
            this.noteLabel = new System.Windows.Forms.Label();
            this.startButton = new System.Windows.Forms.Button();
            this.cancelActionButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // instructionLabel
            // 
            this.instructionLabel.AutoSize = true;
            this.instructionLabel.Location = new System.Drawing.Point(18, 18);
            this.instructionLabel.Name = "instructionLabel";
            this.instructionLabel.Size = new System.Drawing.Size(395, 54);
            this.instructionLabel.TabIndex = 0;
            this.instructionLabel.Text = "请先确认明场照明已打开、当前位置已经人工对焦清楚。\r\n请输入每个点到位后，允许 Z 轴相对当前位置移动的最大距离。\r\n例如填写 8 和 2，就是最多向下 8 个单位、向上 2 个单位。";
            // 
            // zUnitValueLabel
            // 
            this.zUnitValueLabel.AutoSize = true;
            this.zUnitValueLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.zUnitValueLabel.Location = new System.Drawing.Point(18, 88);
            this.zUnitValueLabel.Name = "zUnitValueLabel";
            this.zUnitValueLabel.Size = new System.Drawing.Size(142, 17);
            this.zUnitValueLabel.TabIndex = 1;
            this.zUnitValueLabel.Text = "?dim z = 1，输入单位：μm";
            // 
            // negativeDistanceLabel
            // 
            this.negativeDistanceLabel.AutoSize = true;
            this.negativeDistanceLabel.Location = new System.Drawing.Point(18, 127);
            this.negativeDistanceLabel.Name = "negativeDistanceLabel";
            this.negativeDistanceLabel.Size = new System.Drawing.Size(92, 17);
            this.negativeDistanceLabel.TabIndex = 2;
            this.negativeDistanceLabel.Text = "向下最大距离：";
            // 
            // positiveDistanceLabel
            // 
            this.positiveDistanceLabel.AutoSize = true;
            this.positiveDistanceLabel.Location = new System.Drawing.Point(18, 165);
            this.positiveDistanceLabel.Name = "positiveDistanceLabel";
            this.positiveDistanceLabel.Size = new System.Drawing.Size(92, 17);
            this.positiveDistanceLabel.TabIndex = 4;
            this.positiveDistanceLabel.Text = "向上最大距离：";
            // 
            // negativeDistanceTextBox
            // 
            this.negativeDistanceTextBox.Location = new System.Drawing.Point(126, 124);
            this.negativeDistanceTextBox.Name = "negativeDistanceTextBox";
            this.negativeDistanceTextBox.Size = new System.Drawing.Size(130, 23);
            this.negativeDistanceTextBox.TabIndex = 3;
            // 
            // positiveDistanceTextBox
            // 
            this.positiveDistanceTextBox.Location = new System.Drawing.Point(126, 162);
            this.positiveDistanceTextBox.Name = "positiveDistanceTextBox";
            this.positiveDistanceTextBox.Size = new System.Drawing.Size(130, 23);
            this.positiveDistanceTextBox.TabIndex = 5;
            // 
            // noteLabel
            // 
            this.noteLabel.AutoSize = true;
            this.noteLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.noteLabel.Location = new System.Drawing.Point(18, 205);
            this.noteLabel.Name = "noteLabel";
            this.noteLabel.Size = new System.Drawing.Size(427, 36);
            this.noteLabel.TabIndex = 6;
            this.noteLabel.Text = "只填正数，不填绝对 Z 坐标。达到某一方向上限仍未找到可靠焦点时，\r\n程序会中断本次计算，并返回点击按钮前的 XYZ 位置。";
            // 
            // startButton
            // 
            this.startButton.Location = new System.Drawing.Point(275, 265);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(84, 30);
            this.startButton.TabIndex = 7;
            this.startButton.Text = "开始计算";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.startButton_Click);
            // 
            // cancelActionButton
            // 
            this.cancelActionButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelActionButton.Location = new System.Drawing.Point(369, 265);
            this.cancelActionButton.Name = "cancelActionButton";
            this.cancelActionButton.Size = new System.Drawing.Size(84, 30);
            this.cancelActionButton.TabIndex = 8;
            this.cancelActionButton.Text = "取消";
            this.cancelActionButton.UseVisualStyleBackColor = true;
            // 
            // FocusRangeForm
            // 
            this.AcceptButton = this.startButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelActionButton;
            this.ClientSize = new System.Drawing.Size(471, 313);
            this.Controls.Add(this.cancelActionButton);
            this.Controls.Add(this.startButton);
            this.Controls.Add(this.noteLabel);
            this.Controls.Add(this.positiveDistanceTextBox);
            this.Controls.Add(this.negativeDistanceTextBox);
            this.Controls.Add(this.positiveDistanceLabel);
            this.Controls.Add(this.negativeDistanceLabel);
            this.Controls.Add(this.zUnitValueLabel);
            this.Controls.Add(this.instructionLabel);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FocusRangeForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "设置焦点搜索范围";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label instructionLabel;
        private System.Windows.Forms.Label zUnitValueLabel;
        private System.Windows.Forms.Label negativeDistanceLabel;
        private System.Windows.Forms.Label positiveDistanceLabel;
        private System.Windows.Forms.TextBox negativeDistanceTextBox;
        private System.Windows.Forms.TextBox positiveDistanceTextBox;
        private System.Windows.Forms.Label noteLabel;
        private System.Windows.Forms.Button startButton;
        private System.Windows.Forms.Button cancelActionButton;
    }
}
