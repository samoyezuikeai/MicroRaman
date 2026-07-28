using System;
using System.Collections.Generic;
using System.Drawing;
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
        private DateTime scanStartedUtc = DateTime.MinValue;
        private static readonly TimeSpan ScanDoubleClickGuard = TimeSpan.FromMilliseconds(800);
        private readonly StageScanController stageScanController = new StageScanController();
        private readonly object laserDeviceSync = new object();
        private readonly object spectrometerDeviceSync = new object();
        private Terra.Device laserDevice;
        private Terra.Device spectrometerDevice;
        private double[] laserOffSpectrum;
        private bool laserEnabled;
        private bool tecEnabled;

        /// <summary>
        /// 初始化主窗口并加载当前可用串口。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            InitializeSpectrumPlot();
            // 默认占当前屏幕工作区的 80%，保持普通可缩放窗口。
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size((int)(workingArea.Width * 0.80), (int)(workingArea.Height * 0.80));
            StartPosition = FormStartPosition.CenterScreen;
            Resize += MainForm_Resize;
            LayoutBrightFieldPreviewArea();
            RefreshComList();
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

                    // 连接阶段只确认设备并安全关闭激光器，不修改光谱仪的采集参数。
                    // 保留光谱仪当前已验证可用的积分时间与平均次数。
                    connectedLaser.setLDOff();
                    connectedLaser.setTECOff();
                    laserDevice = connectedLaser;
                    spectrometerDevice = connectedSpectrometer;
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
            if (laserDevice == null)
                return;

            if (laserSettingsForm == null || laserSettingsForm.IsDisposed)
            {
                Terra.Device device;
                lock (laserDeviceSync)
                    device = laserDevice;
                if (device == null)
                    return;

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

        /// <summary>同步采集当前光谱；开激光后使用同一点的关激光光谱扣除背景。</summary>
        private void AcquireSpectrumForScan(bool laserOn)
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
                intensities = AcquireValidSpectrum(device);
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

            double[] acquired = (double[])intensities.Clone();
            if (!laserOn)
            {
                laserOffSpectrum = acquired;
                ShowProcessedRamanSpectrum(wavelengths, acquired, excitationWavelength, "激光关闭背景谱");
                return;
            }

            if (laserOffSpectrum == null || laserOffSpectrum.Length != acquired.Length)
                throw new InvalidOperationException("当前扫描点缺少匹配的关激光背景光谱，扫描已安全停止。");

            double[] corrected = new double[acquired.Length];
            for (int index = 0; index < corrected.Length; index++)
                corrected[index] = acquired[index] - laserOffSpectrum[index];
            laserOffSpectrum = null;
            ShowProcessedRamanSpectrum(wavelengths, corrected, excitationWavelength, "背景扣除拉曼谱");
        }

        private static double[] AcquireValidSpectrum(Terra.Device device)
        {
            // 恢复此前实际采集成功的直接读取方式：连续读取三次，不重置积分状态。
            for (int attempt = 0; attempt < 3; attempt++)
            {
                double[] spectrum = device.getSpectrum();
                if (spectrum != null && spectrum.Length > 1)
                    return spectrum;
            }

            throw new InvalidOperationException("光谱仪未返回有效光谱，扫描已安全停止。请检查积分时间设置。");
        }

        private void ShowProcessedRamanSpectrum(
            double[] wavelengths,
            double[] intensities,
            double excitationWavelength,
            string title)
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
            ShowSpectrum(x, y, title);
        }

        private void InitializeSpectrumPlot()
        {
            const string plotFont = "Microsoft YaHei UI";
            ScottPlot.Fonts.Default = plotFont;
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
            formsPlot1.Plot.Title(title);
            formsPlot1.Plot.XLabel("拉曼位移(cm⁻¹)");
            formsPlot1.Plot.YLabel("强度");
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Refresh();
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
            if (laserSettingsForm != null && !laserSettingsForm.IsDisposed)
                laserSettingsForm.RefreshDeviceState();
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
                laserOffSpectrum = null;
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

            calibrationCancellation = new CancellationTokenSource();
            CancellationToken token = calibrationCancellation.Token;
            cameraShowForm.HideSelectionOverlayForCalibration();
            CalibrateStage.Enabled = false;
                ScanSelection.Enabled = false;
            SetLaserControlsEnabled(false);
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

            scanStartedUtc = DateTime.UtcNow;
            scanCancellation = new CancellationTokenSource();
            CancellationToken token = scanCancellation.Token;
            ScanSelection.Text = "扫描中…";
            CalibrateStage.Enabled = false;
            SetLaserControlsEnabled(false);
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
