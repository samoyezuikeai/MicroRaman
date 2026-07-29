using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Terra;

namespace MicroLaman
{
    /// <summary>
    /// 应用程序主窗口，负责串口连接、相机窗口管理和扫描任务调度。
    /// </summary>
    public partial class MainForm : Form
    {
        private CameraShowForm cameraShowForm;
        private LaserSettingsForm laserSettingsForm;
        private CancellationTokenSource scanCancellation;
        private CancellationTokenSource calibrationCancellation;
        private CancellationTokenSource realtimeSpectrumCancellation;
        private Task realtimeSpectrumTask;
        private DateTime scanStartedUtc = DateTime.MinValue;
        private static readonly TimeSpan ScanDoubleClickGuard = TimeSpan.FromMilliseconds(800);
        // GODZILLA 配套示例的界面默认值为 100 ms；用户可在主窗口中修改并应用。
        private const double DefaultSpectrometerIntegrationTimeMilliseconds = 100.0;
        private readonly StageScanController stageScanController = new StageScanController();
        private readonly object laserDeviceSync = new object();
        private readonly object spectrometerDeviceSync = new object();
        private Terra.Device laserDevice;
        private Terra.Device spectrometerDevice;
        private double spectrometerIntegrationTimeMilliseconds = DefaultSpectrometerIntegrationTimeMilliseconds;
        private double spectrometerMinimumIntegrationTimeMilliseconds;
        private double spectrometerMaximumIntegrationTimeMilliseconds;
        private readonly object savedSpectrumSync = new object();
        private readonly Dictionary<int, RamanSpectrum> laserOnSpectra =
            new Dictionary<int, RamanSpectrum>();
        private readonly object darkSpectrumSync = new object();
        // 手动“采集暗谱”保存的背景用于实时光谱；扫描时每个点单独采集背景，不复用这里的数据。
        private double[] savedDarkSpectrum;
        private readonly Dictionary<int, double[]> scanDarkSpectra =
            new Dictionary<int, double[]>();
        private bool laserEnabled;
        private bool tecEnabled;

        /// <summary>用于回看单个扫描点的开激光后拉曼数据。</summary>
        private sealed class RamanSpectrum
        {
            internal RamanSpectrum(double[] ramanShifts, double[] intensities)
            {
                RamanShifts = ramanShifts;
                Intensities = intensities;
            }

            internal double[] RamanShifts { get; private set; }
            internal double[] Intensities { get; private set; }
        }

        /// <summary>
        /// 初始化主窗口并加载当前可用串口。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            spectrometerIntegrationTimeTextBox.Text = FormatIntegrationTime(DefaultSpectrometerIntegrationTimeMilliseconds);
            InitializeSpectrumPlot();
            scanMatrixPreviewControl.ScanPointSelected += ScanMatrixPreviewControl_ScanPointSelected;
            // 默认占当前屏幕工作区的 80%，保持普通可缩放窗口。
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size((int)(workingArea.Width * 0.80), (int)(workingArea.Height * 0.80));
            StartPosition = FormStartPosition.CenterScreen;
            Resize += MainForm_Resize;
            LayoutBrightFieldPreviewArea();
            RefreshComList();
            UpdateRealtimeSpectrumButtonState();
        }

        /// <summary>
        /// 刷新串口列表，支持控制器热插拔。
        /// </summary>
        private void RefreshComList()
        {
            comboBoxController.Items.Clear();

            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = Convert.ToString(obj["Name"]);
                    if (string.IsNullOrEmpty(name) || name.StartsWith("蓝牙", StringComparison.Ordinal))
                        continue;
                    int start = name.LastIndexOf("(COM");
                    int end = name.LastIndexOf(")");
                    if (start < 0 || end <= start)
                        continue;
                    string com = name.Substring(start + 1, end - start - 1);
                    comboBoxController.Items.Add(com);
                }
            }
        }

        /// <summary>
        /// 响应刷新按钮，重新枚举系统串口。
        /// </summary>
        private void RefreshMyComList_Click(object sender, EventArgs e)
        {
            RefreshComList();
        }

        /// <summary>
        /// 连接选中的 TANGO 串口，并自动连接 Terra SDK 支持的 USB 激光器和光谱仪。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ConnectCom_Click(object sender, EventArgs e)
        {
            if (comboBoxController.SelectedItem == null)
            {
                MessageBox.Show(this, "请选择控制台串口。激光器和光谱仪将通过 Terra SDK 自动检测。", "连接设备",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string controllerPort = comboBoxController.SelectedItem.ToString();
            CloseLaserSettingsWindow();
            ConnectCom.Enabled = false;
            SetLaserControlsEnabled(false);
            try
            {
                await Task.Run(() => ConnectDevices(controllerPort));
                stageScanController.ResetOrigin();
                UpdateDeviceConnectionAppearance();
                SetLaserControlsEnabled(true);
                MessageBox.Show(this, "TANGO 控制台、USB 激光器和光谱仪均已连接。", "连接设备",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                DisconnectDevices();
                UpdateDeviceConnectionAppearance();
                MessageBox.Show(this, ex.Message, "设备连接失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ConnectCom.Enabled = true;
            }
        }

        /// <summary>
        /// 依次连接并验证 TANGO 串口、Terra USB 激光器和光谱仪；任意一步失败都会回滚全部连接。
        /// </summary>
        private void ConnectDevices(string controllerPort)
        {
            lock (laserDeviceSync)
            {
                DisconnectDevices();
                SerialPortManager.Open(controllerPort);
                try
                {
                    // 打开串口不代表选中的设备一定是 TANGO，使用实际查询确认通信正常。
                    new Command().ReadDimensions();

                    List<Terra.Device> devices = Terra.DeviceWrapper.openAndReadAllDevices();
                    if (devices == null || devices.Count < 2)
                        throw new InvalidOperationException(
                            "Terra SDK 未同时发现 THBD 激光器和 GODZILLA 光谱仪，请检查两台设备的 USB 连接。");

                    // 已由实际硬件验证：THBD 激光器是 devices[0]，GODZILLA 光谱仪是 devices[1]。
                    Terra.Device connectedLaser = devices[0];
                    Terra.Device connectedSpectrometer = devices[1];
                    if (!connectedLaser.isUsbConnected())
                        throw new InvalidOperationException("THBD 激光器 USB 尚未连接。");
                    if (!connectedSpectrometer.isUsbConnected())
                        throw new InvalidOperationException("GODZILLA 光谱仪 USB 尚未连接。");

                    double minimumIntegrationTime = connectedSpectrometer.getMinIntegrationTime();
                    double maximumIntegrationTime = connectedSpectrometer.getMaxIntegrationTime();
                    if (minimumIntegrationTime <= 0 || maximumIntegrationTime < minimumIntegrationTime)
                        throw new InvalidOperationException("Terra SDK 未返回有效的光谱仪积分时间范围。");

                    double requestedIntegrationTime = ClampIntegrationTime(
                        spectrometerIntegrationTimeMilliseconds,
                        minimumIntegrationTime,
                        maximumIntegrationTime);
                    connectedSpectrometer.setIntegrationTime(requestedIntegrationTime);
                    connectedLaser.setLDOff();
                    connectedLaser.setTECOff();
                    laserDevice = connectedLaser;
                    spectrometerDevice = connectedSpectrometer;
                    spectrometerMinimumIntegrationTimeMilliseconds = minimumIntegrationTime;
                    spectrometerMaximumIntegrationTimeMilliseconds = maximumIntegrationTime;
                    spectrometerIntegrationTimeMilliseconds = requestedIntegrationTime;
                    laserEnabled = false;
                    tecEnabled = false;
                }
                catch
                {
                    Terra.DeviceWrapper.closeAllDevices();
                    SerialPortManager.Close();
                    throw;
                }
            }
        }

        /// <summary>
        /// THBD 在 Terra SDK 中以 Others 类型出现，控制命令成功发送时也可能固定返回 false。
        /// </summary>
        private static bool IsThbdLaser(Terra.Device device)
        {
            return device != null && device.GetType().FullName == "Terra.Others";
        }

        /// <summary>
        /// 打开非模态激光器设置窗口；窗口打开后主窗口仍可继续操作。
        /// </summary>
        private void LaserSettings_Click(object sender, EventArgs e)
        {
            if (laserDevice == null) return;

            if (laserSettingsForm == null || laserSettingsForm.IsDisposed)
            {
                Terra.Device device;
                lock (laserDeviceSync) device = laserDevice;
                if (device == null) return;

                laserSettingsForm = new LaserSettingsForm(
                    device,
                    laserDeviceSync,
                    UpdateLaserStates,
                    laserEnabled,
                    tecEnabled);
                laserSettingsForm.FormClosed += LaserSettingsForm_FormClosed;
                laserSettingsForm.Show(this);
                return;
            }

            if (laserSettingsForm.WindowState == FormWindowState.Minimized)
                laserSettingsForm.WindowState = FormWindowState.Normal;
            laserSettingsForm.Activate();
        }

        /// <summary>
        /// 激光器设置窗口关闭后清除引用，允许下次重新创建。
        /// </summary>
        private void LaserSettingsForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            laserSettingsForm = null;
        }

        /// <summary>
        /// 接收设置窗口确认后的 LD 与 TEC 状态，供明场定标安全检查使用。
        /// </summary>
        private void UpdateLaserStates(bool ldEnabled, bool laserTecEnabled)
        {
            laserEnabled = ldEnabled;
            tecEnabled = laserTecEnabled;
        }

        /// <summary>供自动蛇形扫描安全切换 LD。</summary>
        private void SetLaserOutputForScan(bool enabled)
        {
            lock (laserDeviceSync)
            {
                Terra.Device device = laserDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("激光器尚未连接，无法执行自动蛇形扫描。");

                bool success = enabled ? device.setLDOn() : device.setLDOff();
                if (!success && !IsThbdLaser(device))
                    throw new InvalidOperationException(enabled ? "自动打开激光失败。" : "自动关闭激光失败。");
                laserEnabled = enabled;
            }

            LaserSettingsForm settings = laserSettingsForm;
            if (settings != null && !settings.IsDisposed)
                settings.SetLaserOutputStateFromScan(enabled);
        }

        /// <summary>供自动蛇形扫描控制 TEC；扫描开始前开启，结束或异常时关闭。</summary>
        private void SetTecOutputForScan(bool enabled)
        {
            lock (laserDeviceSync)
            {
                Terra.Device device = laserDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("激光器尚未连接，无法控制 TEC。");

                bool success = enabled ? device.setTECOn() : device.setTECOff();
                if (!success && !IsThbdLaser(device))
                    throw new InvalidOperationException(enabled ? "自动打开 TEC 失败。" : "自动关闭 TEC 失败。");
                tecEnabled = enabled;
            }

            LaserSettingsForm settings = laserSettingsForm;
            if (settings != null && !settings.IsDisposed)
                settings.SetTecOutputStateFromScan(enabled);
        }

        /// <summary>在 LD 已打开且稳定后采集、扣除当前点暗谱并保存结果。</summary>
        private bool AcquireSpectrumForScan(int scanIndex)
        {
            double[] wavelengths;
            double[] intensities;
            double excitationWavelength;
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，扫描已安全停止。");

                // 采集在扫描工作线程中完成；本次光谱有效返回前平台不会继续移动。
                intensities = AcquireStableSpectrum(device);
                wavelengths = device.getWavelengths();
                excitationWavelength = device.getLaserWavelength();
                if (excitationWavelength <= 0)
                    excitationWavelength = device.excitedWaveLength;
                if (excitationWavelength <= 0 && laserDevice != null)
                    excitationWavelength = laserDevice.getLaserWavelength();
                if (excitationWavelength <= 0 && laserDevice != null)
                    excitationWavelength = laserDevice.excitedWaveLength;
            }

            if (wavelengths == null || wavelengths.Length != intensities.Length)
                throw new InvalidOperationException("光谱仪返回的波长与强度数据长度不一致，扫描已安全停止。");
            if (excitationWavelength <= 0)
                throw new InvalidOperationException(
                    "Terra SDK 未提供有效的激发波长，无法计算拉曼位移。请先在设备参数中设置实际激光波长。");

            double[] darkSpectrum = GetScanDarkSpectrum(scanIndex, intensities.Length);
            double[] correctedIntensities = SubtractDarkSpectrumAndSmooth(intensities, darkSpectrum);
            RamanSpectrum laserOnSpectrum = CreateRamanSpectrum(
                wavelengths, correctedIntensities, excitationWavelength);
            SaveLaserOnSpectrum(scanIndex, laserOnSpectrum);
            ShowSpectrum(
                laserOnSpectrum.RamanShifts,
                laserOnSpectrum.Intensities,
                "开激光拉曼谱（已扣除当前点暗谱）");
            MarkScanPointSpectrumAvailable(scanIndex);
            return true;
        }

        private static double[] AcquireStableSpectrum(Terra.Device device)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                double[] spectrum = device.getSpectrum();
                if (spectrum != null && spectrum.Length > 1)
                    return spectrum;
            }

            throw new InvalidOperationException(
                "光谱仪未返回有效光谱，扫描已安全停止。请重新插拔光谱仪USB或者重启程序。");
        }

        /// <summary>在当前平台点、LD 关闭状态下采集暗谱，供紧随其后的开激光读谱扣除。</summary>
        private void CaptureDarkSpectrumForScan(int scanIndex)
        {
            double[] intensities = ReadCurrentSpectrumIntensities("扫描暗谱");
            lock (darkSpectrumSync)
            {
                scanDarkSpectra[scanIndex] = (double[])intensities.Clone();
            }
        }

        /// <summary>清除首个开激光积分切换时可能残留的缓存帧，不显示也不保存。</summary>
        private void DiscardSpectrumForScan()
        {
            ReadCurrentSpectrumIntensities("首点过渡光谱");
        }

        /// <summary>读取一张原始强度帧，不改变连接、激光或积分时间设置。</summary>
        private double[] ReadCurrentSpectrumIntensities(string operationName)
        {
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，无法采集" + operationName + "。 ");

                return (double[])AcquireStableSpectrum(device).Clone();
            }
        }

        private double[] GetScanDarkSpectrum(int scanIndex, int expectedLength)
        {
            lock (darkSpectrumSync)
            {
                double[] darkSpectrum;
                if (!scanDarkSpectra.TryGetValue(scanIndex, out darkSpectrum))
                    throw new InvalidOperationException("当前扫描点未采集到暗谱，无法进行背景扣除。");
                if (darkSpectrum.Length != expectedLength)
                    throw new InvalidOperationException("当前扫描点的暗谱长度与开激光光谱不一致，无法进行背景扣除。");
                return (double[])darkSpectrum.Clone();
            }
        }

        private static double[] SubtractDarkSpectrumAndSmooth(double[] signal, double[] darkSpectrum)
        {
            if (signal == null || darkSpectrum == null || signal.Length != darkSpectrum.Length)
                throw new InvalidOperationException("光谱与暗谱数据不匹配，无法进行背景扣除。");

            double[] corrected = new double[signal.Length];
            for (int index = 0; index < corrected.Length; index++)
                corrected[index] = signal[index] - darkSpectrum[index];
            return SmoothMovingAverage(corrected, 5);
        }

        private static double[] SmoothMovingAverage(double[] values, int windowSize)
        {
            double[] smoothed = new double[values.Length];
            int halfWindow = windowSize / 2;
            for (int index = 0; index < values.Length; index++)
            {
                int start = Math.Max(0, index - halfWindow);
                int end = Math.Min(values.Length - 1, index + halfWindow);
                double total = 0;
                for (int sample = start; sample <= end; sample++)
                    total += values[sample];
                smoothed[index] = total / (end - start + 1);
            }
            return smoothed;
        }

        private bool HasSavedDarkSpectrum()
        {
            lock (darkSpectrumSync)
                return savedDarkSpectrum != null && savedDarkSpectrum.Length > 1;
        }

        private double[] GetSavedDarkSpectrum(int expectedLength)
        {
            lock (darkSpectrumSync)
            {
                if (savedDarkSpectrum == null || savedDarkSpectrum.Length == 0)
                    throw new InvalidOperationException("需要先采集暗谱再进行展示。");
                if (savedDarkSpectrum.Length != expectedLength)
                    throw new InvalidOperationException("保存的暗谱与当前光谱长度不一致，请重新采集暗谱。");
                return (double[])savedDarkSpectrum.Clone();
            }
        }

        private void SaveCurrentDarkSpectrum()
        {
            double[] intensities = ReadCurrentSpectrumIntensities("暗谱");
            lock (darkSpectrumSync)
            {
                savedDarkSpectrum = (double[])intensities.Clone();
            }
        }

        /// <summary>
        /// 扫描开始前停止此前残留的积分。正常扫描期间不额外预读光谱，避免改变第一点的数据。
        /// </summary>
        private void WarmUpSpectrometerForScan()
        {
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，无法预热采谱。");

                try { device.stopSpectrum(); } catch { }
            }
        }

        private static RamanSpectrum CreateRamanSpectrum(
            double[] wavelengths,
            double[] intensities,
            double excitationWavelength)
        {
            List<double> shifts = new List<double>();
            List<double> values = new List<double>();
            for (int index = 0; index < wavelengths.Length; index++)
            {
                double wavelength = wavelengths[index];
                double intensity = intensities[index];
                if (wavelength <= 0 || double.IsNaN(wavelength) || double.IsInfinity(wavelength)
                    || double.IsNaN(intensity) || double.IsInfinity(intensity))
                    continue;

                // 标准拉曼位移：正值为 Stokes 区，负值为反 Stokes 区，单位 cm⁻¹。
                double shift = 10000000.0 * (1.0 / excitationWavelength - 1.0 / wavelength);
                shifts.Add(shift);
                values.Add(intensity);
            }

            if (shifts.Count < 2)
                throw new InvalidOperationException("光谱仪当前波长范围内没有足够的有效拉曼数据。");

            double[] x = shifts.ToArray();
            double[] y = values.ToArray();
            Array.Sort(x, y);
            return new RamanSpectrum(x, y);
        }

        /// <summary>保存该扫描点的开激光结果，供扫描完成后从矩阵中点击回看。</summary>
        private void SaveLaserOnSpectrum(int scanIndex, RamanSpectrum spectrum)
        {
            lock (savedSpectrumSync)
            {
                laserOnSpectra[scanIndex] = new RamanSpectrum(
                    (double[])spectrum.RamanShifts.Clone(),
                    (double[])spectrum.Intensities.Clone());
            }
        }

        /// <summary>在扫描矩阵中标记已保存开激光光谱的点。</summary>
        private void MarkScanPointSpectrumAvailable(int scanIndex)
        {
            if (IsDisposed || Disposing)
                return;
            if (InvokeRequired)
            {
                try { Invoke(new Action<int>(MarkScanPointSpectrumAvailable), scanIndex); }
                catch (InvalidOperationException) { }
                return;
            }

            scanMatrixPreviewControl.SetSpectrumAvailable(scanIndex);
        }

        /// <summary>点击矩阵点后显示该点的开激光后拉曼光谱。</summary>
        private void ScanMatrixPreviewControl_ScanPointSelected(object sender, ScanPointSelectedEventArgs e)
        {
            RamanSpectrum spectrum;
            lock (savedSpectrumSync)
            {
                if (!laserOnSpectra.TryGetValue(e.ScanIndex, out spectrum))
                    return;
            }

            ShowSpectrum(
                spectrum.RamanShifts,
                spectrum.Intensities,
                string.Format("第 {0} 点开激光原始拉曼谱", e.ScanIndex + 1));
        }

        private void InitializeSpectrumPlot()
        {
            const string plotFont = "Microsoft YaHei UI";
            ScottPlot.Fonts.Default = plotFont;
            formsPlot1.Plot.Clear();
            formsPlot1.Plot.Title("等待光谱采集");
            formsPlot1.Plot.XLabel("拉曼位移 (cm⁻¹)");
            formsPlot1.Plot.YLabel("强度");
            formsPlot1.Plot.Axes.Title.Label.FontName = plotFont;
            formsPlot1.Plot.Axes.Bottom.Label.FontName = plotFont;
            formsPlot1.Plot.Axes.Bottom.TickLabelStyle.FontName = plotFont;
            formsPlot1.Plot.Axes.Left.Label.FontName = plotFont;
            formsPlot1.Plot.Axes.Left.TickLabelStyle.FontName = plotFont;
            formsPlot1.Plot.Font.Automatic();
            formsPlot1.Refresh();
        }

        private void ShowSpectrum(double[] ramanShifts, double[] intensities, string title)
        {
            if (IsDisposed || Disposing)
                return;
            if (InvokeRequired)
            {
                Invoke(new Action<double[], double[], string>(ShowSpectrum),
                    ramanShifts, intensities, title);
                return;
            }

            formsPlot1.Plot.Clear();
            ScottPlot.Plottables.Scatter spectrum = formsPlot1.Plot.Add.Scatter(ramanShifts, intensities);
            spectrum.MarkerSize = 0;
            spectrum.LineWidth = 1.5F;
            spectrum.Color = ScottPlot.Colors.Red;
            formsPlot1.Plot.Title(title);
            formsPlot1.Plot.XLabel("拉曼位移(cm⁻¹)");
            formsPlot1.Plot.YLabel("强度");
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
        }

        /// <summary>切换实时读取模式；首次启动会在专用提示窗口中采集暗谱。</summary>
        private async void RealtimeSpectrum_Click(object sender, EventArgs e)
        {
            if (realtimeSpectrumCancellation != null)
            {
                await StopRealtimeSpectrumAsync(true);
                return;
            }

            if (spectrometerDevice == null)
            {
                MessageBox.Show(this, "请先连接光谱仪。", "实时光谱",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (scanCancellation != null || calibrationCancellation != null)
                return;

            if (!HasSavedDarkSpectrum())
            {
                using (DarkSpectrumPromptForm prompt = new DarkSpectrumPromptForm())
                {
                    if (prompt.ShowDialog(this) != DialogResult.OK)
                        return;
                }

                try
                {
                    SetLaserOutputForScan(false);
                    RealtimeSpectrum.Enabled = false;
                    await Task.Run(() => SaveCurrentDarkSpectrum());
                    // 暗谱完成后，为实时调试恢复激发光：先 TEC，后 LD。
                    SetTecOutputForScan(true);
                    SetLaserOutputForScan(true);
                    await Task.Delay(GetSpectrometerIntegrationTimeMillisecondsForScan() + 100);
                    MessageBox.Show(this,
                        "采集暗谱成功，激光器自动打开，实时光谱如图所示。",
                        "实时光谱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "实时光谱",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    UpdateRealtimeSpectrumButtonState();
                    return;
                }
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            realtimeSpectrumCancellation = cancellation;
            RealtimeSpectrum.Text = "停止实时光谱";
            RealtimeSpectrum.ToolTipText = "停止实时读取并清空波形图";
            RealtimeSpectrum.Enabled = true;
            realtimeSpectrumTask = Task.Run(() => RunRealtimeSpectrumLoop(cancellation), cancellation.Token);
        }

        /// <summary>持续显示当前光谱仪的暗谱扣除结果，不写入扫描矩阵或点位存档。</summary>
        private void RunRealtimeSpectrumLoop(CancellationTokenSource cancellation)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    RamanSpectrum spectrum = ReadRealtimeSpectrum();
                    if (token.IsCancellationRequested || IsDisposed || Disposing)
                        return;

                    BeginInvoke(new Action(() =>
                    {
                        if (realtimeSpectrumCancellation == cancellation)
                        {
                            ShowSpectrum(
                                spectrum.RamanShifts,
                                spectrum.Intensities,
                                "实时光谱");
                        }
                    }));

                    if (token.WaitHandle.WaitOne(30))
                        return;
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !IsDisposed && !Disposing)
                {
                    BeginInvoke(new Action(() => HandleRealtimeSpectrumError(cancellation, ex)));
                }
            }
        }

        /// <summary>在光谱仪锁内读取一帧，并转换为用于实时显示的拉曼坐标。</summary>
        private RamanSpectrum ReadRealtimeSpectrum()
        {
            double[] wavelengths;
            double[] intensities;
            double excitationWavelength;
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，无法继续实时采谱。");

                intensities = AcquireStableSpectrum(device);
                wavelengths = device.getWavelengths();
                excitationWavelength = device.getLaserWavelength();
                if (excitationWavelength <= 0)
                    excitationWavelength = device.excitedWaveLength;
            }

            if (wavelengths == null || intensities == null || wavelengths.Length != intensities.Length)
                throw new InvalidOperationException("光谱仪返回的波长与强度数据长度不一致。");
            if (excitationWavelength <= 0)
                throw new InvalidOperationException("光谱仪未返回有效的激发波长。");
            double[] darkSpectrum = GetSavedDarkSpectrum(intensities.Length);
            double[] correctedIntensities = SubtractDarkSpectrumAndSmooth(intensities, darkSpectrum);
            return CreateRamanSpectrum(wavelengths, correctedIntensities, excitationWavelength);
        }

        /// <summary>实时读取异常时恢复工具栏按钮，防止它停留在“停止”状态。</summary>
        private void HandleRealtimeSpectrumError(CancellationTokenSource cancellation, Exception error)
        {
            if (realtimeSpectrumCancellation != cancellation)
                return;

            realtimeSpectrumCancellation = null;
            realtimeSpectrumTask = null;
            cancellation.Dispose();
            TurnOffLaserAndTecAfterRealtime();
            UpdateRealtimeSpectrumButtonState();
            MessageBox.Show(this, error.Message, "实时光谱", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>停止实时读取；停止当前 SDK 读谱以便立即释放光谱仪锁。</summary>
        private async Task StopRealtimeSpectrumAsync(bool clearPlot)
        {
            CancellationTokenSource cancellation = realtimeSpectrumCancellation;
            Task runningTask = realtimeSpectrumTask;
            realtimeSpectrumCancellation = null;
            realtimeSpectrumTask = null;

            if (cancellation != null)
            {
                cancellation.Cancel();
                Terra.Device device = spectrometerDevice;
                if (device != null)
                {
                    try { device.stopSpectrum(); } catch { }
                }

                if (runningTask != null)
                {
                    try { await runningTask; }
                    catch (OperationCanceledException) { }
                }
                cancellation.Dispose();
                TurnOffLaserAndTecAfterRealtime();
            }

            if (clearPlot && !IsDisposed && !Disposing)
                InitializeSpectrumPlot();
            UpdateRealtimeSpectrumButtonState();
        }

        /// <summary>实时模式停止或异常退出后，按 LD、TEC 的安全顺序关闭激光器。</summary>
        private void TurnOffLaserAndTecAfterRealtime()
        {
            if (laserDevice == null)
                return;

            try { SetLaserOutputForScan(false); }
            catch { }
            try { SetTecOutputForScan(false); }
            catch { }
        }

        /// <summary>连接前、扫描/定标期间禁用；实时显示中保留按钮以允许用户停止。</summary>
        private void UpdateRealtimeSpectrumButtonState()
        {
            bool isRunning = realtimeSpectrumCancellation != null;
            if (isRunning)
            {
                RealtimeSpectrum.Text = "停止实时光谱";
                RealtimeSpectrum.ToolTipText = "停止实时读取并清空波形图";
                RealtimeSpectrum.Enabled = true;
                return;
            }

            RealtimeSpectrum.Text = "实时光谱";
            RealtimeSpectrum.ToolTipText = "开始实时读取当前光谱仪的光谱";
            RealtimeSpectrum.Enabled = spectrometerDevice != null
                && scanCancellation == null
                && calibrationCancellation == null;
        }

        /// <summary>窗口缩放时为下方参考图区保留可见空间。</summary>
        private void MainForm_Resize(object sender, EventArgs e)
        {
            LayoutBrightFieldPreviewArea();
        }

        private void LayoutBrightFieldPreviewArea()
        {
            formsPlot1.Height = Math.Max(280, (int)(ClientSize.Height * 0.52));
        }

        /// <summary>接收相机框选完成后生成的明场图；位图所有权转移给主窗口。</summary>
        private void CameraSelectionPreviewUpdated(Bitmap preview)
        {
            if (preview == null)
                return;
            if (IsDisposed || Disposing)
            {
                preview.Dispose();
                return;
            }
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<Bitmap>(CameraSelectionPreviewUpdated), preview); }
                catch (InvalidOperationException) { preview.Dispose(); }
                return;
            }

            List<PointF> scanPoints;
            string errorMessage;
            float selectionPixelAspectRatio;
            if (cameraShowForm != null
                && !cameraShowForm.IsDisposed
                && cameraShowForm.TryGetSnakeScanPoints(
                    out scanPoints, out errorMessage, out selectionPixelAspectRatio))
            {
                ClearSavedScanSpectra();
                scanMatrixPreviewControl.SetScanGrid(scanPoints, selectionPixelAspectRatio);
            }

            Image old = brightFieldPreviewPictureBox.Image;
            brightFieldPreviewPictureBox.Image = preview;
            brightFieldPreviewStatusLabel.Visible = false;
            if (old != null)
                old.Dispose();
        }

        /// <summary>
        /// 同时启用或禁用激光器设置入口及已打开窗口中的命令控件。
        /// </summary>
        private void SetLaserControlsEnabled(bool enabled)
        {
            LaserSettings.Enabled = enabled;
            bool spectrometerControlsEnabled = enabled && spectrometerDevice != null;
            spectrometerIntegrationTimeTextBox.Enabled = spectrometerControlsEnabled;
            ApplySpectrometerParameters.Enabled = spectrometerControlsEnabled;
            if (laserSettingsForm != null && !laserSettingsForm.IsDisposed)
                laserSettingsForm.SetDeviceCommandsEnabled(enabled);
        }

        /// <summary>
        /// 根据当前连接状态更新主窗口标签和已打开的设置窗口。
        /// </summary>
        private void UpdateDeviceConnectionAppearance()
        {
            label2.Text = laserDevice == null ? "激光器：未连接" : "激光器：已连接";
            labelSpectrometer.Text = spectrometerDevice == null ? "光谱仪：未连接" : "光谱仪：已连接";
            LaserSettings.Enabled = laserDevice != null;
            UpdateSpectrometerIntegrationControls();
            UpdateRealtimeSpectrumButtonState();
            if (laserSettingsForm != null && !laserSettingsForm.IsDisposed)
                laserSettingsForm.RefreshDeviceState();
        }

        /// <summary>应用左侧输入框中的积分时间，并在下一次扫描中同步采用该时长。</summary>
        private async void ApplySpectrometerParameters_Click(object sender, EventArgs e)
        {
            double requestedIntegrationTime;
            if (!TryParseIntegrationTime(spectrometerIntegrationTimeTextBox.Text, out requestedIntegrationTime))
            {
                MessageBox.Show(this, "请输入大于 0 的积分时间（单位：ms）。", "应用参数",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ApplySpectrometerParameters.Enabled = false;
            spectrometerIntegrationTimeTextBox.Enabled = false;
            try
            {
                await Task.Run(() => ApplySpectrometerIntegrationTime(requestedIntegrationTime));
                UpdateSpectrometerIntegrationControls();
                MessageBox.Show(this,
                    string.Format("光谱仪积分时间已设置为 {0} ms。", FormatIntegrationTime(spectrometerIntegrationTimeMilliseconds)),
                    "应用参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateSpectrometerIntegrationControls();
                MessageBox.Show(this, ex.Message, "应用参数", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                bool connected = spectrometerDevice != null;
                spectrometerIntegrationTimeTextBox.Enabled = connected;
                ApplySpectrometerParameters.Enabled = connected;
            }
        }

        /// <summary>在设备锁内校验并写入积分时间，同时更新 SDK 返回的可用范围。</summary>
        private void ApplySpectrometerIntegrationTime(double requestedIntegrationTime)
        {
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪尚未连接，无法应用积分时间。");

                double minimum = device.getMinIntegrationTime();
                double maximum = device.getMaxIntegrationTime();
                if (minimum <= 0 || maximum < minimum)
                    throw new InvalidOperationException("Terra SDK 未返回有效的光谱仪积分时间范围。");
                if (requestedIntegrationTime < minimum || requestedIntegrationTime > maximum)
                {
                    throw new InvalidOperationException(string.Format(
                        "积分时间必须在 {0} 至 {1} ms 之间。",
                        FormatIntegrationTime(minimum),
                        FormatIntegrationTime(maximum)));
                }

                device.setIntegrationTime(requestedIntegrationTime);
                spectrometerMinimumIntegrationTimeMilliseconds = minimum;
                spectrometerMaximumIntegrationTimeMilliseconds = maximum;
                spectrometerIntegrationTimeMilliseconds = requestedIntegrationTime;
            }
        }

        /// <summary>用 SDK 读取到的上下限更新左侧提示，并显示当前实际使用的值。</summary>
        private void UpdateSpectrometerIntegrationControls()
        {
            bool connected = spectrometerDevice != null;
            spectrometerIntegrationTimeTextBox.Text = FormatIntegrationTime(spectrometerIntegrationTimeMilliseconds);
            if (connected && spectrometerMinimumIntegrationTimeMilliseconds > 0
                && spectrometerMaximumIntegrationTimeMilliseconds >= spectrometerMinimumIntegrationTimeMilliseconds)
            {
                integrationRangeLabel.Text = string.Format(
                    "可设置范围：{0} - {1} ms",
                    FormatIntegrationTime(spectrometerMinimumIntegrationTimeMilliseconds),
                    FormatIntegrationTime(spectrometerMaximumIntegrationTimeMilliseconds));
            }
            else
            {
                integrationRangeLabel.Text = "可设置范围：连接后读取";
            }
            spectrometerIntegrationTimeTextBox.Enabled = connected;
            ApplySpectrometerParameters.Enabled = connected;
        }

        private static bool TryParseIntegrationTime(string text, out double integrationTime)
        {
            return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out integrationTime)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out integrationTime))
                && integrationTime > 0 && !double.IsInfinity(integrationTime) && !double.IsNaN(integrationTime);
        }

        private static double ClampIntegrationTime(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static string FormatIntegrationTime(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        /// <summary>将当前已应用的积分时间传入后台扫描，转换为可取消等待的毫秒整数。</summary>
        private int GetSpectrometerIntegrationTimeMillisecondsForScan()
        {
            return Math.Max(1, (int)Math.Ceiling(spectrometerIntegrationTimeMilliseconds));
        }

        /// <summary>
        /// 关闭并释放当前激光器设置窗口，避免重新连接后继续操作旧设备对象。
        /// </summary>
        private void CloseLaserSettingsWindow()
        {
            if (laserSettingsForm == null || laserSettingsForm.IsDisposed)
                return;
            laserSettingsForm.Close();
            laserSettingsForm = null;
        }

        /// <summary>
        /// 尽可能关闭激光和 TEC，然后释放 Terra 设备及 TANGO 串口。
        /// </summary>
        private void DisconnectDevices()
        {
            Terra.Device spectrumDevice = spectrometerDevice;
            if (spectrumDevice != null)
            {
                // stopSpectrum() 专门用于中断尚未完成的同步 getSpectrum()，不能等待采集锁。
                try { spectrumDevice.stopSpectrum(); } catch { }
            }

            lock (spectrometerDeviceSync)
            {
                spectrometerDevice = null;
            }
            lock (savedSpectrumSync)
                laserOnSpectra.Clear();
            lock (darkSpectrumSync)
            {
                savedDarkSpectrum = null;
                scanDarkSpectra.Clear();
            }

            lock (laserDeviceSync)
            {
                Terra.Device device = laserDevice;
                laserDevice = null;
                if (device != null)
                {
                    try { device.setLDOff(); } catch { }
                    try { device.setTECOff(); } catch { }
                }

                try { Terra.DeviceWrapper.closeAllDevices(); } catch { }
                SerialPortManager.Close();
                laserEnabled = false;
                tecEnabled = false;
            }
        }

        /// <summary>重新框选扫描区域时丢弃旧网格及其所有已保存光谱。</summary>
        private void ClearSavedScanSpectra()
        {
            lock (savedSpectrumSync)
                laserOnSpectra.Clear();
            lock (darkSpectrumSync)
                scanDarkSpectra.Clear();
            scanMatrixPreviewControl.ClearSpectrumAvailability();
            InitializeSpectrumPlot();
        }

        /// <summary>
        /// 打开相机窗口；若窗口已经存在则将其恢复并激活。
        /// </summary>
        private void CameraShow_Click(object sender, EventArgs e)
        {
            if (cameraShowForm == null || cameraShowForm.IsDisposed)
            {
                cameraShowForm = new CameraShowForm();
                cameraShowForm.SelectionPreviewUpdated += CameraSelectionPreviewUpdated;
                cameraShowForm.FormClosed += CameraShowForm_FormClosed;
                cameraShowForm.Show(this);
                return;
            }

            if (cameraShowForm.WindowState == FormWindowState.Minimized)
            {
                cameraShowForm.WindowState = FormWindowState.Normal;
            }

            cameraShowForm.Activate();
        }

        /// <summary>
        /// 相机窗口关闭后清除引用，允许下次重新创建。
        /// </summary>
        private void CameraShowForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            CameraShowForm closedCamera = sender as CameraShowForm;
            if (closedCamera != null)
                closedCamera.SelectionPreviewUpdated -= CameraSelectionPreviewUpdated;
            cameraShowForm = null;
            stageScanController.ResetOrigin();
        }

        /// <summary>
        /// 在激光关闭且明场图像清晰时，单独计算并保存像素与平台坐标换算矩阵。
        /// </summary>
        private async void CalibrateStage_Click(object sender, EventArgs e)
        {
            if (calibrationCancellation != null || scanCancellation != null)
                return;
            if (cameraShowForm == null || cameraShowForm.IsDisposed)
            {
                MessageBox.Show(this, "请先打开相机窗口。", "平台定标",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!SerialPortManager.IsOpen)
            {
                MessageBox.Show(this, "请先连接 TANGO 控制器。", "平台定标",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (laserEnabled)
            {
                MessageBox.Show(this, "定标需要清晰的样品纹理，请先关闭激光并打开明场照明。", "平台定标",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                    "请确认明场照明已经打开，且相机中能够清晰看到样品纹理。是否开始定标？",
                    "平台定标",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            if (realtimeSpectrumCancellation != null)
                await StopRealtimeSpectrumAsync(true);

            calibrationCancellation = new CancellationTokenSource();
            CancellationToken token = calibrationCancellation.Token;
            cameraShowForm.HideSelectionOverlayForCalibration();
            CalibrateStage.Enabled = false;
                ScanSelection.Enabled = false;
            SetLaserControlsEnabled(false);
            UpdateRealtimeSpectrumButtonState();
            IProgress<string> progress = new Progress<string>(text => CalibrateStage.Text = text);
            try
            {
                AlignmentVerificationResult verification = await Task.Run(
                    () => stageScanController.Calibrate(cameraShowForm, progress, token),
                    token);
                MessageBox.Show(this,
                    string.Format(
                        "平台定标完成。\r\n自动微调后平均定位偏差：{0:F2} 像素\r\n最大定位偏差：{1:F2} 像素\r\n\r\n现在可以关闭明场照明，然后执行蛇形扫描。",
                        verification.AverageErrorPixels,
                        verification.MaximumErrorPixels),
                    "平台定标",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show(this, "平台定标已停止。", "平台定标",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                stageScanController.ResetOrigin();
                MessageBox.Show(this, ex.Message, "平台定标失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (cameraShowForm != null && !cameraShowForm.IsDisposed)
                    cameraShowForm.RestoreSelectionOverlayAfterCalibration();
                calibrationCancellation.Dispose();
                calibrationCancellation = null;
                CalibrateStage.Text = "平台定标";
                CalibrateStage.Enabled = true;
                ScanSelection.Enabled = true;
                SetLaserControlsEnabled(laserDevice != null);
                UpdateRealtimeSpectrumButtonState();
            }
        }

        /// <summary>
        /// 启动蛇形扫描；扫描进行中再次点击则请求安全停止。
        /// </summary>
        private async void ScanSelection_Click(object sender, EventArgs e)
        {
            if (scanCancellation != null)
            {
                // 忽略启动按钮产生的第二次双击事件，避免刚开始标定就被误判为停止操作。
                if (DateTime.UtcNow - scanStartedUtc < ScanDoubleClickGuard)
                    return;

                ScanSelection.Text = "停止中…";
                scanCancellation.Cancel();
                return;
            }

            if (cameraShowForm == null || cameraShowForm.IsDisposed)
            {
                MessageBox.Show(this, "请先打开相机窗口并框选扫描区域。", "蛇形扫描",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!SerialPortManager.IsOpen)
            {
                MessageBox.Show(this, "请先连接 TANGO 串口。", "蛇形扫描",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (laserDevice == null)
            {
                MessageBox.Show(this, "请先连接激光器。", "蛇形扫描",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (spectrometerDevice == null)
            {
                MessageBox.Show(this, "请先连接光谱仪。", "蛇形扫描",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!stageScanController.HasCalibration)
            {
                MessageBox.Show(this,
                    "尚未保存平台标定数据。请先关闭激光、打开明场照明，然后点击“平台定标”。",
                    "蛇形扫描",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            List<PointF> scanPoints;
            string errorMessage;
            float selectionPixelAspectRatio;
            if (!cameraShowForm.TryGetSnakeScanPoints(
                out scanPoints, out errorMessage, out selectionPixelAspectRatio))
            {
                MessageBox.Show(this, errorMessage, "蛇形扫描",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 即使框选区域未改变，新一轮蛇形扫描也不能复用上一轮的点位光谱。
            if (realtimeSpectrumCancellation != null)
                await StopRealtimeSpectrumAsync(true);
            ClearSavedScanSpectra();

            scanStartedUtc = DateTime.UtcNow;
            scanCancellation = new CancellationTokenSource();
            CancellationToken token = scanCancellation.Token;
            ScanSelection.Text = "扫描中…";
            CalibrateStage.Enabled = false;
            SetLaserControlsEnabled(false);
            UpdateRealtimeSpectrumButtonState();
            IProgress<string> progress = new Progress<string>(text => ScanSelection.Text = text);
            try
            {
                await Task.Run(
                    () => stageScanController.Scan(
                        cameraShowForm,
                        scanPoints,
                        progress,
                        token,
                        SetLaserOutputForScan,
                        SetTecOutputForScan,
                        WarmUpSpectrometerForScan,
                        CaptureDarkSpectrumForScan,
                        DiscardSpectrumForScan,
                        GetSpectrometerIntegrationTimeMillisecondsForScan(),
                        AcquireSpectrumForScan),
                    token);
                MessageBox.Show(this,
                    string.Format("已完成 {0} 个网格点的蛇形遍历。", scanPoints.Count),
                    "蛇形扫描",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show(this, "扫描已停止。", "蛇形扫描",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "蛇形扫描失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                scanCancellation.Dispose();
                scanCancellation = null;
                scanStartedUtc = DateTime.MinValue;
                ScanSelection.Text = "蛇形扫描";
                CalibrateStage.Enabled = true;
                SetLaserControlsEnabled(laserDevice != null);
                UpdateRealtimeSpectrumButtonState();
            }
        }

        /// <summary>
        /// 主窗口关闭时先通知后台扫描任务取消。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (scanCancellation != null)
                scanCancellation.Cancel();
            if (calibrationCancellation != null)
                calibrationCancellation.Cancel();
            if (realtimeSpectrumCancellation != null)
                realtimeSpectrumCancellation.Cancel();
            Image preview = brightFieldPreviewPictureBox.Image;
            brightFieldPreviewPictureBox.Image = null;
            if (preview != null)
                preview.Dispose();
            CloseLaserSettingsWindow();
            DisconnectDevices();
            base.OnFormClosing(e);
        }
    }
}
