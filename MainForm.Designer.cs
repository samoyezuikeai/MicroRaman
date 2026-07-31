
namespace MicroLaman
{
    partial class MainForm
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.comboBoxController = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.RefreshMyComList = new System.Windows.Forms.Button();
            this.ConnectCom = new System.Windows.Forms.Button();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this.CameraShow = new System.Windows.Forms.ToolStripButton();
            this.CalibrateStage = new System.Windows.Forms.ToolStripButton();
            this.RealtimeSpectrum = new System.Windows.Forms.ToolStripButton();
            this.ScanSelection = new System.Windows.Forms.ToolStripButton();
            this.RamanMapping = new System.Windows.Forms.ToolStripButton();
            this.panel1 = new System.Windows.Forms.Panel();
            this.mappingReferenceGroupBox = new System.Windows.Forms.GroupBox();
            this.mappingAutomaticRadioButton = new System.Windows.Forms.RadioButton();
            this.mappingFullSpectrumRadioButton = new System.Windows.Forms.RadioButton();
            this.mappingReferenceHintLabel = new System.Windows.Forms.Label();
            this.mappingPcaRadioButton = new System.Windows.Forms.RadioButton();
            this.mappingPeakWidthRadioButton = new System.Windows.Forms.RadioButton();
            this.mappingPeakPositionRadioButton = new System.Windows.Forms.RadioButton();
            this.mappingPeakAreaRadioButton = new System.Windows.Forms.RadioButton();
            this.mappingPeakHeightRadioButton = new System.Windows.Forms.RadioButton();
            this.integrationRangeLabel = new System.Windows.Forms.Label();
            this.ApplySpectrometerParameters = new System.Windows.Forms.Button();
            this.spectrometerIntegrationTimeTextBox = new System.Windows.Forms.TextBox();
            this.integrationTimeLabel = new System.Windows.Forms.Label();
            this.LaserSettings = new System.Windows.Forms.Button();
            this.labelSpectrometer = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.formsPlot1 = new ScottPlot.WinForms.FormsPlot();
            this.scanWorkspace = new System.Windows.Forms.TableLayoutPanel();
            this.scanMatrixGroupBox = new System.Windows.Forms.GroupBox();
            this.scanMatrixPreviewControl = new MicroLaman.ScanMatrixPreviewControl();
            this.brightFieldGroupBox = new System.Windows.Forms.GroupBox();
            this.brightFieldPreviewStatusLabel = new System.Windows.Forms.Label();
            this.brightFieldPreviewPictureBox = new System.Windows.Forms.PictureBox();
            this.toolStrip1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.mappingReferenceGroupBox.SuspendLayout();
            this.scanWorkspace.SuspendLayout();
            this.scanMatrixGroupBox.SuspendLayout();
            this.brightFieldGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.brightFieldPreviewPictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // comboBoxController
            // 
            this.comboBoxController.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.comboBoxController.FormattingEnabled = true;
            this.comboBoxController.Location = new System.Drawing.Point(176, 31);
            this.comboBoxController.Margin = new System.Windows.Forms.Padding(6);
            this.comboBoxController.Name = "comboBoxController";
            this.comboBoxController.Size = new System.Drawing.Size(178, 43);
            this.comboBoxController.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.label1.Location = new System.Drawing.Point(14, 34);
            this.label1.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(177, 35);
            this.label1.TabIndex = 1;
            this.label1.Text = "控制台串口：";
            // 
            // RefreshMyComList
            // 
            this.RefreshMyComList.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.RefreshMyComList.Location = new System.Drawing.Point(20, 181);
            this.RefreshMyComList.Margin = new System.Windows.Forms.Padding(6);
            this.RefreshMyComList.Name = "RefreshMyComList";
            this.RefreshMyComList.Size = new System.Drawing.Size(106, 58);
            this.RefreshMyComList.TabIndex = 2;
            this.RefreshMyComList.Text = "刷新";
            this.RefreshMyComList.UseVisualStyleBackColor = true;
            this.RefreshMyComList.Click += new System.EventHandler(this.RefreshMyComList_Click);
            // 
            // ConnectCom
            // 
            this.ConnectCom.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.ConnectCom.Location = new System.Drawing.Point(138, 181);
            this.ConnectCom.Margin = new System.Windows.Forms.Padding(6);
            this.ConnectCom.Name = "ConnectCom";
            this.ConnectCom.Size = new System.Drawing.Size(106, 58);
            this.ConnectCom.TabIndex = 3;
            this.ConnectCom.Text = "连接";
            this.ConnectCom.UseVisualStyleBackColor = true;
            this.ConnectCom.Click += new System.EventHandler(this.ConnectCom_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.AutoSize = false;
            this.toolStrip1.BackColor = System.Drawing.Color.White;
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(44, 44);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.CameraShow,
            this.CalibrateStage,
            this.RealtimeSpectrum,
            this.ScanSelection,
            this.RamanMapping});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Padding = new System.Windows.Forms.Padding(4);
            this.toolStrip1.Size = new System.Drawing.Size(2228, 80);
            this.toolStrip1.TabIndex = 4;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // CameraShow
            // 
            this.CameraShow.AutoSize = false;
            this.CameraShow.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.CameraShow.Image = ((System.Drawing.Image)(resources.GetObject("CameraShow.Image")));
            this.CameraShow.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.CameraShow.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.CameraShow.Name = "CameraShow";
            this.CameraShow.Size = new System.Drawing.Size(56, 56);
            this.CameraShow.ToolTipText = "点击后打开显微镜摄像头，检测前一定要打开";
            this.CameraShow.Click += new System.EventHandler(this.CameraShow_Click);
            // 
            // CalibrateStage
            // 
            this.CalibrateStage.AutoSize = false;
            this.CalibrateStage.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.CalibrateStage.Image = ((System.Drawing.Image)(resources.GetObject("CalibrateStage.Image")));
            this.CalibrateStage.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.CalibrateStage.Name = "CalibrateStage";
            this.CalibrateStage.Size = new System.Drawing.Size(56, 56);
            this.CalibrateStage.ToolTipText = "在关闭激光、打开明场照明后计算像素与平台坐标比例";
            this.CalibrateStage.Click += new System.EventHandler(this.CalibrateStage_Click);
            //
            // RealtimeSpectrum
            //
            this.RealtimeSpectrum.AutoSize = false;
            this.RealtimeSpectrum.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.RealtimeSpectrum.Enabled = false;
            this.RealtimeSpectrum.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.RealtimeSpectrum.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.RealtimeSpectrum.Name = "RealtimeSpectrum";
            this.RealtimeSpectrum.Size = new System.Drawing.Size(104, 56);
            this.RealtimeSpectrum.Text = "实时光谱";
            this.RealtimeSpectrum.ToolTipText = "开始或停止实时读取光谱仪；停止时清空波形图";
            this.RealtimeSpectrum.Click += new System.EventHandler(this.RealtimeSpectrum_Click);
            //
            // ScanSelection
            //
            this.ScanSelection.AutoSize = false;
            this.ScanSelection.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.ScanSelection.Image = ((System.Drawing.Image)(resources.GetObject("ScanSelection.Image")));
            this.ScanSelection.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ScanSelection.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.ScanSelection.Name = "ScanSelection";
            this.ScanSelection.Size = new System.Drawing.Size(56, 56);
            this.ScanSelection.ToolTipText = "按蛇形顺序遍历框选区域内的全部网格点";
            this.ScanSelection.Click += new System.EventHandler(this.ScanSelection_Click);
            //
            // RamanMapping
            //
            this.RamanMapping.AutoSize = false;
            this.RamanMapping.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.RamanMapping.Enabled = false;
            this.RamanMapping.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.RamanMapping.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.RamanMapping.Name = "RamanMapping";
            this.RamanMapping.Size = new System.Drawing.Size(126, 56);
            this.RamanMapping.Text = "拉曼 Mapping";
            this.RamanMapping.ToolTipText = "扫描全部完成后，根据各点保存的整条光谱生成伪彩图";
            this.RamanMapping.Click += new System.EventHandler(this.RamanMapping_Click);
            //
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.Color.White;
            this.panel1.Controls.Add(this.mappingReferenceGroupBox);
            this.panel1.Controls.Add(this.integrationRangeLabel);
            this.panel1.Controls.Add(this.ApplySpectrometerParameters);
            this.panel1.Controls.Add(this.spectrometerIntegrationTimeTextBox);
            this.panel1.Controls.Add(this.integrationTimeLabel);
            this.panel1.Controls.Add(this.LaserSettings);
            this.panel1.Controls.Add(this.labelSpectrometer);
            this.panel1.Controls.Add(this.label2);
            this.panel1.Controls.Add(this.comboBoxController);
            this.panel1.Controls.Add(this.label1);
            this.panel1.Controls.Add(this.ConnectCom);
            this.panel1.Controls.Add(this.RefreshMyComList);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Left;
            this.panel1.Location = new System.Drawing.Point(0, 80);
            this.panel1.Margin = new System.Windows.Forms.Padding(6);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(402, 1270);
            this.panel1.TabIndex = 5;
            //
            // mappingReferenceGroupBox
            //
            this.mappingReferenceGroupBox.Controls.Add(this.mappingPcaRadioButton);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingFullSpectrumRadioButton);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingReferenceHintLabel);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingPeakWidthRadioButton);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingPeakPositionRadioButton);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingPeakAreaRadioButton);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingPeakHeightRadioButton);
            this.mappingReferenceGroupBox.Controls.Add(this.mappingAutomaticRadioButton);
            this.mappingReferenceGroupBox.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.mappingReferenceGroupBox.Location = new System.Drawing.Point(20, 494);
            this.mappingReferenceGroupBox.Name = "mappingReferenceGroupBox";
            this.mappingReferenceGroupBox.Size = new System.Drawing.Size(346, 434);
            this.mappingReferenceGroupBox.TabIndex = 12;
            this.mappingReferenceGroupBox.TabStop = false;
            this.mappingReferenceGroupBox.Text = "Mapping 参考指标";
            //
            //
            // mappingAutomaticRadioButton
            //
            this.mappingAutomaticRadioButton.AutoSize = true;
            this.mappingAutomaticRadioButton.Checked = true;
            this.mappingAutomaticRadioButton.Location = new System.Drawing.Point(18, 40);
            this.mappingAutomaticRadioButton.Name = "mappingAutomaticRadioButton";
            this.mappingAutomaticRadioButton.Size = new System.Drawing.Size(137, 35);
            this.mappingAutomaticRadioButton.TabIndex = 0;
            this.mappingAutomaticRadioButton.TabStop = true;
            this.mappingAutomaticRadioButton.Text = "自动推荐";
            this.mappingAutomaticRadioButton.UseVisualStyleBackColor = true;
            //
            // mappingPcaRadioButton
            //
            this.mappingPcaRadioButton.AutoSize = true;
            this.mappingPcaRadioButton.Location = new System.Drawing.Point(18, 328);
            this.mappingPcaRadioButton.Name = "mappingPcaRadioButton";
            this.mappingPcaRadioButton.Size = new System.Drawing.Size(199, 35);
            this.mappingPcaRadioButton.TabIndex = 6;
            this.mappingPcaRadioButton.Text = "PCA 全谱异常";
            this.mappingPcaRadioButton.UseVisualStyleBackColor = true;
            //
            // mappingFullSpectrumRadioButton
            //
            this.mappingFullSpectrumRadioButton.AutoSize = true;
            this.mappingFullSpectrumRadioButton.Location = new System.Drawing.Point(18, 280);
            this.mappingFullSpectrumRadioButton.Name = "mappingFullSpectrumRadioButton";
            this.mappingFullSpectrumRadioButton.Size = new System.Drawing.Size(248, 35);
            this.mappingFullSpectrumRadioButton.TabIndex = 5;
            this.mappingFullSpectrumRadioButton.Text = "全谱差异（荧光）";
            this.mappingFullSpectrumRadioButton.UseVisualStyleBackColor = true;
            //
            // mappingReferenceHintLabel
            //
            this.mappingReferenceHintLabel.AutoSize = false;
            this.mappingReferenceHintLabel.ForeColor = System.Drawing.Color.DimGray;
            this.mappingReferenceHintLabel.Location = new System.Drawing.Point(18, 376);
            this.mappingReferenceHintLabel.Name = "mappingReferenceHintLabel";
            this.mappingReferenceHintLabel.Size = new System.Drawing.Size(304, 43);
            this.mappingReferenceHintLabel.TabIndex = 7;
            this.mappingReferenceHintLabel.Text = "自动推荐会比较全部标准后选择；全谱差异适合荧光或未知样品。";
            //
            // mappingPeakWidthRadioButton
            //
            this.mappingPeakWidthRadioButton.AutoSize = true;
            this.mappingPeakWidthRadioButton.Location = new System.Drawing.Point(18, 232);
            this.mappingPeakWidthRadioButton.Name = "mappingPeakWidthRadioButton";
            this.mappingPeakWidthRadioButton.Size = new System.Drawing.Size(205, 35);
            this.mappingPeakWidthRadioButton.TabIndex = 4;
            this.mappingPeakWidthRadioButton.Text = "半高宽 FWHM";
            this.mappingPeakWidthRadioButton.UseVisualStyleBackColor = true;
            //
            // mappingPeakPositionRadioButton
            //
            this.mappingPeakPositionRadioButton.AutoSize = true;
            this.mappingPeakPositionRadioButton.Location = new System.Drawing.Point(18, 184);
            this.mappingPeakPositionRadioButton.Name = "mappingPeakPositionRadioButton";
            this.mappingPeakPositionRadioButton.Size = new System.Drawing.Size(137, 35);
            this.mappingPeakPositionRadioButton.TabIndex = 3;
            this.mappingPeakPositionRadioButton.Text = "峰位置";
            this.mappingPeakPositionRadioButton.UseVisualStyleBackColor = true;
            //
            // mappingPeakAreaRadioButton
            //
            this.mappingPeakAreaRadioButton.AutoSize = true;
            this.mappingPeakAreaRadioButton.Location = new System.Drawing.Point(18, 136);
            this.mappingPeakAreaRadioButton.Name = "mappingPeakAreaRadioButton";
            this.mappingPeakAreaRadioButton.Size = new System.Drawing.Size(137, 35);
            this.mappingPeakAreaRadioButton.TabIndex = 2;
            this.mappingPeakAreaRadioButton.Text = "峰面积";
            this.mappingPeakAreaRadioButton.UseVisualStyleBackColor = true;
            //
            // mappingPeakHeightRadioButton
            //
            this.mappingPeakHeightRadioButton.AutoSize = true;
            this.mappingPeakHeightRadioButton.Location = new System.Drawing.Point(18, 88);
            this.mappingPeakHeightRadioButton.Name = "mappingPeakHeightRadioButton";
            this.mappingPeakHeightRadioButton.Size = new System.Drawing.Size(189, 35);
            this.mappingPeakHeightRadioButton.TabIndex = 1;
            this.mappingPeakHeightRadioButton.Text = "峰高（强度）";
            this.mappingPeakHeightRadioButton.UseVisualStyleBackColor = true;
            //
            // integrationRangeLabel
            // 
            this.integrationRangeLabel.AutoSize = true;
            this.integrationRangeLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5F);
            this.integrationRangeLabel.ForeColor = System.Drawing.Color.DimGray;
            this.integrationRangeLabel.Location = new System.Drawing.Point(20, 444);
            this.integrationRangeLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.integrationRangeLabel.Name = "integrationRangeLabel";
            this.integrationRangeLabel.Size = new System.Drawing.Size(266, 30);
            this.integrationRangeLabel.TabIndex = 11;
            this.integrationRangeLabel.Text = "可设置范围：连接后读取";
            // 
            // ApplySpectrometerParameters
            // 
            this.ApplySpectrometerParameters.Enabled = false;
            this.ApplySpectrometerParameters.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.ApplySpectrometerParameters.Location = new System.Drawing.Point(20, 380);
            this.ApplySpectrometerParameters.Margin = new System.Windows.Forms.Padding(6);
            this.ApplySpectrometerParameters.Name = "ApplySpectrometerParameters";
            this.ApplySpectrometerParameters.Size = new System.Drawing.Size(314, 52);
            this.ApplySpectrometerParameters.TabIndex = 10;
            this.ApplySpectrometerParameters.Text = "应用参数";
            this.ApplySpectrometerParameters.UseVisualStyleBackColor = true;
            this.ApplySpectrometerParameters.Click += new System.EventHandler(this.ApplySpectrometerParameters_Click);
            // 
            // spectrometerIntegrationTimeTextBox
            // 
            this.spectrometerIntegrationTimeTextBox.Enabled = false;
            this.spectrometerIntegrationTimeTextBox.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.spectrometerIntegrationTimeTextBox.Location = new System.Drawing.Point(228, 327);
            this.spectrometerIntegrationTimeTextBox.Margin = new System.Windows.Forms.Padding(6);
            this.spectrometerIntegrationTimeTextBox.Name = "spectrometerIntegrationTimeTextBox";
            this.spectrometerIntegrationTimeTextBox.Size = new System.Drawing.Size(138, 41);
            this.spectrometerIntegrationTimeTextBox.TabIndex = 9;
            this.spectrometerIntegrationTimeTextBox.Text = "1000";
            // 
            // integrationTimeLabel
            // 
            this.integrationTimeLabel.AutoSize = true;
            this.integrationTimeLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.integrationTimeLabel.Location = new System.Drawing.Point(20, 331);
            this.integrationTimeLabel.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.integrationTimeLabel.Name = "integrationTimeLabel";
            this.integrationTimeLabel.Size = new System.Drawing.Size(214, 35);
            this.integrationTimeLabel.TabIndex = 8;
            this.integrationTimeLabel.Text = "积分时间 (ms)：";
            //
            // LaserSettings
            //
            this.LaserSettings.Enabled = false;
            this.LaserSettings.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.LaserSettings.Location = new System.Drawing.Point(20, 251);
            this.LaserSettings.Margin = new System.Windows.Forms.Padding(6);
            this.LaserSettings.Name = "LaserSettings";
            this.LaserSettings.Size = new System.Drawing.Size(314, 58);
            this.LaserSettings.TabIndex = 6;
            this.LaserSettings.Text = "激光器设置";
            this.LaserSettings.UseVisualStyleBackColor = true;
            this.LaserSettings.Click += new System.EventHandler(this.LaserSettings_Click);
            // 
            // labelSpectrometer
            // 
            this.labelSpectrometer.AutoSize = true;
            this.labelSpectrometer.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.labelSpectrometer.Location = new System.Drawing.Point(14, 129);
            this.labelSpectrometer.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.labelSpectrometer.Name = "labelSpectrometer";
            this.labelSpectrometer.Size = new System.Drawing.Size(204, 35);
            this.labelSpectrometer.TabIndex = 7;
            this.labelSpectrometer.Text = "光谱仪：未连接";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft YaHei UI", 10F);
            this.label2.Location = new System.Drawing.Point(14, 91);
            this.label2.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(204, 35);
            this.label2.TabIndex = 5;
            this.label2.Text = "激光器：未连接";
            // 
            // formsPlot1
            // 
            this.formsPlot1.Dock = System.Windows.Forms.DockStyle.Top;
            this.formsPlot1.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.formsPlot1.Location = new System.Drawing.Point(402, 80);
            this.formsPlot1.Margin = new System.Windows.Forms.Padding(6);
            this.formsPlot1.Name = "formsPlot1";
            this.formsPlot1.Size = new System.Drawing.Size(1826, 700);
            this.formsPlot1.TabIndex = 6;
            // 
            // scanWorkspace
            // 
            this.scanWorkspace.ColumnCount = 2;
            this.scanWorkspace.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.scanWorkspace.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.scanWorkspace.Controls.Add(this.scanMatrixGroupBox, 0, 0);
            this.scanWorkspace.Controls.Add(this.brightFieldGroupBox, 1, 0);
            this.scanWorkspace.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scanWorkspace.Location = new System.Drawing.Point(402, 780);
            this.scanWorkspace.Name = "scanWorkspace";
            this.scanWorkspace.Padding = new System.Windows.Forms.Padding(12);
            this.scanWorkspace.RowCount = 1;
            this.scanWorkspace.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.scanWorkspace.Size = new System.Drawing.Size(1826, 570);
            this.scanWorkspace.TabIndex = 7;
            // 
            // scanMatrixGroupBox
            // 
            this.scanMatrixGroupBox.Controls.Add(this.scanMatrixPreviewControl);
            this.scanMatrixGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scanMatrixGroupBox.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.scanMatrixGroupBox.Location = new System.Drawing.Point(15, 15);
            this.scanMatrixGroupBox.Name = "scanMatrixGroupBox";
            this.scanMatrixGroupBox.Size = new System.Drawing.Size(895, 540);
            this.scanMatrixGroupBox.TabIndex = 0;
            this.scanMatrixGroupBox.TabStop = false;
            this.scanMatrixGroupBox.Text = "扫描坐标矩阵";
            // 
            // scanMatrixPreviewControl
            // 
            this.scanMatrixPreviewControl.BackColor = System.Drawing.Color.White;
            this.scanMatrixPreviewControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scanMatrixPreviewControl.Location = new System.Drawing.Point(3, 34);
            this.scanMatrixPreviewControl.Name = "scanMatrixPreviewControl";
            this.scanMatrixPreviewControl.Size = new System.Drawing.Size(889, 503);
            this.scanMatrixPreviewControl.TabIndex = 0;
            // 
            // brightFieldGroupBox
            // 
            this.brightFieldGroupBox.Controls.Add(this.brightFieldPreviewStatusLabel);
            this.brightFieldGroupBox.Controls.Add(this.brightFieldPreviewPictureBox);
            this.brightFieldGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.brightFieldGroupBox.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.brightFieldGroupBox.Location = new System.Drawing.Point(916, 15);
            this.brightFieldGroupBox.Name = "brightFieldGroupBox";
            this.brightFieldGroupBox.Size = new System.Drawing.Size(895, 540);
            this.brightFieldGroupBox.TabIndex = 1;
            this.brightFieldGroupBox.TabStop = false;
            this.brightFieldGroupBox.Text = "明场参考图";
            // 
            // brightFieldPreviewStatusLabel
            // 
            this.brightFieldPreviewStatusLabel.BackColor = System.Drawing.Color.Transparent;
            this.brightFieldPreviewStatusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.brightFieldPreviewStatusLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F);
            this.brightFieldPreviewStatusLabel.ForeColor = System.Drawing.Color.Gainsboro;
            this.brightFieldPreviewStatusLabel.Location = new System.Drawing.Point(3, 34);
            this.brightFieldPreviewStatusLabel.Name = "brightFieldPreviewStatusLabel";
            this.brightFieldPreviewStatusLabel.Size = new System.Drawing.Size(889, 503);
            this.brightFieldPreviewStatusLabel.TabIndex = 1;
            this.brightFieldPreviewStatusLabel.Text = "等待检测开始";
            this.brightFieldPreviewStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // brightFieldPreviewPictureBox
            // 
            this.brightFieldPreviewPictureBox.BackColor = System.Drawing.Color.Black;
            this.brightFieldPreviewPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.brightFieldPreviewPictureBox.Location = new System.Drawing.Point(3, 34);
            this.brightFieldPreviewPictureBox.Name = "brightFieldPreviewPictureBox";
            this.brightFieldPreviewPictureBox.Size = new System.Drawing.Size(889, 503);
            this.brightFieldPreviewPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.brightFieldPreviewPictureBox.TabIndex = 0;
            this.brightFieldPreviewPictureBox.TabStop = false;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(2228, 1350);
            this.Controls.Add(this.scanWorkspace);
            this.Controls.Add(this.formsPlot1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.toolStrip1);
            this.Margin = new System.Windows.Forms.Padding(6);
            this.Name = "MainForm";
            this.Text = "MicroLaman";
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.mappingReferenceGroupBox.ResumeLayout(false);
            this.mappingReferenceGroupBox.PerformLayout();
            this.scanWorkspace.ResumeLayout(false);
            this.scanMatrixGroupBox.ResumeLayout(false);
            this.brightFieldGroupBox.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.brightFieldPreviewPictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ComboBox comboBoxController;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button RefreshMyComList;
        private System.Windows.Forms.Button ConnectCom;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton CameraShow;
        private System.Windows.Forms.ToolStripButton CalibrateStage;
        private System.Windows.Forms.ToolStripButton RealtimeSpectrum;
        private System.Windows.Forms.ToolStripButton ScanSelection;
        private System.Windows.Forms.ToolStripButton RamanMapping;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.GroupBox mappingReferenceGroupBox;
        private System.Windows.Forms.RadioButton mappingAutomaticRadioButton;
        private System.Windows.Forms.RadioButton mappingFullSpectrumRadioButton;
        private System.Windows.Forms.Label mappingReferenceHintLabel;
        private System.Windows.Forms.RadioButton mappingPcaRadioButton;
        private System.Windows.Forms.RadioButton mappingPeakWidthRadioButton;
        private System.Windows.Forms.RadioButton mappingPeakPositionRadioButton;
        private System.Windows.Forms.RadioButton mappingPeakAreaRadioButton;
        private System.Windows.Forms.RadioButton mappingPeakHeightRadioButton;
        private ScottPlot.WinForms.FormsPlot formsPlot1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label labelSpectrometer;
        private System.Windows.Forms.Button LaserSettings;
        private System.Windows.Forms.Label integrationTimeLabel;
        private System.Windows.Forms.TextBox spectrometerIntegrationTimeTextBox;
        private System.Windows.Forms.Button ApplySpectrometerParameters;
        private System.Windows.Forms.Label integrationRangeLabel;
        private System.Windows.Forms.TableLayoutPanel scanWorkspace;
        private System.Windows.Forms.GroupBox scanMatrixGroupBox;
        private MicroLaman.ScanMatrixPreviewControl scanMatrixPreviewControl;
        private System.Windows.Forms.GroupBox brightFieldGroupBox;
        private System.Windows.Forms.PictureBox brightFieldPreviewPictureBox;
        private System.Windows.Forms.Label brightFieldPreviewStatusLabel;
    }
}

