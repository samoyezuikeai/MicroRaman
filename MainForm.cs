using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;

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
        private Terra.Device laserDevice;
        private bool laserEnabled;
        private bool tecEnabled;

        /// <summary>
        /// 初始化主窗口并加载当前可用串口。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
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
        /// 连接选中的 TANGO 串口并自动连接 Terra SDK 支持的 USB 激光器。
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ConnectCom_Click(object sender, EventArgs e)
        {
            if (comboBoxController.SelectedItem == null)
            {
                MessageBox.Show(this, "请选择控制台串口。激光器将通过 Terra SDK 自动检测。", "连接设备",
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
                UpdateLaserConnectionAppearance();
                SetLaserControlsEnabled(true);
                MessageBox.Show(this, "TANGO 控制台和 USB 激光器均已连接。", "连接设备",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                DisconnectDevices();
                UpdateLaserConnectionAppearance();
                MessageBox.Show(this, ex.Message, "设备连接失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ConnectCom.Enabled = true;
            }
        }

        /// <summary>
        /// 依次连接并验证 TANGO 串口和 Terra USB 激光器；任意一步失败都会回滚两个连接。
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
                    Terra.Device connectedDevice = FindConnectedLaserDevice(devices);
                    if (connectedDevice == null)
                        throw new InvalidOperationException(
                            "Terra SDK 未发现可用的 USB 激光器，请检查 USB 连接、驱动和设备占用状态。");
                    if (!connectedDevice.isUsbConnected())
                        throw new InvalidOperationException("已发现激光器，但 Terra SDK 报告 USB 尚未连接。");

                    if (!connectedDevice.setLDOff())
                        throw new InvalidOperationException("激光器已连接，但无法确认激光处于关闭状态。");
                    if (!connectedDevice.setTECOff())
                        throw new InvalidOperationException("激光器已连接，但无法确认 TEC 处于关闭状态。");

                    laserDevice = connectedDevice;
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
        /// 从 Terra 枚举结果中按 SDK 设备类型和 USB 状态查找激光器，不依赖 Windows 设备名称。
        /// </summary>
        private static Terra.Device FindConnectedLaserDevice(IList<Terra.Device> devices)
        {
            if (devices == null || devices.Count == 0)
                return null;

            List<Terra.Device> connectedDevices = new List<Terra.Device>();
            foreach (Terra.Device device in devices)
            {
                bool connected;
                try { connected = device.isUsbConnected(); } catch { continue; }
                if (!connected)
                    continue;

                connectedDevices.Add(device);

                string deviceType = null;
                try { deviceType = Terra.DeviceWrapper.getName(device.getIndex()); } catch { }
                if (!string.IsNullOrWhiteSpace(deviceType)
                    && deviceType.IndexOf("Laser", StringComparison.OrdinalIgnoreCase) >= 0)
                    return device;
            }

            // 某些 Terra SDK/固件版本不返回类型名称；仅有一个已连接设备时可安全交给后续 LD/TEC 指令验证。
            return connectedDevices.Count == 1 ? connectedDevices[0] : null;
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
                if (!success)
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
                if (!success)
                    throw new InvalidOperationException(enabled ? "自动打开 TEC 失败。" : "自动关闭 TEC 失败。");
                tecEnabled = enabled;
            }

            LaserSettingsForm settings = laserSettingsForm;
            if (settings != null && !settings.IsDisposed)
                settings.SetTecOutputStateFromScan(enabled);
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
        private void UpdateLaserConnectionAppearance()
        {
            label2.Text = laserDevice == null
                ? "激光器：未连接"
                : "激光器：已连接";
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
                        SetTecOutputForScan),
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
