using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MicroLaman
{
    /// <summary>
    /// 提供 Terra USB 激光器的非模态设置界面，所有设备访问均由 MainForm 统一加锁执行。
    /// </summary>
    internal sealed class LaserSettingsForm : Form
    {
        private Terra.Device laserDevice;
        private object deviceSync;
        private Action<bool, bool> stateChanged;
        private Action beforeLaserOutputEnabled;
        private readonly List<Control> commandControls = new List<Control>();
        private readonly Label[] statusValues = new Label[9];
        private readonly Timer refreshTimer = new Timer();
        private Label connectionLabel;
        private Label powerRangeLabel;
        private Label rawStatusLabel;
        private Button ldToggleButton;
        private Button tecToggleButton;
        private CheckBox autoRefreshCheckBox;
        private NumericUpDown periodValue;
        private NumericUpDown temperatureValue;
        private NumericUpDown powerValue;
        private NumericUpDown pwmValue;
        private NumericUpDown pwmCorrectValue;
        private NumericUpDown currentValue;
        private bool commandsAllowed = true;
        private bool commandRunning;
        private bool statusRefreshing;
        private bool laserOutputEnabled;
        private bool tecEnabled;

        /// <summary>
        /// 创建供 Visual Studio 设计器使用的无参窗体实例。
        /// </summary>
        public LaserSettingsForm()
        {
            InitializeComponent();
            InitializeLaserControls();
        }

        /// <summary>
        /// 使用已连接的 Terra 设备创建设置窗口并启动状态刷新计时器。
        /// </summary>
        internal LaserSettingsForm(
            Terra.Device device,
            object synchronizationRoot,
            Action<bool, bool> onStateChanged,
            Action onBeforeLaserOutputEnabled,
            bool initialLaserOutputEnabled,
            bool initialTecEnabled)
            : this()
        {
            laserDevice = device ?? throw new ArgumentNullException(nameof(device));
            deviceSync = synchronizationRoot ?? throw new ArgumentNullException(nameof(synchronizationRoot));
            stateChanged = onStateChanged;
            beforeLaserOutputEnabled = onBeforeLaserOutputEnabled;
            laserOutputEnabled = initialLaserOutputEnabled;
            tecEnabled = initialTecEnabled;
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();
            RefreshDeviceState();
        }

        /// <summary>
        /// 创建窗口的开关、温控、功率、电流和状态区域。
        /// </summary>
        private void InitializeLaserControls()
        {
            Text = "激光器设置";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1080, 680);
            ClientSize = new Size(1180, 720);
            // 默认占当前屏幕工作区的 80%，保持普通可缩放窗口。
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size((int)(workingArea.Width * 0.80), (int)(workingArea.Height * 0.80));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
            Controls.Add(root);

            connectionLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold)
            };
            root.Controls.Add(connectionLabel, 0, 0);

            TableLayoutPanel settings = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 4, 0, 8)
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            settings.Controls.Add(CreateSwitchAndTemperatureGroup(), 0, 0);
            settings.Controls.Add(CreatePowerAndCurrentGroup(), 1, 0);
            root.Controls.Add(settings, 0, 1);
            root.Controls.Add(CreateStatusGroup(), 0, 2);
        }

        /// <summary>
        /// 创建 LD、TEC、全开关、激光周期和 TEC 目标温度控件。
        /// </summary>
        private GroupBox CreateSwitchAndTemperatureGroup()
        {
            GroupBox group = new GroupBox { Text = "开关与温控", Dock = DockStyle.Fill, Padding = new Padding(12) };
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            group.Controls.Add(panel);

            FlowLayoutPanel switches = CreateHorizontalPanel();
            switches.Controls.Add(CreateCommandButton("全部开启", async () =>
                await ExecuteCommandAsync("开启激光器全部状态", () => SetAllOutputs(true))));
            switches.Controls.Add(CreateCommandButton("全部关闭", async () =>
                await ExecuteCommandAsync("关闭激光器全部状态", () => SetAllOutputs(false))));
            ldToggleButton = CreateCommandButton("LD：关", async () =>
                await ExecuteCommandAsync("切换 LD", () => SetLaserOutput(!LaserOutputEnabled)));
            tecToggleButton = CreateCommandButton("TEC：关", async () =>
                await ExecuteCommandAsync("切换 TEC", () => SetTecOutput(!TecEnabled)));
            switches.Controls.Add(ldToggleButton);
            switches.Controls.Add(tecToggleButton);
            panel.Controls.Add(switches);

            periodValue = CreateNumeric(0, 10000, 500);
            panel.Controls.Add(CreateSettingPanel(
                "激光开关半周期 (ms)",
                periodValue,
                "应用",
                value => ExecuteDeviceCommand(device => device.setLaserPeriod(value)),
                new[] { 10, 100, 1000, 10000 }));

            temperatureValue = CreateNumeric(-20, 60, 25);
            panel.Controls.Add(CreateSettingPanel(
                "TEC 目标温度 (°C)",
                temperatureValue,
                "应用",
                value => ExecuteDeviceCommand(device => device.setTECTemperature(value)),
                new[] { -10, 0, 25, 35 }));

            powerRangeLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(8, 18, 8, 8),
                ForeColor = Color.DimGray,
                Text = "激光功率范围：等待设备信息"
            };
            panel.Controls.Add(powerRangeLabel);
            return group;
        }

        /// <summary>
        /// 创建激光功率、PWM、PWM 校正和电流设置控件。
        /// </summary>
        private GroupBox CreatePowerAndCurrentGroup()
        {
            GroupBox group = new GroupBox { Text = "功率与电流", Dock = DockStyle.Fill, Padding = new Padding(12) };
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            group.Controls.Add(panel);

            powerValue = CreateNumeric(0, 1000, 100);
            panel.Controls.Add(CreateSettingPanel(
                "激光功率 (mW)",
                powerValue,
                "设置功率",
                value => ExecuteDeviceCommand(device => device.setLaserPower(value)),
                new[] { 5, 50, 100, 200, 300, 400, 500 }));

            pwmValue = CreateNumeric(0, 4800, 3000);
            panel.Controls.Add(CreateSettingPanel(
                "激光功率 PWM",
                pwmValue,
                "设置 PWM",
                value => ExecuteDeviceCommand(device => device.setLaserPWM(value)),
                new[] { 700, 1400, 2100, 2800, 3500, 4200, 4800 }));

            pwmCorrectValue = CreateNumeric(0, 4800, 3000);
            panel.Controls.Add(CreateSettingPanel(
                "PWM 功率校正",
                pwmCorrectValue,
                "设置校正",
                value => ExecuteDeviceCommand(device => device.setLaserPWMCorrect(value)),
                new[] { 700, 1400, 2100, 2800, 3500, 4200, 4800 }));

            currentValue = CreateNumeric(0, 1200, 1000);
            panel.Controls.Add(CreateSettingPanel(
                "激光电流 (mA)",
                currentValue,
                "设置电流",
                value => ExecuteDeviceCommand(device => device.setLaserCurrent(value)),
                new[]
                {
                    30, 40, 50, 60, 70,
                    80, 90, 100, 110, 120,
                    300, 400, 500, 600, 700,
                    800, 900, 1000, 1100, 1200
                }));
            return group;
        }

        /// <summary>
        /// 创建激光器状态读取、自动刷新和原始数据展示区域。
        /// </summary>
        private GroupBox CreateStatusGroup()
        {
            GroupBox group = new GroupBox { Text = "激光器状态", Dock = DockStyle.Fill, Padding = new Padding(10) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            group.Controls.Add(layout);

            FlowLayoutPanel actions = CreateHorizontalPanel();
            Button refreshButton = new Button { Text = "刷新状态", AutoSize = true };
            refreshButton.Click += async (sender, args) => await RefreshStatusAsync(true);
            autoRefreshCheckBox = new CheckBox { Text = "自动刷新 (1秒)", Checked = false, AutoSize = true, Margin = new Padding(15, 7, 5, 5) };
            actions.Controls.Add(refreshButton);
            actions.Controls.Add(autoRefreshCheckBox);
            layout.Controls.Add(actions, 0, 0);

            string[] names = { "当前温度", "目标温度", "TEC/mA", "KP", "KI", "KD", "TMPGN", "LD/uA", "Power on" };
            TableLayoutPanel values = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = names.Length, RowCount = 2 };
            for (int index = 0; index < names.Length; index++)
            {
                values.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / names.Length));
                values.Controls.Add(new Label { Text = names[index], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter }, index, 0);
                statusValues[index] = new Label
                {
                    Text = "--",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    BorderStyle = BorderStyle.FixedSingle
                };
                values.Controls.Add(statusValues[index], index, 1);
            }
            layout.Controls.Add(values, 0, 1);

            rawStatusLabel = new Label { Text = "原始状态：--", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            layout.Controls.Add(rawStatusLabel, 0, 2);
            return group;
        }

        /// <summary>
        /// 创建包含标题、数值框、应用按钮和快捷值按钮的设置行。
        /// </summary>
        private Control CreateSettingPanel(
            string title,
            NumericUpDown numeric,
            string applyText,
            Func<int, bool> command,
            int[] presets)
        {
            FlowLayoutPanel container = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(8, 8, 8, 4)
            };
            FlowLayoutPanel editor = CreateHorizontalPanel();
            editor.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(3, 8, 8, 3) });
            editor.Controls.Add(numeric);
            editor.Controls.Add(CreateCommandButton(applyText, async () =>
                await ExecuteCommandAsync(title, () => command((int)numeric.Value))));
            container.Controls.Add(editor);

            FlowLayoutPanel presetPanel = CreateHorizontalPanel();
            foreach (int preset in presets)
            {
                Button presetButton = CreateCommandButton(FormatPreset(title, preset), async () =>
                {
                    decimal bounded = Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, preset));
                    numeric.Value = bounded;
                    await ExecuteCommandAsync(title, () => command((int)bounded));
                });
                presetPanel.Controls.Add(presetButton);
            }
            container.Controls.Add(presetPanel);
            return container;
        }

        /// <summary>
        /// 根据设置类型为快捷值添加便于识别的单位。
        /// </summary>
        private static string FormatPreset(string title, int value)
        {
            if (title.IndexOf("温度", StringComparison.Ordinal) >= 0)
                return value + "°C";
            if (title.IndexOf("半周期", StringComparison.Ordinal) >= 0)
                return value >= 1000 ? (value / 1000) + "s" : value + "ms";
            if (title.IndexOf("电流", StringComparison.Ordinal) >= 0)
                return value + "mA";
            if (title.IndexOf("(mW)", StringComparison.Ordinal) >= 0)
                return value + "mW";
            return value.ToString();
        }

        /// <summary>
        /// 创建统一尺寸并登记到命令禁用列表的按钮。
        /// </summary>
        private Button CreateCommandButton(string text, Func<Task> action)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(82, 30), Margin = new Padding(4) };
            button.Click += async (sender, args) => await action();
            commandControls.Add(button);
            return button;
        }

        /// <summary>
        /// 创建横向自动尺寸容器。
        /// </summary>
        private static FlowLayoutPanel CreateHorizontalPanel()
        {
            return new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(4)
            };
        }

        /// <summary>
        /// 创建具有指定范围和默认值的整数输入框。
        /// </summary>
        private NumericUpDown CreateNumeric(int minimum, int maximum, int value)
        {
            NumericUpDown numeric = new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
                Width = 90,
                Margin = new Padding(4)
            };
            commandControls.Add(numeric);
            return numeric;
        }

        /// <summary>
        /// 获取当前窗口是否仍持有可用的 Terra 激光器设备。
        /// </summary>
        private bool IsDeviceConnected
        {
            get
            {
                if (laserDevice == null || deviceSync == null)
                    return false;
                lock (deviceSync)
                {
                    try { return laserDevice != null && laserDevice.isUsbConnected(); }
                    catch { return false; }
                }
            }
        }

        /// <summary>
        /// 获取软件记录的 LD 输出状态。
        /// </summary>
        internal bool LaserOutputEnabled { get { return laserOutputEnabled; } }

        /// <summary>
        /// 获取软件记录的 TEC 输出状态。
        /// </summary>
        internal bool TecEnabled { get { return tecEnabled; } }

        /// <summary>
        /// 在共享设备锁内执行一条 Terra 设置指令。
        /// </summary>
        private bool ExecuteDeviceCommand(Func<Terra.Device, bool> command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (deviceSync == null)
                throw new InvalidOperationException("激光器尚未连接。");

            lock (deviceSync)
            {
                if (laserDevice == null || !laserDevice.isUsbConnected())
                    throw new InvalidOperationException("激光器尚未连接。");
                return command(laserDevice);
            }
        }

        /// <summary>
        /// 切换 LD 输出，并在设备确认成功后同步窗口和 MainForm 状态。
        /// </summary>
        private bool SetLaserOutput(bool enabled)
        {
            if (enabled && !laserOutputEnabled)
                CaptureBrightFieldBeforeLaserOutput();
            bool success = ExecuteDeviceCommand(device => enabled ? device.setLDOn() : device.setLDOff());
            if (success)
            {
                laserOutputEnabled = enabled;
                NotifyStateChanged();
            }
            return success;
        }

        /// <summary>
        /// 切换 TEC 输出，并在设备确认成功后同步窗口和 MainForm 状态。
        /// </summary>
        private bool SetTecOutput(bool enabled)
        {
            bool success = ExecuteDeviceCommand(device => enabled ? device.setTECOn() : device.setTECOff());
            if (success)
            {
                tecEnabled = enabled;
                NotifyStateChanged();
            }
            return success;
        }

        /// <summary>
        /// 按安全顺序切换全部输出：开启时先 TEC 后 LD，关闭时先 LD 后 TEC。
        /// </summary>
        private bool SetAllOutputs(bool enabled)
        {
            if (deviceSync == null)
                throw new InvalidOperationException("激光器尚未连接。");

            bool success;
            lock (deviceSync)
            {
                if (laserDevice == null || !laserDevice.isUsbConnected())
                    throw new InvalidOperationException("激光器尚未连接。");

                if (enabled)
                {
                    // 全部开启必须先建立 TEC 制冷，再允许 LD 发光。
                    success = laserDevice.setTECOn();
                    if (success)
                    {
                        tecEnabled = true;
                        CaptureBrightFieldBeforeLaserOutput();
                        success = laserDevice.setLDOn();
                        if (success)
                            laserOutputEnabled = true;
                    }
                }
                else
                {
                    // 全部关闭必须先停止 LD 发光，再关闭 TEC 制冷。
                    success = laserDevice.setLDOff();
                    if (success)
                    {
                        laserOutputEnabled = false;
                        success = laserDevice.setTECOff();
                        if (success)
                            tecEnabled = false;
                    }
                }
            }

            NotifyStateChanged();
            return success;
        }

        /// <summary>
        /// 将当前 LD 与 TEC 状态通知 MainForm，供定标安全检查使用。
        /// </summary>
        private void NotifyStateChanged()
        {
            Action<bool, bool> callback = stateChanged;
            if (callback != null)
                callback(laserOutputEnabled, tecEnabled);
        }

        /// <summary>在 LD 开启命令之前保存最后一张明场图，供主窗口检测区使用。</summary>
        private void CaptureBrightFieldBeforeLaserOutput()
        {
            Action callback = beforeLaserOutputEnabled;
            if (callback != null)
                callback();
        }

        /// <summary>
        /// 读取 Terra SDK 报告的最大激光功率，无有效值时使用 1000 mW。
        /// </summary>
        private int GetMaximumPower()
        {
            if (deviceSync == null)
                return 1000;
            lock (deviceSync)
            {
                return laserDevice != null && laserDevice.maxLaserPower > 0
                    ? laserDevice.maxLaserPower
                    : 1000;
            }
        }

        /// <summary>
        /// 在共享设备锁内读取激光器原始状态字节。
        /// </summary>
        private byte[] ReadDeviceStatus()
        {
            if (deviceSync == null)
                throw new InvalidOperationException("激光器尚未连接。");
            lock (deviceSync)
            {
                if (laserDevice == null || !laserDevice.isUsbConnected())
                    throw new InvalidOperationException("激光器尚未连接。");
                return laserDevice.readLaserState();
            }
        }

        /// <summary>
        /// 在后台执行一条 Terra 设置命令，并统一处理禁用状态和错误提示。
        /// </summary>
        private async Task ExecuteCommandAsync(string operation, Func<bool> command)
        {
            if (commandRunning || !IsDeviceConnected)
                return;

            commandRunning = true;
            ApplyCommandAvailability();
            try
            {
                bool success = await Task.Run(command);
                if (!success)
                    throw new InvalidOperationException(operation + "失败，Terra SDK 返回 false。");
                RefreshDeviceState();
                await RefreshStatusAsync(false);
            }
            catch (Exception ex)
            {
                RefreshDeviceState();
                MessageBox.Show(this, ex.Message, operation, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                commandRunning = false;
                ApplyCommandAvailability();
            }
        }

        /// <summary>
        /// 设置外部操作期间激光器命令控件是否允许使用。
        /// </summary>
        internal void SetDeviceCommandsEnabled(bool enabled)
        {
            commandsAllowed = enabled;
            ApplyCommandAvailability();
        }

        /// <summary>
        /// 根据连接状态、运行状态和主窗口限制更新全部命令控件。
        /// </summary>
        private void ApplyCommandAvailability()
        {
            bool enabled = commandsAllowed && !commandRunning && IsDeviceConnected;
            foreach (Control control in commandControls)
                control.Enabled = enabled;
        }

        /// <summary>
        /// 刷新连接提示、LD/TEC 按钮状态和设备功率范围。
        /// </summary>
        internal void RefreshDeviceState()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshDeviceState));
                return;
            }

            bool connected = IsDeviceConnected;
            connectionLabel.Text = connected ? "激光器：已连接" : "激光器：未连接";
            connectionLabel.ForeColor = connected ? Color.ForestGreen : Color.Firebrick;
            ldToggleButton.Text = laserOutputEnabled ? "LD：开" : "LD：关";
            ldToggleButton.BackColor = laserOutputEnabled ? Color.LightGreen : SystemColors.Control;
            tecToggleButton.Text = tecEnabled ? "TEC：开" : "TEC：关";
            tecToggleButton.BackColor = tecEnabled ? Color.LightGreen : SystemColors.Control;

            int maximumPower = GetMaximumPower();
            powerValue.Maximum = Math.Max(1, maximumPower);
            if (powerValue.Value > powerValue.Maximum)
                powerValue.Value = powerValue.Maximum;
            powerRangeLabel.Text = string.Format("Terra SDK 激光功率范围：0 - {0} mW", maximumPower);
            ApplyCommandAvailability();
        }

        /// <summary>
        /// 自动刷新计时器到期时，在用户启用自动刷新后读取设备状态。
        /// </summary>
        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (autoRefreshCheckBox.Checked)
                await RefreshStatusAsync(false);
        }

        /// <summary>
        /// 从 Terra SDK 读取原始状态字节并更新九项状态显示。
        /// </summary>
        private async Task RefreshStatusAsync(bool showErrors)
        {
            if (statusRefreshing || commandRunning || !IsDeviceConnected)
                return;

            statusRefreshing = true;
            try
            {
                byte[] state = await Task.Run(() => ReadDeviceStatus());
                DisplayLaserStatus(state);
            }
            catch (Exception ex)
            {
                if (showErrors)
                    MessageBox.Show(this, ex.Message, "读取激光器状态", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                statusRefreshing = false;
            }
        }

        /// <summary>
        /// 兼容显示 Terra 返回的状态数据；18 字节及以上按九个大端 16 位字段解析。
        /// </summary>
        private void DisplayLaserStatus(byte[] state)
        {
            if (state == null || state.Length == 0)
            {
                rawStatusLabel.Text = "原始状态：未返回数据";
                return;
            }

            rawStatusLabel.Text = "原始状态：" + BitConverter.ToString(state);
            int fieldCount = Math.Min(statusValues.Length, state.Length / 2);
            int start = Math.Max(0, state.Length - fieldCount * 2);
            for (int index = 0; index < statusValues.Length; index++)
            {
                if (index >= fieldCount)
                {
                    statusValues[index].Text = "--";
                    continue;
                }

                int value = state[start + index * 2] * 256 + state[start + index * 2 + 1];
                statusValues[index].Text = index == statusValues.Length - 1
                    ? (value == 0 ? "Off" : "On")
                    : value.ToString();
            }
        }

        /// <summary>
        /// 关闭设置窗口时停止状态刷新计时器。
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // LaserSettingsForm
            // 
            this.ClientSize = new System.Drawing.Size(1374, 847);
            this.Name = "LaserSettingsForm";
            this.ResumeLayout(false);

        }
    }
}
