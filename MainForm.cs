using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Terra;

namespace MicroRaman
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
        private bool realtimeSpectrumStarting;
        private DateTime scanStartedUtc = DateTime.MinValue;
        private static readonly TimeSpan ScanDoubleClickGuard = TimeSpan.FromMilliseconds(800);
        // Raman mapping 默认采用 1000 ms 积分；用户可在主窗口中修改并应用。
        private const double DefaultSpectrometerIntegrationTimeMilliseconds = 1000.0;
        private readonly StageScanController stageScanController = new StageScanController();
        private readonly object laserDeviceSync = new object();
        private readonly object spectrometerDeviceSync = new object();
        private Terra.Device laserDevice;
        private Terra.Device spectrometerDevice;
        private double spectrometerIntegrationTimeMilliseconds = DefaultSpectrometerIntegrationTimeMilliseconds;
        private double spectrometerMinimumIntegrationTimeMilliseconds;
        private double spectrometerMaximumIntegrationTimeMilliseconds;
        private bool spectrometerCoolingStarted;
        private readonly object savedSpectrumSync = new object();
        private readonly Dictionary<int, RamanSpectrum> laserOnSpectra =
            new Dictionary<int, RamanSpectrum>();
        private readonly object scanSpectrumUiSync = new object();
        private readonly HashSet<int> pendingScanSpectrumIndexes = new HashSet<int>();
        private RamanSpectrum pendingScanSpectrum;
        private int pendingScanSpectrumIndex = -1;
        private int pendingScanSpectrumVersion;
        private bool scanSpectrumUiUpdateScheduled;
        private int completedScanPointCount;
        private int spectrumDataVersion;
        private bool mappingCalculationRunning;
        private readonly object darkSpectrumSync = new object();
        // 手动保存的背景只用于实时光谱；蛇形扫描在每一行开始时刷新暗谱以跟踪温漂。
        private double[] savedDarkSpectrum;
        private double[] scanDarkSpectrum;
        private bool laserEnabled;
        private bool tecEnabled;

        /// <summary>
        /// 用于回看单个扫描点的开激光后拉曼数据。
        /// </summary>
        private sealed class RamanSpectrum
        {
            /// <summary>
            /// 执行 RamanSpectrum 相关的内部处理。
            /// </summary>
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
            UpdateRamanMappingButtonState();
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
                        DefaultSpectrometerIntegrationTimeMilliseconds,
                        minimumIntegrationTime,
                        maximumIntegrationTime);
                    connectedLaser.setLDOff();
                    connectedLaser.setTECOff();
                    laserDevice = connectedLaser;
                    spectrometerDevice = connectedSpectrometer;
                    spectrometerMinimumIntegrationTimeMilliseconds = minimumIntegrationTime;
                    spectrometerMaximumIntegrationTimeMilliseconds = maximumIntegrationTime;
                    // 与左侧“应用参数”使用同一套 SDK 范围校验和写入逻辑。
                    ApplySpectrometerIntegrationTime(requestedIntegrationTime);
                    ConfigureSpectrometerAcquisition(connectedSpectrometer);
                    // 支持探测器 TEC 的光谱仪从连接后即开始制冷，减少正式 Mapping 前的等待和暗电流漂移。
                    spectrometerCoolingStarted = TryStartSpectrometerCooling(connectedSpectrometer);
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

        /// <summary>
        /// 供自动蛇形扫描安全切换 LD。
        /// </summary>
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

        /// <summary>
        /// 供自动蛇形扫描控制 TEC；扫描开始前开启，结束或异常时关闭。
        /// </summary>
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

        /// <summary>
        /// 在 LD 已打开且稳定后采集、扣除最近一次扫描暗谱并保存结果。
        /// </summary>
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
                intensities = AcquireFreshSpectrum(device);
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

            double[] darkSpectrum = GetScanDarkSpectrum(intensities.Length);
            double[] correctedIntensities = SubtractDarkSpectrumAndSmooth(intensities, darkSpectrum);
            RamanSpectrum laserOnSpectrum = CreateRamanSpectrum(
                wavelengths, correctedIntensities, excitationWavelength);
            SaveLaserOnSpectrum(scanIndex, laserOnSpectrum);
            QueueScanSpectrumUiUpdate(scanIndex, laserOnSpectrum);
            return true;
        }

        /// <summary>
        /// 强制从当前 LD 状态重新开始一次积分，避免读取到开关切换前的缓存帧。 不支持 reset 采集的设备会停止旧积分后回退到普通同步采谱。
        /// </summary>
        private static double[] AcquireFreshSpectrum(Terra.Device device)
        {
            try
            {
                double[] resetSpectrum = device.getResetSpectrum();
                if (resetSpectrum != null && resetSpectrum.Length > 1)
                    return resetSpectrum;
            }
            catch
            {
                // 部分旧固件没有实现 reset 采集，下面使用 stop + getSpectrum 兼容。
            }

            try { device.stopSpectrum(); } catch { }
            return AcquireStableSpectrum(device);
        }

        /// <summary>
        /// 执行 AcquireStableSpectrum 相关的内部处理。
        /// </summary>
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

        /// <summary>
        /// 在 LD 关闭状态强制采集一张新暗谱，供随后短时间内的亮谱扣除。
        /// </summary>
        private void CaptureDarkSpectrumForScan()
        {
            double[] intensities;
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，无法采集当前点暗谱。");
                intensities = AcquireFreshSpectrum(device);
            }
            lock (darkSpectrumSync)
            {
                scanDarkSpectrum = (double[])intensities.Clone();
            }
        }

        /// <summary>
        /// 读取一张原始强度帧，不改变连接、激光或积分时间设置。
        /// </summary>
        private double[] ReadCurrentSpectrumIntensities(string operationName)
        {
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，无法采集" + operationName + "。 ");

                // 暗谱必须从 LD 已关闭后的新积分取得，不能复用上一轮实时亮谱缓存。
                return (double[])AcquireFreshSpectrum(device).Clone();
            }
        }

        /// <summary>
        /// 获取ScanDarkSpectrum相关的内部处理。
        /// </summary>
        private double[] GetScanDarkSpectrum(int expectedLength)
        {
            lock (darkSpectrumSync)
            {
                if (scanDarkSpectrum == null)
                    throw new InvalidOperationException("本轮扫描未采集暗谱，无法进行背景扣除。");
                if (scanDarkSpectrum.Length != expectedLength)
                    throw new InvalidOperationException("扫描暗谱长度与开激光光谱不一致，无法进行背景扣除。");
                return (double[])scanDarkSpectrum.Clone();
            }
        }

        /// <summary>
        /// 执行 SubtractDarkSpectrumAndSmooth 相关的内部处理。
        /// </summary>
        private static double[] SubtractDarkSpectrumAndSmooth(double[] signal, double[] darkSpectrum)
        {
            if (signal == null || darkSpectrum == null || signal.Length != darkSpectrum.Length)
                throw new InvalidOperationException("光谱与暗谱数据不匹配，无法进行背景扣除。");

            double[] corrected = new double[signal.Length];
            for (int index = 0; index < corrected.Length; index++)
                corrected[index] = signal[index] - darkSpectrum[index];
            double[] smoothed = SmoothMovingAverage(corrected, 5);
            // 与商业 Raman 软件的处理顺序一致：暗谱扣除后再消除缓慢变化的荧光/暗电流基线。
            return RamanMappingAnalyzer.RemoveBaseline(smoothed);
        }

        /// <summary>
        /// 执行 SmoothMovingAverage 相关的内部处理。
        /// </summary>
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

        /// <summary>
        /// 判断SavedDarkSpectrum相关的内部处理。
        /// </summary>
        private bool HasSavedDarkSpectrum()
        {
            lock (darkSpectrumSync)
                return savedDarkSpectrum != null && savedDarkSpectrum.Length > 1;
        }

        /// <summary>
        /// 获取SavedDarkSpectrum相关的内部处理。
        /// </summary>
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

        /// <summary>
        /// 保存CurrentDarkSpectrum相关的内部处理。
        /// </summary>
        private void SaveCurrentDarkSpectrum()
        {
            double[] intensities = ReadCurrentSpectrumIntensities("暗谱");
            lock (darkSpectrumSync)
            {
                savedDarkSpectrum = (double[])intensities.Clone();
            }
        }

        /// <summary>
        /// 扫描开始前停止此前残留的积分；若当前型号确实返回了有效 CCD TEC 状态，则等待其稳定。 不支持 CCD TEC 的型号会直接跳过，不能仅依赖 SDK 的 isSupportCCDTEC（该版本会固定返回 true）。
        /// </summary>
        private void WarmUpSpectrometerForScan(CancellationToken cancellationToken)
        {
            bool waitForCooling;
            lock (spectrometerDeviceSync)
            {
                Terra.Device device = spectrometerDevice;
                if (device == null || !device.isUsbConnected())
                    throw new InvalidOperationException("光谱仪连接已断开，无法预热采谱。");

                try { device.stopSpectrum(); } catch { }
                waitForCooling = spectrometerCoolingStarted;
            }

            if (!waitForCooling)
                return;

            // 制冷已在连接时启动，这里最多再等 10 秒；每点配对暗谱仍会补偿剩余的小幅暗电流变化。
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte coolingState;
                lock (spectrometerDeviceSync)
                {
                    Terra.Device device = spectrometerDevice;
                    if (device == null || !device.isUsbConnected())
                        throw new InvalidOperationException("光谱仪连接已断开，无法检查温控状态。");

                    byte[] state;
                    try { state = device.getCCDTECState(); }
                    catch
                    {
                        spectrometerCoolingStarted = false;
                        return;
                    }
                    if (state == null || state.Length <= 11 || state[11] > 2)
                    {
                        spectrometerCoolingStarted = false;
                        return;
                    }
                    coolingState = state[11];
                }

                if (coolingState == 1)
                    return;
                // 2 表示 TEC 关闭：视为该硬件未实际接受温控命令，不阻塞扫描。
                if (coolingState == 2)
                {
                    spectrometerCoolingStarted = false;
                    return;
                }
                if (cancellationToken.WaitHandle.WaitOne(250))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// 创建RamanSpectrum相关的内部处理。
        /// </summary>
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

        /// <summary>
        /// 保存该扫描点的开激光结果，供扫描完成后从矩阵中点击回看。
        /// </summary>
        private void SaveLaserOnSpectrum(int scanIndex, RamanSpectrum spectrum)
        {
            lock (savedSpectrumSync)
            {
                laserOnSpectra[scanIndex] = new RamanSpectrum(
                    (double[])spectrum.RamanShifts.Clone(),
                    (double[])spectrum.Intensities.Clone());
            }
        }

        /// <summary>
        /// 扫描线程只投递数据，不等待 ScottPlot 和矩阵重绘；这样 LD 会在硬件读谱完成后立即关闭。 多个尚未处理的点合并成一次 UI 更新，但所有点仍会被标记为可回看。
        /// </summary>
        private void QueueScanSpectrumUiUpdate(int scanIndex, RamanSpectrum spectrum)
        {
            if (IsDisposed || Disposing)
                return;

            bool scheduleUpdate = false;
            lock (scanSpectrumUiSync)
            {
                pendingScanSpectrumIndexes.Add(scanIndex);
                pendingScanSpectrum = spectrum;
                pendingScanSpectrumIndex = scanIndex;
                pendingScanSpectrumVersion = spectrumDataVersion;
                if (!scanSpectrumUiUpdateScheduled)
                {
                    scanSpectrumUiUpdateScheduled = true;
                    scheduleUpdate = true;
                }
            }

            if (!scheduleUpdate)
                return;
            try { BeginInvoke(new Action(ProcessPendingScanSpectrumUiUpdate)); }
            catch (InvalidOperationException)
            {
                lock (scanSpectrumUiSync)
                    scanSpectrumUiUpdateScheduled = false;
            }
        }

        /// <summary>
        /// 执行 ProcessPendingScanSpectrumUiUpdate 相关的内部处理。
        /// </summary>
        private void ProcessPendingScanSpectrumUiUpdate()
        {
            List<int> availableIndexes;
            RamanSpectrum spectrum;
            int scanIndex;
            int dataVersion;
            lock (scanSpectrumUiSync)
            {
                availableIndexes = new List<int>(pendingScanSpectrumIndexes);
                pendingScanSpectrumIndexes.Clear();
                spectrum = pendingScanSpectrum;
                scanIndex = pendingScanSpectrumIndex;
                dataVersion = pendingScanSpectrumVersion;
                pendingScanSpectrum = null;
                pendingScanSpectrumIndex = -1;
                scanSpectrumUiUpdateScheduled = false;
            }

            if (dataVersion != spectrumDataVersion || spectrum == null)
                return;
            scanMatrixPreviewControl.SetSpectraAvailable(availableIndexes);
            ShowSpectrum(
                spectrum.RamanShifts,
                spectrum.Intensities,
                string.Format("第 {0} 点开激光拉曼谱（已扣除最近暗谱）", scanIndex + 1));
        }

        /// <summary>
        /// 清空PendingScanSpectrumUiUpdates相关的内部处理。
        /// </summary>
        private void ClearPendingScanSpectrumUiUpdates()
        {
            lock (scanSpectrumUiSync)
            {
                pendingScanSpectrumIndexes.Clear();
                pendingScanSpectrum = null;
                pendingScanSpectrumIndex = -1;
                pendingScanSpectrumVersion = spectrumDataVersion;
            }
        }

        /// <summary>
        /// 点击矩阵点后显示该点的开激光后拉曼光谱。
        /// </summary>
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

        /// <summary>
        /// 扫描全部成功完成后，按左侧选择的光谱指标生成 Mapping 伪彩图。
        /// </summary>
        private async void RamanMapping_Click(object sender, EventArgs e)
        {
            List<RamanMappingSpectrum> spectra = CreateRamanMappingSnapshot();
            if (spectra == null)
            {
                MessageBox.Show(this, "请先完整完成一次蛇形扫描。", "拉曼 Mapping",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateRamanMappingButtonState();
                return;
            }

            RamanMappingMode mappingMode = GetSelectedMappingMode();
            double targetShift = 960.0;
            double halfWidth = 20.0;
            double referenceShift = double.NaN;
            double referenceHalfWidth = double.NaN;
            bool requiresTargetRange = mappingMode == RamanMappingMode.PeakHeight
                || mappingMode == RamanMappingMode.PeakArea;
            if (requiresTargetRange)
            {
                using (RamanMappingOptionsForm rangeForm = new RamanMappingOptionsForm(
                    mappingMode, 500.0, 540.0))
                {
                    if (rangeForm.ShowDialog(this) != DialogResult.OK)
                        return;

                    targetShift = (rangeForm.RangeStart + rangeForm.RangeEnd) * 0.5;
                    halfWidth = (rangeForm.RangeEnd - rangeForm.RangeStart) * 0.5;
                }
            }
            else if (RequiresPeakParameters(mappingMode))
            {
                try
                {
                    AutoDetectedRamanPeak detectedPeak =
                        LabSpecPeakMappingAnalyzer.DetectTargetPeakParameters(spectra);
                    targetShift = detectedPeak.Center;
                    halfWidth = detectedPeak.HalfWidth;
                    referenceShift = detectedPeak.ReferenceCenter;
                    referenceHalfWidth = detectedPeak.ReferenceHalfWidth;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "拉曼 Mapping 失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            int dataVersion = spectrumDataVersion;
            mappingCalculationRunning = true;
            RamanMapping.Enabled = false;
            RamanMapping.Text = "计算中…";
            try
            {
                IDictionary<int, Color> colors;
                string mappingTitle;
                if (mappingMode == RamanMappingMode.FullSpectrumDifference)
                {
                    FullSpectrumDifferenceMappingResult result = await Task.Run(() =>
                        FullSpectrumDifferenceMappingAnalyzer.Analyze(spectra));
                    colors = result.Colors;
                    mappingTitle =
                        "拉曼 Mapping（全谱差异：相对背景的荧光/波形变化）";
                }
                else if (mappingMode == RamanMappingMode.Pca)
                {
                    PcaMappingResult result = await Task.Run(() =>
                        PcaMappingAnalyzer.Analyze(spectra));
                    colors = result.Colors;
                    mappingTitle = string.Format(
                        "拉曼 Mapping（PCA 全谱异常，{0} 个主成分；背景蓝，异常区域黄/红）",
                        result.ComponentCount);
                }
                else
                {
                    LabSpecPeakMappingResult result = await Task.Run(() =>
                        LabSpecPeakMappingAnalyzer.Analyze(
                            spectra, targetShift, halfWidth,
                            referenceShift, referenceHalfWidth, mappingMode));
                    colors = result.Colors;
                    mappingTitle = requiresTargetRange
                        ? string.Format(
                            "拉曼 Mapping（{0}：{1:F1}–{2:F1} cm⁻¹ 范围内找峰）",
                            result.MetricDisplayName,
                            result.TargetShift - result.HalfWidth,
                            result.TargetShift + result.HalfWidth)
                        : string.Format(
                            "拉曼 Mapping（自动峰 {0:F1}±{1:F1} cm⁻¹，{2}{3}）",
                            result.TargetShift,
                            result.HalfWidth,
                            result.MetricDisplayName,
                            result.UsedReferenceNormalization
                                ? string.Format(" / {0:F1} cm⁻¹ 自动参考峰", result.ReferenceShift)
                                : " / 未使用参考峰");
                }
                if (dataVersion != spectrumDataVersion || IsDisposed || Disposing)
                    return;

                scanMatrixPreviewControl.SetMappingColors(colors);
                scanMatrixGroupBox.Text = mappingTitle;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "拉曼 Mapping 失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                mappingCalculationRunning = false;
                RamanMapping.Text = "拉曼 Mapping";
                UpdateRamanMappingButtonState();
            }
        }

        /// <summary>
        /// 获取SelectedMappingMode相关的内部处理。
        /// </summary>
        private RamanMappingMode GetSelectedMappingMode()
        {
            if (mappingPeakHeightRadioButton.Checked) return RamanMappingMode.PeakHeight;
            if (mappingPeakPositionRadioButton.Checked) return RamanMappingMode.PeakPosition;
            if (mappingPeakWidthRadioButton.Checked) return RamanMappingMode.PeakWidth;
            if (mappingFullSpectrumRadioButton.Checked) return RamanMappingMode.FullSpectrumDifference;
            if (mappingPcaRadioButton.Checked) return RamanMappingMode.Pca;
            return RamanMappingMode.PeakArea;
        }

        /// <summary>
        /// 判断PeakParameters相关的内部处理。
        /// </summary>
        private static bool RequiresPeakParameters(RamanMappingMode mappingMode)
        {
            return mappingMode == RamanMappingMode.PeakHeight
                || mappingMode == RamanMappingMode.PeakArea
                || mappingMode == RamanMappingMode.PeakPosition
                || mappingMode == RamanMappingMode.PeakWidth;
        }

        /// <summary>
        /// 复制一份完整扫描数据，使后台计算期间不持有采集数据锁。
        /// </summary>
        private List<RamanMappingSpectrum> CreateRamanMappingSnapshot()
        {
            lock (savedSpectrumSync)
            {
                if (completedScanPointCount < 2 || laserOnSpectra.Count != completedScanPointCount)
                    return null;

                List<RamanMappingSpectrum> spectra =
                    new List<RamanMappingSpectrum>(completedScanPointCount);
                for (int scanIndex = 0; scanIndex < completedScanPointCount; scanIndex++)
                {
                    RamanSpectrum spectrum;
                    if (!laserOnSpectra.TryGetValue(scanIndex, out spectrum))
                        return null;
                    spectra.Add(new RamanMappingSpectrum(
                        scanIndex,
                        (double[])spectrum.RamanShifts.Clone(),
                        (double[])spectrum.Intensities.Clone()));
                }
                return spectra;
            }
        }

        /// <summary>
        /// 仅当本轮蛇形扫描完整结束且每个点都有光谱时允许生成 Mapping。
        /// </summary>
        private void UpdateRamanMappingButtonState()
        {
            int savedSpectrumCount;
            lock (savedSpectrumSync)
                savedSpectrumCount = laserOnSpectra.Count;

            RamanMapping.Enabled = !mappingCalculationRunning
                && scanCancellation == null
                && calibrationCancellation == null
                && completedScanPointCount >= 2
                && savedSpectrumCount == completedScanPointCount;
        }

        /// <summary>
        /// 初始化SpectrumPlot相关的内部处理。
        /// </summary>
        private void InitializeSpectrumPlot()
        {
            const string plotFont = "Microsoft YaHei UI";
            ScottPlot.Fonts.Default = plotFont;
            formsPlot1.Plot.Clear();
            formsPlot1.Plot.Title("等待光谱采集");
            formsPlot1.Plot.XLabel("拉曼位移 (cm\u207B\u00B9)");
            formsPlot1.Plot.YLabel("强度");
            formsPlot1.Plot.Axes.Title.Label.FontName = plotFont;
            // Segoe UI 包含上标负号 U+207B；中文字符会由系统回退到中文字体。
            formsPlot1.Plot.Axes.Bottom.Label.FontName = "Segoe UI";
            formsPlot1.Plot.Axes.Bottom.TickLabelStyle.FontName = plotFont;
            formsPlot1.Plot.Axes.Left.Label.FontName = plotFont;
            formsPlot1.Plot.Axes.Left.TickLabelStyle.FontName = plotFont;
            formsPlot1.Plot.Font.Automatic();
            formsPlot1.Refresh();
        }

        /// <summary>
        /// 显示Spectrum相关的内部处理。
        /// </summary>
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
            formsPlot1.Plot.XLabel("拉曼位移 (cm\u207B\u00B9)");
            formsPlot1.Plot.Axes.Bottom.Label.FontName = "Segoe UI";
            formsPlot1.Plot.YLabel("强度");
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
        }

        /// <summary>
        /// 切换实时读取模式；每次启动均重新采集本次会话的暗谱。
        /// </summary>
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

            await StartRealtimeSpectrumSessionAsync(true, true);
        }

        /// <summary>
        /// 以当前积分时间启动一次新的实时会话。每次会话都在 LD 关闭后重采暗谱， 防止积分时间、暗电流或上一轮缓存帧被错误复用。
        /// </summary>
        private async Task<bool> StartRealtimeSpectrumSessionAsync(bool promptForDarkSpectrum, bool showSuccessMessage)
        {
            realtimeSpectrumStarting = true;
            UpdatePlatformCommandButtonState();
            try
            {
                if (promptForDarkSpectrum)
                {
                    using (DarkSpectrumPromptForm prompt = new DarkSpectrumPromptForm())
                    {
                        if (prompt.ShowDialog(this) != DialogResult.OK)
                            return false;
                    }
                }

                // 每次会话都废弃上一轮的暗谱，并在 LD 关闭后重新开始一次完整积分。
                ClearSavedRealtimeDarkSpectrum();
                SetLaserOutputForScan(false);
                RealtimeSpectrum.Enabled = false;
                await Task.Run(() => SaveCurrentDarkSpectrum());
                // 暗谱完成后，为实时调试恢复激发光：先 TEC，后 LD。
                SetTecOutputForScan(true);
                SetLaserOutputForScan(true);
                int brightFrameDelay = Math.Max(300, GetSpectrometerIntegrationTimeMillisecondsForScan() + 100);
                await Task.Delay(brightFrameDelay);
                if (showSuccessMessage)
                {
                    MessageBox.Show(this,
                        "采集暗谱成功，激光器自动打开，实时光谱如图所示。",
                        "实时光谱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                CancellationTokenSource cancellation = new CancellationTokenSource();
                realtimeSpectrumCancellation = cancellation;
                realtimeSpectrumStarting = false;
                RealtimeSpectrum.Text = "停止实时光谱";
                RealtimeSpectrum.ToolTipText = "停止实时读取并清空波形图";
                RealtimeSpectrum.Enabled = true;
                UpdatePlatformCommandButtonState();
                realtimeSpectrumTask = Task.Run(() => RunRealtimeSpectrumLoop(cancellation), cancellation.Token);
                return true;
            }
            catch (Exception ex)
            {
                TurnOffLaserAndTecAfterRealtime();
                MessageBox.Show(this, ex.Message, "实时光谱",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateRealtimeSpectrumButtonState();
                return false;
            }
            finally
            {
                if (realtimeSpectrumCancellation == null)
                {
                    realtimeSpectrumStarting = false;
                    UpdatePlatformCommandButtonState();
                }
            }
        }

        /// <summary>
        /// 持续显示当前光谱仪的暗谱扣除结果，不写入扫描矩阵或点位存档。
        /// </summary>
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

        /// <summary>
        /// 在光谱仪锁内读取一帧，并转换为用于实时显示的拉曼坐标。
        /// </summary>
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

                // 重新开始完整积分，避免停止上一轮实时显示后取得残留的暗谱或亮谱缓存。
                intensities = AcquireFreshSpectrum(device);
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

        /// <summary>
        /// 实时读取异常时恢复工具栏按钮，防止它停留在“停止”状态。
        /// </summary>
        private void HandleRealtimeSpectrumError(CancellationTokenSource cancellation, Exception error)
        {
            if (realtimeSpectrumCancellation != cancellation)
                return;

            realtimeSpectrumCancellation = null;
            realtimeSpectrumTask = null;
            cancellation.Dispose();
            TurnOffLaserAndTecAfterRealtime();
            realtimeSpectrumStarting = false;
            UpdatePlatformCommandButtonState();
            UpdateRealtimeSpectrumButtonState();
            MessageBox.Show(this, error.Message, "实时光谱", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 停止实时读取；停止当前 SDK 读谱以便立即释放光谱仪锁。
        /// </summary>
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
                ClearSavedRealtimeDarkSpectrum();
            }

            if (clearPlot && !IsDisposed && !Disposing)
                InitializeSpectrumPlot();
            realtimeSpectrumStarting = false;
            UpdatePlatformCommandButtonState();
            UpdateRealtimeSpectrumButtonState();
        }

        /// <summary>
        /// 实时模式停止或异常退出后，按 LD、TEC 的安全顺序关闭激光器。
        /// </summary>
        private void TurnOffLaserAndTecAfterRealtime()
        {
            if (laserDevice == null)
                return;

            try { SetLaserOutputForScan(false); }
            catch { }
            try { SetTecOutputForScan(false); }
            catch { }
        }

        /// <summary>
        /// 实时会话结束后不复用旧暗谱，下一次启动必须重新在 LD 关闭状态采集。
        /// </summary>
        private void ClearSavedRealtimeDarkSpectrum()
        {
            lock (darkSpectrumSync)
                savedDarkSpectrum = null;
        }

        /// <summary>
        /// 实时光谱独占光谱仪和激光器期间，禁止启动会改变平台状态的操作。
        /// </summary>
        private void UpdatePlatformCommandButtonState()
        {
            if (realtimeSpectrumStarting || realtimeSpectrumCancellation != null)
            {
                CalibrateStage.Enabled = false;
                ScanSelection.Enabled = false;
                return;
            }

            if (scanCancellation == null && calibrationCancellation == null)
            {
                CalibrateStage.Enabled = true;
                ScanSelection.Enabled = true;
            }
        }

        /// <summary>
        /// 连接前、扫描/定标期间禁用；实时显示中保留按钮以允许用户停止。
        /// </summary>
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

        /// <summary>
        /// 窗口缩放时为下方参考图区保留可见空间。
        /// </summary>
        private void MainForm_Resize(object sender, EventArgs e)
        {
            LayoutBrightFieldPreviewArea();
        }

        /// <summary>
        /// 执行 LayoutBrightFieldPreviewArea 相关的内部处理。
        /// </summary>
        private void LayoutBrightFieldPreviewArea()
        {
            formsPlot1.Height = Math.Max(280, (int)(ClientSize.Height * 0.52));
        }

        /// <summary>
        /// 接收相机框选完成后生成的明场图；位图所有权转移给主窗口。
        /// </summary>
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
            if (completedScanPointCount == 0)
            {
                scanMatrixPreviewControl.ClearMappingColors();
                scanMatrixGroupBox.Text = "扫描坐标矩阵";
            }
            UpdateRamanMappingButtonState();
            if (laserSettingsForm != null && !laserSettingsForm.IsDisposed)
                laserSettingsForm.RefreshDeviceState();
        }

        /// <summary>
        /// 应用左侧输入框中的积分时间，并在下一次扫描中同步采用该时长。
        /// </summary>
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
            bool restartRealtimeSpectrum = realtimeSpectrumCancellation != null;
            try
            {
                if (restartRealtimeSpectrum)
                {
                    // 先完全结束旧积分、关闭 LD/TEC；旧积分时间对应的暗谱不能继续使用。
                    await StopRealtimeSpectrumAsync(true);
                    realtimeSpectrumStarting = true;
                    UpdatePlatformCommandButtonState();
                }

                await Task.Run(() => ApplySpectrometerIntegrationTime(requestedIntegrationTime));
                UpdateSpectrometerIntegrationControls();

                if (restartRealtimeSpectrum)
                {
                    bool resumed = await StartRealtimeSpectrumSessionAsync(false, false);
                    if (!resumed)
                        return;
                }

                MessageBox.Show(this,
                    restartRealtimeSpectrum
                        ? string.Format(
                            "光谱仪积分时间已设置为 {0} ms。\r\n已重新采集暗谱并恢复实时光谱。",
                            FormatIntegrationTime(spectrometerIntegrationTimeMilliseconds))
                        : string.Format(
                            "光谱仪积分时间已设置为 {0} ms。",
                            FormatIntegrationTime(spectrometerIntegrationTimeMilliseconds)),
                    "应用参数", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                UpdateSpectrometerIntegrationControls();
                MessageBox.Show(this, ex.Message, "应用参数", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (realtimeSpectrumCancellation == null)
                {
                    realtimeSpectrumStarting = false;
                    UpdatePlatformCommandButtonState();
                }
                bool connected = spectrometerDevice != null;
                spectrometerIntegrationTimeTextBox.Enabled = connected;
                ApplySpectrometerParameters.Enabled = connected;
            }
        }

        /// <summary>
        /// 在设备锁内校验并写入积分时间，同时更新 SDK 返回的可用范围。
        /// </summary>
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

                // Terra SDK 的 getIntegrationTime() 在部分设备上会返回旧值，不能用于连接阶段的强制校验。
                // 保持原有已验证可用的写入方式，并由后续 getResetSpectrum() 按该参数重新开始积分。
                device.setIntegrationTime(requestedIntegrationTime);
                try { device.clearFrameBuffer(); } catch { }
                spectrometerMinimumIntegrationTimeMilliseconds = minimum;
                spectrometerMaximumIntegrationTimeMilliseconds = maximum;
                spectrometerIntegrationTimeMilliseconds = requestedIntegrationTime;
            }
        }

        /// <summary>
        /// 按 Terra 官方 GODP 示例固定为自由触发、单次平均、单帧缓存，避免历史设置残留。
        /// </summary>
        private static void ConfigureSpectrometerAcquisition(Terra.Device device)
        {
            if (device == null)
                return;
            try { device.stopSpectrum(); } catch { }
            try { device.setTriggerMode(0); } catch { }
            try { device.setScansToAverage(1); } catch { }
            try { device.setFrameBufferNumber(1); } catch { }
            try { device.clearFrameBuffer(); } catch { }
        }

        /// <summary>
        /// 仅在设备能读回有效 CCD TEC 状态时启动光谱仪制冷；避免 SDK 固定返回支持而误操作无 TEC 型号。
        /// </summary>
        private static bool TryStartSpectrometerCooling(Terra.Device device)
        {
            if (device == null)
                return false;
            try
            {
                if (!device.isSupportCCDTEC())
                    return false;
                byte[] state = device.getCCDTECState();
                if (state == null || state.Length <= 11 || state[11] > 2)
                    return false;
                bool powerStarted = device.setCCDTECPowerOn();
                bool coolingEnabled = device.setCCDTECEnable();
                return powerStarted && coolingEnabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 用 SDK 读取到的上下限更新左侧提示，并显示当前实际使用的值。
        /// </summary>
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

        /// <summary>
        /// 尝试ParseIntegrationTime相关的内部处理。
        /// </summary>
        private static bool TryParseIntegrationTime(string text, out double integrationTime)
        {
            return (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out integrationTime)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out integrationTime))
                && integrationTime > 0 && !double.IsInfinity(integrationTime) && !double.IsNaN(integrationTime);
        }

        /// <summary>
        /// 限制IntegrationTime相关的内部处理。
        /// </summary>
        private static double ClampIntegrationTime(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        /// <summary>
        /// 格式化IntegrationTime相关的内部处理。
        /// </summary>
        private static string FormatIntegrationTime(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// 将当前已应用的积分时间传入后台扫描，转换为可取消等待的毫秒整数。
        /// </summary>
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
                if (spectrometerDevice != null)
                {
                    try
                    {
                        if (spectrometerDevice.isSupportCCDTEC())
                            spectrometerDevice.setCCDTECPowerOff();
                    }
                    catch { }
                }
                spectrometerDevice = null;
                spectrometerCoolingStarted = false;
            }
            lock (savedSpectrumSync)
                laserOnSpectra.Clear();
            completedScanPointCount = 0;
            spectrumDataVersion++;
            ClearPendingScanSpectrumUiUpdates();
            lock (darkSpectrumSync)
            {
                savedDarkSpectrum = null;
                scanDarkSpectrum = null;
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

        /// <summary>
        /// 重新框选扫描区域时丢弃旧网格及其所有已保存光谱。
        /// </summary>
        private void ClearSavedScanSpectra()
        {
            lock (savedSpectrumSync)
                laserOnSpectra.Clear();
            completedScanPointCount = 0;
            spectrumDataVersion++;
            ClearPendingScanSpectrumUiUpdates();
            lock (darkSpectrumSync)
                scanDarkSpectrum = null;
            scanMatrixPreviewControl.ClearSpectrumAvailability();
            scanMatrixPreviewControl.ClearMappingColors();
            scanMatrixGroupBox.Text = "扫描坐标矩阵";
            InitializeSpectrumPlot();
            UpdateRamanMappingButtonState();
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
            if (calibrationCancellation != null || scanCancellation != null
                || realtimeSpectrumStarting || realtimeSpectrumCancellation != null)
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
            UpdateRamanMappingButtonState();
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
                UpdateRamanMappingButtonState();
            }
        }

        /// <summary>
        /// 启动蛇形扫描；扫描进行中再次点击则请求安全停止。
        /// </summary>
        private async void ScanSelection_Click(object sender, EventArgs e)
        {
            if (realtimeSpectrumStarting || realtimeSpectrumCancellation != null)
                return;

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
            UpdateRamanMappingButtonState();
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
                        GetSpectrometerIntegrationTimeMillisecondsForScan(),
                        AcquireSpectrumForScan),
                    token);
                completedScanPointCount = scanPoints.Count;
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
                UpdateRamanMappingButtonState();
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
