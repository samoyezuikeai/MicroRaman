namespace MicroLaman
{
    partial class CameraShowForm
    {
        private System.Windows.Forms.Panel controlPanel;
        private System.Windows.Forms.Label resolutionLabel;
        private System.Windows.Forms.ComboBox resolutionComboBox;
        private System.Windows.Forms.Label exposureLabel;
        private System.Windows.Forms.NumericUpDown exposureNumeric;
        private System.Windows.Forms.Label gainLabel;
        private System.Windows.Forms.NumericUpDown gainNumeric;
        private System.Windows.Forms.CheckBox autoExposureCheckBox;
        private System.Windows.Forms.Button applySettingsButton;
        private System.Windows.Forms.Label xPointCountLabel;
        private System.Windows.Forms.NumericUpDown xPointCountNumeric;
        private System.Windows.Forms.Label yPointCountLabel;
        private System.Windows.Forms.NumericUpDown yPointCountNumeric;
        private System.Windows.Forms.Label performanceLabel;
        private System.Windows.Forms.Panel previewPanel;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label stagePositionLabel;
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.controlPanel = new System.Windows.Forms.Panel();
            this.resolutionLabel = new System.Windows.Forms.Label();
            this.resolutionComboBox = new System.Windows.Forms.ComboBox();
            this.exposureLabel = new System.Windows.Forms.Label();
            this.exposureNumeric = new System.Windows.Forms.NumericUpDown();
            this.gainLabel = new System.Windows.Forms.Label();
            this.gainNumeric = new System.Windows.Forms.NumericUpDown();
            this.autoExposureCheckBox = new System.Windows.Forms.CheckBox();
            this.applySettingsButton = new System.Windows.Forms.Button();
            this.rectangleToolButton = new System.Windows.Forms.Button();
            this.xPointCountLabel = new System.Windows.Forms.Label();
            this.xPointCountNumeric = new System.Windows.Forms.NumericUpDown();
            this.yPointCountLabel = new System.Windows.Forms.Label();
            this.yPointCountNumeric = new System.Windows.Forms.NumericUpDown();
            this.performanceLabel = new System.Windows.Forms.Label();
            this.previewPanel = new System.Windows.Forms.Panel();
            this.stagePositionLabel = new System.Windows.Forms.Label();
            this.statusLabel = new System.Windows.Forms.Label();
            this.controlPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.exposureNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.gainNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.xPointCountNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.yPointCountNumeric)).BeginInit();
            this.previewPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // controlPanel
            // 
            this.controlPanel.BackColor = System.Drawing.Color.White;
            this.controlPanel.Controls.Add(this.resolutionLabel);
            this.controlPanel.Controls.Add(this.resolutionComboBox);
            this.controlPanel.Controls.Add(this.exposureLabel);
            this.controlPanel.Controls.Add(this.exposureNumeric);
            this.controlPanel.Controls.Add(this.gainLabel);
            this.controlPanel.Controls.Add(this.gainNumeric);
            this.controlPanel.Controls.Add(this.autoExposureCheckBox);
            this.controlPanel.Controls.Add(this.applySettingsButton);
            this.controlPanel.Controls.Add(this.rectangleToolButton);
            this.controlPanel.Controls.Add(this.xPointCountLabel);
            this.controlPanel.Controls.Add(this.xPointCountNumeric);
            this.controlPanel.Controls.Add(this.yPointCountLabel);
            this.controlPanel.Controls.Add(this.yPointCountNumeric);
            this.controlPanel.Controls.Add(this.performanceLabel);
            this.controlPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.controlPanel.Location = new System.Drawing.Point(0, 0);
            this.controlPanel.Margin = new System.Windows.Forms.Padding(6);
            this.controlPanel.Name = "controlPanel";
            this.controlPanel.Size = new System.Drawing.Size(1962, 92);
            this.controlPanel.TabIndex = 0;
            // 
            // resolutionLabel
            // 
            this.resolutionLabel.AutoSize = true;
            this.resolutionLabel.ForeColor = System.Drawing.Color.Black;
            this.resolutionLabel.Location = new System.Drawing.Point(20, 32);
            this.resolutionLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.resolutionLabel.Name = "resolutionLabel";
            this.resolutionLabel.Size = new System.Drawing.Size(82, 24);
            this.resolutionLabel.TabIndex = 0;
            this.resolutionLabel.Text = "分辨率";
            // 
            // resolutionComboBox
            // 
            this.resolutionComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.resolutionComboBox.FormattingEnabled = true;
            this.resolutionComboBox.Items.AddRange(new object[] {
            "高清 5472×3648",
            "均衡 2736×1824",
            "高速 1824×1216"});
            this.resolutionComboBox.Location = new System.Drawing.Point(114, 24);
            this.resolutionComboBox.Margin = new System.Windows.Forms.Padding(6);
            this.resolutionComboBox.Name = "resolutionComboBox";
            this.resolutionComboBox.Size = new System.Drawing.Size(266, 32);
            this.resolutionComboBox.TabIndex = 1;
            // 
            // exposureLabel
            // 
            this.exposureLabel.AutoSize = true;
            this.exposureLabel.ForeColor = System.Drawing.Color.Black;
            this.exposureLabel.Location = new System.Drawing.Point(410, 32);
            this.exposureLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.exposureLabel.Name = "exposureLabel";
            this.exposureLabel.Size = new System.Drawing.Size(106, 24);
            this.exposureLabel.TabIndex = 2;
            this.exposureLabel.Text = "曝光(ms)";
            // 
            // exposureNumeric
            // 
            this.exposureNumeric.DecimalPlaces = 1;
            this.exposureNumeric.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.exposureNumeric.Location = new System.Drawing.Point(534, 24);
            this.exposureNumeric.Margin = new System.Windows.Forms.Padding(6);
            this.exposureNumeric.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.exposureNumeric.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.exposureNumeric.Name = "exposureNumeric";
            this.exposureNumeric.Size = new System.Drawing.Size(136, 35);
            this.exposureNumeric.TabIndex = 3;
            this.exposureNumeric.Value = new decimal(new int[] {
            30,
            0,
            0,
            0});
            // 
            // gainLabel
            // 
            this.gainLabel.AutoSize = true;
            this.gainLabel.ForeColor = System.Drawing.Color.Black;
            this.gainLabel.Location = new System.Drawing.Point(696, 32);
            this.gainLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.gainLabel.Name = "gainLabel";
            this.gainLabel.Size = new System.Drawing.Size(58, 24);
            this.gainLabel.TabIndex = 4;
            this.gainLabel.Text = "增益";
            // 
            // gainNumeric
            // 
            this.gainNumeric.Location = new System.Drawing.Point(764, 24);
            this.gainNumeric.Margin = new System.Windows.Forms.Padding(6);
            this.gainNumeric.Maximum = new decimal(new int[] {
            240,
            0,
            0,
            0});
            this.gainNumeric.Name = "gainNumeric";
            this.gainNumeric.Size = new System.Drawing.Size(104, 35);
            this.gainNumeric.TabIndex = 5;
            this.gainNumeric.Value = new decimal(new int[] {
            80,
            0,
            0,
            0});
            // 
            // autoExposureCheckBox
            // 
            this.autoExposureCheckBox.AutoSize = true;
            this.autoExposureCheckBox.Checked = true;
            this.autoExposureCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoExposureCheckBox.ForeColor = System.Drawing.Color.Black;
            this.autoExposureCheckBox.Location = new System.Drawing.Point(890, 30);
            this.autoExposureCheckBox.Margin = new System.Windows.Forms.Padding(6);
            this.autoExposureCheckBox.Name = "autoExposureCheckBox";
            this.autoExposureCheckBox.Size = new System.Drawing.Size(138, 28);
            this.autoExposureCheckBox.TabIndex = 6;
            this.autoExposureCheckBox.Text = "自动曝光";
            this.autoExposureCheckBox.UseVisualStyleBackColor = true;
            this.autoExposureCheckBox.CheckedChanged += new System.EventHandler(this.AutoExposureCheckBox_CheckedChanged);
            // 
            // applySettingsButton
            // 
            this.applySettingsButton.Location = new System.Drawing.Point(1054, 20);
            this.applySettingsButton.Margin = new System.Windows.Forms.Padding(6);
            this.applySettingsButton.Name = "applySettingsButton";
            this.applySettingsButton.Size = new System.Drawing.Size(140, 50);
            this.applySettingsButton.TabIndex = 7;
            this.applySettingsButton.Text = "应用参数";
            this.applySettingsButton.UseVisualStyleBackColor = true;
            this.applySettingsButton.Click += new System.EventHandler(this.ApplySettingsButton_Click);
            // 
            // rectangleToolButton
            // 
            this.rectangleToolButton.BackColor = System.Drawing.Color.White;
            this.rectangleToolButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.rectangleToolButton.ForeColor = System.Drawing.Color.Black;
            this.rectangleToolButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.rectangleToolButton.Location = new System.Drawing.Point(1210, 20);
            this.rectangleToolButton.Margin = new System.Windows.Forms.Padding(6);
            this.rectangleToolButton.Name = "rectangleToolButton";
            this.rectangleToolButton.Padding = new System.Windows.Forms.Padding(8, 0, 8, 0);
            this.rectangleToolButton.Size = new System.Drawing.Size(144, 50);
            this.rectangleToolButton.TabIndex = 8;
            this.rectangleToolButton.Text = "    框选";
            this.rectangleToolButton.UseVisualStyleBackColor = false;
            this.rectangleToolButton.Click += new System.EventHandler(this.RectangleToolButton_Click);
            // 
            // xPointCountLabel
            // 
            this.xPointCountLabel.AutoSize = true;
            this.xPointCountLabel.ForeColor = System.Drawing.Color.Black;
            this.xPointCountLabel.Location = new System.Drawing.Point(1372, 32);
            this.xPointCountLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.xPointCountLabel.Name = "xPointCountLabel";
            this.xPointCountLabel.Size = new System.Drawing.Size(22, 24);
            this.xPointCountLabel.TabIndex = 9;
            this.xPointCountLabel.Text = "X";
            // 
            // xPointCountNumeric
            // 
            this.xPointCountNumeric.Location = new System.Drawing.Point(1400, 24);
            this.xPointCountNumeric.Margin = new System.Windows.Forms.Padding(6);
            this.xPointCountNumeric.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.xPointCountNumeric.Name = "xPointCountNumeric";
            this.xPointCountNumeric.Size = new System.Drawing.Size(84, 35);
            this.xPointCountNumeric.TabIndex = 10;
            this.xPointCountNumeric.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.xPointCountNumeric.ValueChanged += new System.EventHandler(this.ScanPointCount_ValueChanged);
            // 
            // yPointCountLabel
            // 
            this.yPointCountLabel.AutoSize = true;
            this.yPointCountLabel.ForeColor = System.Drawing.Color.Black;
            this.yPointCountLabel.Location = new System.Drawing.Point(1496, 32);
            this.yPointCountLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.yPointCountLabel.Name = "yPointCountLabel";
            this.yPointCountLabel.Size = new System.Drawing.Size(22, 24);
            this.yPointCountLabel.TabIndex = 11;
            this.yPointCountLabel.Text = "Y";
            // 
            // yPointCountNumeric
            // 
            this.yPointCountNumeric.Location = new System.Drawing.Point(1524, 24);
            this.yPointCountNumeric.Margin = new System.Windows.Forms.Padding(6);
            this.yPointCountNumeric.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.yPointCountNumeric.Name = "yPointCountNumeric";
            this.yPointCountNumeric.Size = new System.Drawing.Size(84, 35);
            this.yPointCountNumeric.TabIndex = 12;
            this.yPointCountNumeric.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.yPointCountNumeric.ValueChanged += new System.EventHandler(this.ScanPointCount_ValueChanged);
            // 
            // performanceLabel
            // 
            this.performanceLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.performanceLabel.ForeColor = System.Drawing.Color.Green;
            this.performanceLabel.Location = new System.Drawing.Point(1622, 20);
            this.performanceLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.performanceLabel.Name = "performanceLabel";
            this.performanceLabel.Size = new System.Drawing.Size(334, 50);
            this.performanceLabel.TabIndex = 13;
            this.performanceLabel.Text = "正在测量帧率…";
            this.performanceLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // previewPanel
            // 
            this.previewPanel.BackColor = System.Drawing.Color.Black;
            this.previewPanel.Controls.Add(this.stagePositionLabel);
            this.previewPanel.Controls.Add(this.statusLabel);
            this.previewPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.previewPanel.Location = new System.Drawing.Point(0, 92);
            this.previewPanel.Margin = new System.Windows.Forms.Padding(6);
            this.previewPanel.Name = "previewPanel";
            this.previewPanel.Size = new System.Drawing.Size(1962, 1024);
            this.previewPanel.TabIndex = 1;
            this.previewPanel.MouseDown += new System.Windows.Forms.MouseEventHandler(this.PreviewPanel_MouseDown);
            this.previewPanel.MouseMove += new System.Windows.Forms.MouseEventHandler(this.PreviewPanel_MouseMove);
            this.previewPanel.MouseUp += new System.Windows.Forms.MouseEventHandler(this.PreviewPanel_MouseUp);
            this.previewPanel.Resize += new System.EventHandler(this.PreviewPanel_Resize);
            // 
            // stagePositionLabel
            // 
            this.stagePositionLabel.BackColor = System.Drawing.Color.Black;
            this.stagePositionLabel.Dock = System.Windows.Forms.DockStyle.Right;
            this.stagePositionLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold);
            this.stagePositionLabel.ForeColor = System.Drawing.Color.LimeGreen;
            this.stagePositionLabel.Location = new System.Drawing.Point(1698, 0);
            this.stagePositionLabel.Name = "stagePositionLabel";
            this.stagePositionLabel.Padding = new System.Windows.Forms.Padding(12);
            this.stagePositionLabel.Size = new System.Drawing.Size(264, 1024);
            this.stagePositionLabel.TabIndex = 2;
            this.stagePositionLabel.Text = "平台坐标\r\nX: ---- mm\r\nY: ---- mm\r\nZ: ---- mm";
            // 
            // statusLabel
            // 
            this.statusLabel.BackColor = System.Drawing.Color.Transparent;
            this.statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.statusLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this.statusLabel.Location = new System.Drawing.Point(0, 0);
            this.statusLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(1962, 1024);
            this.statusLabel.TabIndex = 0;
            this.statusLabel.Text = "正在连接相机…";
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // CameraShowForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1962, 1116);
            this.Controls.Add(this.previewPanel);
            this.Controls.Add(this.controlPanel);
            this.Margin = new System.Windows.Forms.Padding(6);
            this.MinimumSize = new System.Drawing.Size(1428, 819);
            this.Name = "CameraShowForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "MIchrome 20 显微镜实时对焦";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.CameraShowForm_FormClosing);
            this.Shown += new System.EventHandler(this.CameraShowForm_Shown);
            this.controlPanel.ResumeLayout(false);
            this.controlPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.exposureNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.gainNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.xPointCountNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.yPointCountNumeric)).EndInit();
            this.previewPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button rectangleToolButton;
    }
}
