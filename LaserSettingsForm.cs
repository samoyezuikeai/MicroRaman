using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MicroRaman
{
    /// <summary>
    /// 提供 Terra USB 激光器的非模态设置界面，所有设备访问均由 MainForm 统一加锁执行。
    /// </summary>
    public sealed partial class LaserSettingsForm : Form
    {
        private Terra.Device laserDevice;
        private object deviceSync;
        private Action<bool, bool> stateChanged;
        private readonly List<Control> commandControls = new List<Control>();
        private readonly Label[] statusValues = new Label[9];
        private readonly Timer refreshTimer = new Timer();
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
            if (!IsDesignTime)
                InitializeLaserControlBehavior();
        }

        private static bool IsDesignTime
        {
            get
            {
                return LicenseManager.UsageMode == LicenseUsageMode.Designtime
                    || (System.Diagnostics.Process.GetCurrentProcess().ProcessName ?? string.Empty)
                        .IndexOf("designer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>
        /// 使用已连接的 Terra 设备创建设置窗口并启动状态刷新计时器。
        /// </summary>
        internal LaserSettingsForm(
            Terra.Device device,
            object synchronizationRoot,
            Action<bool, bool> onStateChanged,
            bool initialLaserOutputEnabled,
            bool initialTecEnabled)
            : this()
        {
            laserDevice = device ?? throw new ArgumentNullException(nameof(device));
            deviceSync = synchronizationRoot ?? throw new ArgumentNullException(nameof(synchronizationRoot));
            stateChanged = onStateChanged;
            laserOutputEnabled = initialLaserOutputEnabled;
            tecEnabled = initialTecEnabled;
            refreshTimer.Interval = 1000;
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();
            RefreshDeviceState();
        }

        /// <summary>
        /// 初始化由 Designer 固定声明的控件与运行时状态数组。
        /// </summary>
        private void InitializeLaserControlBehavior()
        {
            commandControls.AddRange(new Control[]
            {
                allOnButton, allOffButton, ldToggleButton, tecToggleButton,
                periodValue, applyPeriodButton, temperatureValue, applyTemperatureButton,
                powerValue, applyPowerButton, pwmValue, applyPwmButton,
                pwmCorrectValue, applyPwmCorrectButton, currentValue, applyCurrentButton
            });

            statusValues[0] = statusTemperatureValue;
            statusValues[1] = statusTargetTemperatureValue;
            statusValues[2] = statusTecCurrentValue;
            statusValues[3] = statusKpValue;
            statusValues[4] = statusKiValue;
            statusValues[5] = statusKdValue;
            statusValues[6] = statusTmpgnValue;
            statusValues[7] = statusLdCurrentValue;
            statusValues[8] = statusPowerOnValue;

            AddPresetButtons(periodPresetPanel, periodValue, new[] { 10, 100, 1000, 10000 }, "ms", () =>
                ExecuteDeviceCommand(device => device.setLaserPeriod((int)periodValue.Value)));
            AddPresetButtons(temperaturePresetPanel, temperatureValue, new[] { -10, 0, 25, 35 }, "°C", () =>
                ExecuteDeviceCommand(device => device.setTECTemperature((int)temperatureValue.Value)));
            AddPresetButtons(powerPresetPanel, powerValue, new[] { 5, 50, 100, 200, 300, 400, 500 }, "mW", () =>
                ExecuteDeviceCommand(device => device.setLaserPower((int)powerValue.Value)));
            AddPresetButtons(pwmPresetPanel, pwmValue, new[] { 700, 1400, 2100, 2800, 3500, 4200, 4800 }, string.Empty, () =>
                ExecuteDeviceCommand(device => device.setLaserPWM((int)pwmValue.Value)));
            AddPresetButtons(pwmCorrectPresetPanel, pwmCorrectValue, new[] { 700, 1400, 2100, 2800, 3500, 4200, 4800 }, string.Empty, () =>
                ExecuteDeviceCommand(device => device.setLaserPWMCorrect((int)pwmCorrectValue.Value)));
            AddPresetButtons(currentPresetPanel, currentValue, new[]
            {
                30, 40, 50, 60, 70, 80, 90, 100, 110, 120,
                300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200
            }, "mA", () => ExecuteDeviceCommand(device => device.setLaserCurrent((int)currentValue.Value)));
        }

        /// <summary>
        /// 执行 AddPresetButtons 相关的内部处理。
        /// </summary>
        private void AddPresetButtons(
            FlowLayoutPanel panel,
            NumericUpDown numeric,
            int[] values,
            string suffix,
            Func<bool> command)
        {
            foreach (int value in values)
            {
                Button button = new Button
                {
                    AutoSize = true,
                    Margin = new Padding(2),
                    Name = panel.Name + "Preset" + value,
                    Text = value + suffix,
                    Tag = value
                };
                button.Click += async (sender, args) =>
                {
                    decimal bounded = Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, value));
                    numeric.Value = bounded;
                    await ExecuteCommandAsync(numeric.AccessibleName ?? "应用预设", command);
                };
                panel.Controls.Add(button);
                commandControls.Add(button);
            }
        }

        /// <summary>
        /// 处理 AllOnButton_Click 触发的界面事件。
        /// </summary>
        private async void AllOnButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("开启激光器全部状态", () => SetAllOutputs(true));
        }

        /// <summary>
        /// 处理 AllOffButton_Click 触发的界面事件。
        /// </summary>
        private async void AllOffButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("关闭激光器全部状态", () => SetAllOutputs(false));
        }

        /// <summary>
        /// 处理 LdToggleButton_Click 触发的界面事件。
        /// </summary>
        private async void LdToggleButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("切换 LD", () => SetLaserOutput(!LaserOutputEnabled));
        }

        /// <summary>
        /// 处理 TecToggleButton_Click 触发的界面事件。
        /// </summary>
        private async void TecToggleButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("切换 TEC", () => SetTecOutput(!TecEnabled));
        }

        /// <summary>
        /// 处理 ApplyPeriodButton_Click 触发的界面事件。
        /// </summary>
        private async void ApplyPeriodButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("激光开关半周期", () => ExecuteDeviceCommand(device => device.setLaserPeriod((int)periodValue.Value)));
        }

        /// <summary>
        /// 处理 ApplyTemperatureButton_Click 触发的界面事件。
        /// </summary>
        private async void ApplyTemperatureButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("TEC 目标温度", () => ExecuteDeviceCommand(device => device.setTECTemperature((int)temperatureValue.Value)));
        }

        /// <summary>
        /// 处理 ApplyPowerButton_Click 触发的界面事件。
        /// </summary>
        private async void ApplyPowerButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("激光功率", () => ExecuteDeviceCommand(device => device.setLaserPower((int)powerValue.Value)));
        }

        /// <summary>
        /// 处理 ApplyPwmButton_Click 触发的界面事件。
        /// </summary>
        private async void ApplyPwmButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("激光功率 PWM", () => ExecuteDeviceCommand(device => device.setLaserPWM((int)pwmValue.Value)));
        }

        /// <summary>
        /// 处理 ApplyPwmCorrectButton_Click 触发的界面事件。
        /// </summary>
        private async void ApplyPwmCorrectButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("PWM 功率校正", () => ExecuteDeviceCommand(device => device.setLaserPWMCorrect((int)pwmCorrectValue.Value)));
        }

        /// <summary>
        /// 处理 ApplyCurrentButton_Click 触发的界面事件。
        /// </summary>
        private async void ApplyCurrentButton_Click(object sender, EventArgs e)
        {
            await ExecuteCommandAsync("激光电流", () => ExecuteDeviceCommand(device => device.setLaserCurrent((int)currentValue.Value)));
        }

        /// <summary>
        /// 处理 RefreshStatusButton_Click 触发的界面事件。
        /// </summary>
        private async void RefreshStatusButton_Click(object sender, EventArgs e)
        {
            await RefreshStatusAsync(true);
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
                bool success = command(laserDevice);
                // THBD 被 Terra SDK 归类为 Others，控制命令会正常发送但固定返回 false。
                return success || laserDevice.GetType().FullName == "Terra.Others";
            }
        }

        /// <summary>
        /// 切换 LD 输出，并在设备确认成功后同步窗口和 MainForm 状态。
        /// </summary>
        private bool SetLaserOutput(bool enabled)
        {
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
                    success = IsAcceptedCommandResult(laserDevice, laserDevice.setTECOn());
                    if (success)
                    {
                        tecEnabled = true;
                        success = IsAcceptedCommandResult(laserDevice, laserDevice.setLDOn());
                        if (success)
                            laserOutputEnabled = true;
                    }
                }
                else
                {
                    // 全部关闭必须先停止 LD 发光，再关闭 TEC 制冷。
                    success = IsAcceptedCommandResult(laserDevice, laserDevice.setLDOff());
                    if (success)
                    {
                        laserOutputEnabled = false;
                        success = IsAcceptedCommandResult(laserDevice, laserDevice.setTECOff());
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

        /// <summary>
        /// 判断AcceptedCommandResult相关的内部处理。
        /// </summary>
        private static bool IsAcceptedCommandResult(Terra.Device device, bool success)
        {
            return success || (device != null && device.GetType().FullName == "Terra.Others");
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
        /// 同步自动扫描直接切换的 LD 状态，避免设置窗口显示旧状态。
        /// </summary>
        internal void SetLaserOutputStateFromScan(bool enabled)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<bool>(SetLaserOutputStateFromScan), enabled); }
                catch (InvalidOperationException) { }
                return;
            }

            laserOutputEnabled = enabled;
            RefreshDeviceState();
        }

        /// <summary>
        /// 同步自动扫描直接切换的 TEC 状态，避免设置窗口显示旧状态。
        /// </summary>
        internal void SetTecOutputStateFromScan(bool enabled)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<bool>(SetTecOutputStateFromScan), enabled); }
                catch (InvalidOperationException) { }
                return;
            }

            tecEnabled = enabled;
            RefreshDeviceState();
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

    }
}

