using System;
using System.Drawing;
using System.Windows.Forms;

namespace MicroRaman
{
    /// <summary>
    /// 激光器设置窗体的可编辑 WinForms 设计器布局。
    /// </summary>
    public sealed partial class LaserSettingsForm
    {
        private TableLayoutPanel rootLayout;
        private TableLayoutPanel settingsLayout;
        private GroupBox switchAndTemperatureGroupBox;
        private FlowLayoutPanel switchButtonPanel;
        private Button allOnButton;
        private Button allOffButton;
        private Button ldToggleButton;
        private Button tecToggleButton;
        private TableLayoutPanel temperatureSettingsLayout;
        private NumericUpDown periodValue;
        private Button applyPeriodButton;
        private NumericUpDown temperatureValue;
        private Button applyTemperatureButton;
        private FlowLayoutPanel periodPresetPanel;
        private FlowLayoutPanel temperaturePresetPanel;
        private Label powerRangeLabel;
        private GroupBox powerAndCurrentGroupBox;
        private TableLayoutPanel powerSettingsLayout;
        private NumericUpDown powerValue;
        private Button applyPowerButton;
        private NumericUpDown pwmValue;
        private Button applyPwmButton;
        private NumericUpDown pwmCorrectValue;
        private Button applyPwmCorrectButton;
        private NumericUpDown currentValue;
        private Button applyCurrentButton;
        private FlowLayoutPanel powerPresetPanel;
        private FlowLayoutPanel pwmPresetPanel;
        private FlowLayoutPanel pwmCorrectPresetPanel;
        private FlowLayoutPanel currentPresetPanel;
        private GroupBox statusGroupBox;
        private TableLayoutPanel statusLayout;
        private FlowLayoutPanel statusActionPanel;
        private Button refreshStatusButton;
        private CheckBox autoRefreshCheckBox;
        private TableLayoutPanel statusValuesLayout;
        private Label statusTemperatureValue;
        private Label statusTargetTemperatureValue;
        private Label statusTecCurrentValue;
        private Label statusKpValue;
        private Label statusKiValue;
        private Label statusKdValue;
        private Label statusTmpgnValue;
        private Label statusLdCurrentValue;
        private Label statusPowerOnValue;
        private Label rawStatusLabel;
        private Label connectionLabel;

        private void InitializeComponent()
        {
            this.rootLayout = new TableLayoutPanel();
            this.connectionLabel = new Label();
            this.settingsLayout = new TableLayoutPanel();
            this.switchAndTemperatureGroupBox = new GroupBox();
            this.switchButtonPanel = new FlowLayoutPanel();
            this.allOnButton = new Button();
            this.allOffButton = new Button();
            this.ldToggleButton = new Button();
            this.tecToggleButton = new Button();
            this.temperatureSettingsLayout = new TableLayoutPanel();
            this.periodValue = new NumericUpDown();
            this.applyPeriodButton = new Button();
            this.temperatureValue = new NumericUpDown();
            this.applyTemperatureButton = new Button();
            this.periodPresetPanel = new FlowLayoutPanel();
            this.temperaturePresetPanel = new FlowLayoutPanel();
            this.powerRangeLabel = new Label();
            this.powerAndCurrentGroupBox = new GroupBox();
            this.powerSettingsLayout = new TableLayoutPanel();
            this.powerValue = new NumericUpDown();
            this.applyPowerButton = new Button();
            this.pwmValue = new NumericUpDown();
            this.applyPwmButton = new Button();
            this.pwmCorrectValue = new NumericUpDown();
            this.applyPwmCorrectButton = new Button();
            this.currentValue = new NumericUpDown();
            this.applyCurrentButton = new Button();
            this.powerPresetPanel = new FlowLayoutPanel();
            this.pwmPresetPanel = new FlowLayoutPanel();
            this.pwmCorrectPresetPanel = new FlowLayoutPanel();
            this.currentPresetPanel = new FlowLayoutPanel();
            this.statusGroupBox = new GroupBox();
            this.statusLayout = new TableLayoutPanel();
            this.statusActionPanel = new FlowLayoutPanel();
            this.refreshStatusButton = new Button();
            this.autoRefreshCheckBox = new CheckBox();
            this.statusValuesLayout = new TableLayoutPanel();
            this.statusTemperatureValue = new Label();
            this.statusTargetTemperatureValue = new Label();
            this.statusTecCurrentValue = new Label();
            this.statusKpValue = new Label();
            this.statusKiValue = new Label();
            this.statusKdValue = new Label();
            this.statusTmpgnValue = new Label();
            this.statusLdCurrentValue = new Label();
            this.statusPowerOnValue = new Label();
            this.rawStatusLabel = new Label();
            ((System.ComponentModel.ISupportInitialize)(this.periodValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.temperatureValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.powerValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pwmValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pwmCorrectValue)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.currentValue)).BeginInit();
            this.SuspendLayout();
            // 
            // rootLayout
            // 
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.connectionLabel, 0, 0);
            this.rootLayout.Controls.Add(this.settingsLayout, 0, 1);
            this.rootLayout.Controls.Add(this.statusGroupBox, 0, 2);
            this.rootLayout.Dock = DockStyle.Fill;
            this.rootLayout.Location = new Point(0, 0);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.Padding = new Padding(12);
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
            this.rootLayout.Size = new Size(1180, 720);
            this.rootLayout.TabIndex = 0;
            // 
            // connectionLabel
            // 
            this.connectionLabel.Dock = DockStyle.Fill;
            this.connectionLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            this.connectionLabel.Location = new Point(15, 15);
            this.connectionLabel.Name = "connectionLabel";
            this.connectionLabel.Size = new Size(1150, 42);
            this.connectionLabel.TabIndex = 0;
            this.connectionLabel.Text = "激光器：未连接";
            this.connectionLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // settingsLayout
            // 
            this.settingsLayout.ColumnCount = 2;
            this.settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            this.settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            this.settingsLayout.Controls.Add(this.switchAndTemperatureGroupBox, 0, 0);
            this.settingsLayout.Controls.Add(this.powerAndCurrentGroupBox, 1, 0);
            this.settingsLayout.Dock = DockStyle.Fill;
            this.settingsLayout.Location = new Point(15, 61);
            this.settingsLayout.Name = "settingsLayout";
            this.settingsLayout.Padding = new Padding(0, 4, 0, 8);
            this.settingsLayout.RowCount = 1;
            this.settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.settingsLayout.Size = new Size(1150, 454);
            this.settingsLayout.TabIndex = 1;
            // 
            // switchAndTemperatureGroupBox
            // 
            this.switchAndTemperatureGroupBox.Controls.Add(this.powerRangeLabel);
            this.switchAndTemperatureGroupBox.Controls.Add(this.temperatureSettingsLayout);
            this.switchAndTemperatureGroupBox.Controls.Add(this.switchButtonPanel);
            this.switchAndTemperatureGroupBox.Dock = DockStyle.Fill;
            this.switchAndTemperatureGroupBox.Location = new Point(3, 7);
            this.switchAndTemperatureGroupBox.Name = "switchAndTemperatureGroupBox";
            this.switchAndTemperatureGroupBox.Padding = new Padding(12);
            this.switchAndTemperatureGroupBox.Size = new Size(477, 439);
            this.switchAndTemperatureGroupBox.TabIndex = 0;
            this.switchAndTemperatureGroupBox.TabStop = false;
            this.switchAndTemperatureGroupBox.Text = "开关与温控";
            // 
            // switchButtonPanel
            // 
            this.switchButtonPanel.Controls.Add(this.allOnButton);
            this.switchButtonPanel.Controls.Add(this.allOffButton);
            this.switchButtonPanel.Controls.Add(this.ldToggleButton);
            this.switchButtonPanel.Controls.Add(this.tecToggleButton);
            this.switchButtonPanel.Dock = DockStyle.Top;
            this.switchButtonPanel.Location = new Point(12, 29);
            this.switchButtonPanel.Name = "switchButtonPanel";
            this.switchButtonPanel.Size = new Size(453, 40);
            this.switchButtonPanel.TabIndex = 0;
            // 
            // allOnButton
            // 
            this.allOnButton.Location = new Point(3, 3);
            this.allOnButton.Name = "allOnButton";
            this.allOnButton.Size = new Size(82, 30);
            this.allOnButton.TabIndex = 0;
            this.allOnButton.Text = "全部开启";
            this.allOnButton.UseVisualStyleBackColor = true;
            this.allOnButton.Click += new EventHandler(this.AllOnButton_Click);
            // 
            // allOffButton
            // 
            this.allOffButton.Location = new Point(91, 3);
            this.allOffButton.Name = "allOffButton";
            this.allOffButton.Size = new Size(82, 30);
            this.allOffButton.TabIndex = 1;
            this.allOffButton.Text = "全部关闭";
            this.allOffButton.UseVisualStyleBackColor = true;
            this.allOffButton.Click += new EventHandler(this.AllOffButton_Click);
            // 
            // ldToggleButton
            // 
            this.ldToggleButton.Location = new Point(179, 3);
            this.ldToggleButton.Name = "ldToggleButton";
            this.ldToggleButton.Size = new Size(82, 30);
            this.ldToggleButton.TabIndex = 2;
            this.ldToggleButton.Text = "LD：关";
            this.ldToggleButton.UseVisualStyleBackColor = true;
            this.ldToggleButton.Click += new EventHandler(this.LdToggleButton_Click);
            // 
            // tecToggleButton
            // 
            this.tecToggleButton.Location = new Point(267, 3);
            this.tecToggleButton.Name = "tecToggleButton";
            this.tecToggleButton.Size = new Size(82, 30);
            this.tecToggleButton.TabIndex = 3;
            this.tecToggleButton.Text = "TEC：关";
            this.tecToggleButton.UseVisualStyleBackColor = true;
            this.tecToggleButton.Click += new EventHandler(this.TecToggleButton_Click);
            // 
            // temperatureSettingsLayout
            // 
            this.temperatureSettingsLayout.ColumnCount = 4;
            this.temperatureSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.temperatureSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            this.temperatureSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
            this.temperatureSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.temperatureSettingsLayout.Controls.Add(new Label { Name = "periodLabel", Text = "激光开关半周期 (ms)", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 0);
            this.temperatureSettingsLayout.Controls.Add(this.periodValue, 1, 0);
            this.temperatureSettingsLayout.Controls.Add(this.applyPeriodButton, 2, 0);
            this.temperatureSettingsLayout.Controls.Add(this.periodPresetPanel, 0, 1);
            this.temperatureSettingsLayout.SetColumnSpan(this.periodPresetPanel, 4);
            this.temperatureSettingsLayout.Controls.Add(new Label { Name = "temperatureLabel", Text = "TEC 目标温度 (°C)", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 2);
            this.temperatureSettingsLayout.Controls.Add(this.temperatureValue, 1, 2);
            this.temperatureSettingsLayout.Controls.Add(this.applyTemperatureButton, 2, 2);
            this.temperatureSettingsLayout.Controls.Add(this.temperaturePresetPanel, 0, 3);
            this.temperatureSettingsLayout.SetColumnSpan(this.temperaturePresetPanel, 4);
            this.temperatureSettingsLayout.Location = new Point(20, 82);
            this.temperatureSettingsLayout.Name = "temperatureSettingsLayout";
            this.temperatureSettingsLayout.RowCount = 4;
            this.temperatureSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.temperatureSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            this.temperatureSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.temperatureSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            this.temperatureSettingsLayout.Size = new Size(430, 148);
            this.temperatureSettingsLayout.TabIndex = 1;
            // 
            // periodValue
            // 
            this.periodValue.Anchor = AnchorStyles.Left;
            this.periodValue.Margin = new Padding(0, 6, 0, 0);
            this.periodValue.Size = new Size(130, 28);
            this.periodValue.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.periodValue.Name = "periodValue";
            this.periodValue.Value = new decimal(new int[] { 500, 0, 0, 0 });
            // 
            // applyPeriodButton
            // 
            this.applyPeriodButton.Dock = DockStyle.Fill;
            this.applyPeriodButton.Name = "applyPeriodButton";
            this.applyPeriodButton.Text = "应用";
            this.applyPeriodButton.UseVisualStyleBackColor = true;
            this.applyPeriodButton.Click += new EventHandler(this.ApplyPeriodButton_Click);
            // 
            // temperatureValue
            // 
            this.temperatureValue.Anchor = AnchorStyles.Left;
            this.temperatureValue.Margin = new Padding(0, 6, 0, 0);
            this.temperatureValue.Size = new Size(130, 28);
            this.temperatureValue.Minimum = new decimal(new int[] { 20, 0, 0, -2147483648 });
            this.temperatureValue.Name = "temperatureValue";
            this.temperatureValue.Value = new decimal(new int[] { 25, 0, 0, 0 });
            // 
            // applyTemperatureButton
            // 
            this.applyTemperatureButton.Dock = DockStyle.Fill;
            this.applyTemperatureButton.Name = "applyTemperatureButton";
            this.applyTemperatureButton.Text = "应用";
            this.applyTemperatureButton.UseVisualStyleBackColor = true;
            this.applyTemperatureButton.Click += new EventHandler(this.ApplyTemperatureButton_Click);
            // 
            // periodPresetPanel
            // 
            this.periodPresetPanel.Name = "periodPresetPanel";
            this.periodPresetPanel.Dock = DockStyle.Fill;
            this.periodPresetPanel.TabIndex = 2;
            // 
            // temperaturePresetPanel
            // 
            this.temperaturePresetPanel.Name = "temperaturePresetPanel";
            this.temperaturePresetPanel.Dock = DockStyle.Fill;
            this.temperaturePresetPanel.TabIndex = 3;
            // 
            // powerRangeLabel
            // 
            this.powerRangeLabel.AutoSize = true;
            this.powerRangeLabel.ForeColor = Color.DimGray;
            this.powerRangeLabel.Location = new Point(20, 246);
            this.powerRangeLabel.Name = "powerRangeLabel";
            this.powerRangeLabel.Size = new Size(250, 20);
            this.powerRangeLabel.TabIndex = 2;
            this.powerRangeLabel.Text = "激光功率范围：等待设备信息";
            // 
            // powerAndCurrentGroupBox
            // 
            this.powerAndCurrentGroupBox.Controls.Add(this.powerSettingsLayout);
            this.powerAndCurrentGroupBox.Dock = DockStyle.Fill;
            this.powerAndCurrentGroupBox.Location = new Point(486, 7);
            this.powerAndCurrentGroupBox.Name = "powerAndCurrentGroupBox";
            this.powerAndCurrentGroupBox.Padding = new Padding(12);
            this.powerAndCurrentGroupBox.Size = new Size(661, 439);
            this.powerAndCurrentGroupBox.TabIndex = 1;
            this.powerAndCurrentGroupBox.TabStop = false;
            this.powerAndCurrentGroupBox.Text = "功率与电流";
            // 
            // powerSettingsLayout
            // 
            this.powerSettingsLayout.ColumnCount = 4;
            this.powerSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            this.powerSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190F));
            this.powerSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            this.powerSettingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.powerSettingsLayout.Controls.Add(new Label { Name = "powerLabel", Text = "激光功率 (mW)", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 0);
            this.powerSettingsLayout.Controls.Add(this.powerValue, 1, 0);
            this.powerSettingsLayout.Controls.Add(this.applyPowerButton, 2, 0);
            this.powerSettingsLayout.Controls.Add(this.powerPresetPanel, 0, 1);
            this.powerSettingsLayout.SetColumnSpan(this.powerPresetPanel, 4);
            this.powerSettingsLayout.Controls.Add(new Label { Name = "pwmLabel", Text = "激光功率 PWM", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 2);
            this.powerSettingsLayout.Controls.Add(this.pwmValue, 1, 2);
            this.powerSettingsLayout.Controls.Add(this.applyPwmButton, 2, 2);
            this.powerSettingsLayout.Controls.Add(this.pwmPresetPanel, 0, 3);
            this.powerSettingsLayout.SetColumnSpan(this.pwmPresetPanel, 4);
            this.powerSettingsLayout.Controls.Add(new Label { Name = "pwmCorrectLabel", Text = "PWM 功率校正", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 4);
            this.powerSettingsLayout.Controls.Add(this.pwmCorrectValue, 1, 4);
            this.powerSettingsLayout.Controls.Add(this.applyPwmCorrectButton, 2, 4);
            this.powerSettingsLayout.Controls.Add(this.pwmCorrectPresetPanel, 0, 5);
            this.powerSettingsLayout.SetColumnSpan(this.pwmCorrectPresetPanel, 4);
            this.powerSettingsLayout.Controls.Add(new Label { Name = "currentLabel", Text = "激光电流 (mA)", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Anchor = AnchorStyles.Left }, 0, 6);
            this.powerSettingsLayout.Controls.Add(this.currentValue, 1, 6);
            this.powerSettingsLayout.Controls.Add(this.applyCurrentButton, 2, 6);
            this.powerSettingsLayout.Controls.Add(this.currentPresetPanel, 0, 7);
            this.powerSettingsLayout.SetColumnSpan(this.currentPresetPanel, 4);
            this.powerSettingsLayout.Dock = DockStyle.Top;
            this.powerSettingsLayout.Location = new Point(20, 29);
            this.powerSettingsLayout.Name = "powerSettingsLayout";
            this.powerSettingsLayout.RowCount = 8;
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            this.powerSettingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            this.powerSettingsLayout.Size = new Size(615, 334);
            this.powerSettingsLayout.TabIndex = 0;
            // 
            // Numeric controls
            // 
            this.powerValue.Anchor = AnchorStyles.Left;
            this.powerValue.Margin = new Padding(0, 7, 0, 0);
            this.powerValue.Size = new Size(190, 28);
            this.powerValue.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.powerValue.Name = "powerValue";
            this.powerValue.Value = new decimal(new int[] { 50, 0, 0, 0 });
            this.pwmValue.Anchor = AnchorStyles.Left;
            this.pwmValue.Margin = new Padding(0, 7, 0, 0);
            this.pwmValue.Size = new Size(190, 28);
            this.pwmValue.Maximum = new decimal(new int[] { 4800, 0, 0, 0 });
            this.pwmValue.Name = "pwmValue";
            this.pwmValue.Value = new decimal(new int[] { 1400, 0, 0, 0 });
            this.pwmCorrectValue.Anchor = AnchorStyles.Left;
            this.pwmCorrectValue.Margin = new Padding(0, 7, 0, 0);
            this.pwmCorrectValue.Size = new Size(190, 28);
            this.pwmCorrectValue.Maximum = new decimal(new int[] { 4800, 0, 0, 0 });
            this.pwmCorrectValue.Name = "pwmCorrectValue";
            this.pwmCorrectValue.Value = new decimal(new int[] { 1400, 0, 0, 0 });
            this.currentValue.Anchor = AnchorStyles.Left;
            this.currentValue.Margin = new Padding(0, 7, 0, 0);
            this.currentValue.Size = new Size(190, 28);
            this.currentValue.Maximum = new decimal(new int[] { 1200, 0, 0, 0 });
            this.currentValue.Name = "currentValue";
            this.currentValue.Value = new decimal(new int[] { 800, 0, 0, 0 });
            // 
            // applyPowerButton
            // 
            this.applyPowerButton.Dock = DockStyle.Fill;
            this.applyPowerButton.Name = "applyPowerButton";
            this.applyPowerButton.Text = "设置功率";
            this.applyPowerButton.UseVisualStyleBackColor = true;
            this.applyPowerButton.Click += new EventHandler(this.ApplyPowerButton_Click);
            this.applyPwmButton.Dock = DockStyle.Fill;
            this.applyPwmButton.Name = "applyPwmButton";
            this.applyPwmButton.Text = "设置 PWM";
            this.applyPwmButton.UseVisualStyleBackColor = true;
            this.applyPwmButton.Click += new EventHandler(this.ApplyPwmButton_Click);
            this.applyPwmCorrectButton.Dock = DockStyle.Fill;
            this.applyPwmCorrectButton.Name = "applyPwmCorrectButton";
            this.applyPwmCorrectButton.Text = "设置校正";
            this.applyPwmCorrectButton.UseVisualStyleBackColor = true;
            this.applyPwmCorrectButton.Click += new EventHandler(this.ApplyPwmCorrectButton_Click);
            this.applyCurrentButton.Dock = DockStyle.Fill;
            this.applyCurrentButton.Name = "applyCurrentButton";
            this.applyCurrentButton.Text = "设置电流";
            this.applyCurrentButton.UseVisualStyleBackColor = true;
            this.applyCurrentButton.Click += new EventHandler(this.ApplyCurrentButton_Click);
            // 
            // powerPresetPanel
            // 
            this.powerPresetPanel.Name = "powerPresetPanel";
            this.powerPresetPanel.Dock = DockStyle.Fill;
            this.powerPresetPanel.TabIndex = 1;
            // 
            // pwmPresetPanel
            // 
            this.pwmPresetPanel.Name = "pwmPresetPanel";
            this.pwmPresetPanel.Dock = DockStyle.Fill;
            this.pwmPresetPanel.TabIndex = 2;
            // 
            // pwmCorrectPresetPanel
            // 
            this.pwmCorrectPresetPanel.Name = "pwmCorrectPresetPanel";
            this.pwmCorrectPresetPanel.Dock = DockStyle.Fill;
            this.pwmCorrectPresetPanel.TabIndex = 3;
            // 
            // currentPresetPanel
            // 
            this.currentPresetPanel.AutoScroll = true;
            this.currentPresetPanel.Name = "currentPresetPanel";
            this.currentPresetPanel.Dock = DockStyle.Fill;
            this.currentPresetPanel.TabIndex = 4;
            // 
            // statusGroupBox
            // 
            this.statusGroupBox.Controls.Add(this.statusLayout);
            this.statusGroupBox.Dock = DockStyle.Fill;
            this.statusGroupBox.Location = new Point(15, 521);
            this.statusGroupBox.Name = "statusGroupBox";
            this.statusGroupBox.Padding = new Padding(10);
            this.statusGroupBox.Size = new Size(1150, 184);
            this.statusGroupBox.TabIndex = 2;
            this.statusGroupBox.TabStop = false;
            this.statusGroupBox.Text = "激光器状态";
            // 
            // statusLayout
            // 
            this.statusLayout.ColumnCount = 1;
            this.statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            this.statusLayout.Controls.Add(this.statusActionPanel, 0, 0);
            this.statusLayout.Controls.Add(this.statusValuesLayout, 0, 1);
            this.statusLayout.Controls.Add(this.rawStatusLabel, 0, 2);
            this.statusLayout.Dock = DockStyle.Fill;
            this.statusLayout.Name = "statusLayout";
            this.statusLayout.RowCount = 3;
            this.statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            this.statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            this.statusLayout.TabIndex = 0;
            // 
            // statusActionPanel
            // 
            this.statusActionPanel.Controls.Add(this.refreshStatusButton);
            this.statusActionPanel.Controls.Add(this.autoRefreshCheckBox);
            this.statusActionPanel.Dock = DockStyle.Fill;
            this.statusActionPanel.Name = "statusActionPanel";
            this.statusActionPanel.TabIndex = 0;
            // 
            // refreshStatusButton
            // 
            this.refreshStatusButton.Name = "refreshStatusButton";
            this.refreshStatusButton.Size = new Size(90, 30);
            this.refreshStatusButton.TabIndex = 0;
            this.refreshStatusButton.Text = "刷新状态";
            this.refreshStatusButton.UseVisualStyleBackColor = true;
            this.refreshStatusButton.Click += new EventHandler(this.RefreshStatusButton_Click);
            // 
            // autoRefreshCheckBox
            // 
            this.autoRefreshCheckBox.AutoSize = true;
            this.autoRefreshCheckBox.Location = new Point(105, 7);
            this.autoRefreshCheckBox.Name = "autoRefreshCheckBox";
            this.autoRefreshCheckBox.Size = new Size(150, 24);
            this.autoRefreshCheckBox.TabIndex = 1;
            this.autoRefreshCheckBox.Text = "自动刷新 (1秒)";
            this.autoRefreshCheckBox.UseVisualStyleBackColor = true;
            // 
            // statusValuesLayout
            // 
            this.statusValuesLayout.ColumnCount = 9;
            this.statusValuesLayout.Dock = DockStyle.Fill;
            this.statusValuesLayout.RowCount = 2;
            this.statusValuesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            this.statusValuesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            for (int index = 0; index < 9; index++)
                this.statusValuesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.11111F));
            string[] captions = { "当前温度", "目标温度", "TEC/mA", "KP", "KI", "KD", "TMPGN", "LD/uA", "Power on" };
            Label[] values = { this.statusTemperatureValue, this.statusTargetTemperatureValue, this.statusTecCurrentValue, this.statusKpValue, this.statusKiValue, this.statusKdValue, this.statusTmpgnValue, this.statusLdCurrentValue, this.statusPowerOnValue };
            for (int index = 0; index < captions.Length; index++)
            {
                Label caption = new Label { Name = "statusCaption" + index, Text = captions[index], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
                values[index].Name = "statusValue" + index;
                values[index].Text = "--";
                values[index].Dock = DockStyle.Fill;
                values[index].TextAlign = ContentAlignment.MiddleCenter;
                values[index].BorderStyle = BorderStyle.FixedSingle;
                this.statusValuesLayout.Controls.Add(caption, index, 0);
                this.statusValuesLayout.Controls.Add(values[index], index, 1);
            }
            this.statusValuesLayout.Name = "statusValuesLayout";
            this.statusValuesLayout.TabIndex = 1;
            // 
            // rawStatusLabel
            // 
            this.rawStatusLabel.Dock = DockStyle.Fill;
            this.rawStatusLabel.ForeColor = Color.DimGray;
            this.rawStatusLabel.Name = "rawStatusLabel";
            this.rawStatusLabel.TabIndex = 2;
            this.rawStatusLabel.Text = "原始状态：--";
            this.rawStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // LaserSettingsForm
            // 
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ClientSize = new Size(1180, 720);
            this.Controls.Add(this.rootLayout);
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new Size(1080, 680);
            this.Name = "LaserSettingsForm";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "激光器设置";
            ((System.ComponentModel.ISupportInitialize)(this.periodValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.temperatureValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.powerValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pwmValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pwmCorrectValue)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.currentValue)).EndInit();
            this.ResumeLayout(false);
        }
    }
}
