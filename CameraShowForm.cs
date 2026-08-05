using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MicroRaman
{
    /// <summary>
    /// 相机实时预览窗口，负责 TUCam 采集、参数控制、框选绘制和定标快照。
    /// </summary>
    public partial class CameraShowForm : Form
    {
        private TucamOpen camera;
        private TucamFrame frame;
        private Thread captureThread;
        private System.Windows.Forms.Timer performanceTimer;
        private readonly Command stagePositionCommand = new Command();
        private int stagePositionQueryPending;
        private readonly Stopwatch frameRateWatch = new Stopwatch();
        private readonly object frameArrivalSync = new object();
        private readonly object snapshotCaptureSync = new object();
        private readonly object snapshotStateSync = new object();
        private readonly object overlayModelSync = new object();
        private readonly AutoResetEvent snapshotReady = new AutoResetEvent(false);
        private readonly object previewDrawSync = new object();
        private readonly object scanPreviewSync = new object();
        private readonly object selectionPreviewSync = new object();
        private RectangleSelectionOverlay selectionOverlay;
        private PictureBox frozenScanPictureBox;
        private volatile bool capturing;
        private bool apiInitialized;
        private bool bufferAllocated;
        private bool captureStarted;
        private bool drawInitialized;
        private IntPtr previewHandle;
        private int previewWidth;
        private int previewHeight;
        private int previewSurfaceClearPending;
        private int imageWidth;
        private int imageHeight;
        private int framesSinceUpdate;
        private bool rectangleToolEnabled;
        private bool drawingRectangle;
        private bool selectionOverlayHiddenForCalibration;
        private Point rectangleStart;
        private RectangleF selectionImageRegion = RectangleF.Empty;
        private RectangleF displayedSelectionImageRegion = RectangleF.Empty;
        private long capturedFrameSequence;
        private bool snapshotRequested;
        private int snapshotRequestId;
        private int snapshotFramesToSkip;
        private int snapshotMaximumDimension = 768;
        private GrayFrameSnapshot snapshotResult;
        private Exception snapshotException;
        private byte[] snapshotRawBuffer;
        private readonly List<PointF> recordedScanPointsImage = new List<PointF>();
        private Bitmap selectionReferenceFrame;
        private Bitmap frozenScanDisplayFrame;
        private bool scanPreviewFrozen;
        private bool selectionPreviewRequested;
        private int selectionPreviewRequestId;
        private RectangleF selectionPreviewRegion;
        private int selectionPreviewXCount;
        private int selectionPreviewYCount;
        private Bitmap savedSelectionFrame;
        private int savedSelectionFrameRequestId;

        internal event Action<Bitmap> SelectionPreviewUpdated;

        internal int CameraImageWidth { get { return imageWidth; } }
        internal int CameraImageHeight { get { return imageHeight; } }

        /// <summary>
        /// 初始化相机窗口及默认曝光状态。
        /// </summary>
        public CameraShowForm()
        {
            InitializeComponent();
            rectangleToolButton.Image = CreateSelectionToolIcon();
            // 默认占当前屏幕工作区的 80%，保持普通可缩放窗口。
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size((int)(workingArea.Width * 0.80), (int)(workingArea.Height * 0.80));
            StartPosition = FormStartPosition.CenterScreen;
            resolutionComboBox.SelectedIndex = 1;
            AutoExposureCheckBox_CheckedChanged(null, EventArgs.Empty);
        }

        /// <summary>
        /// 窗口首次显示后创建帧内标注绘制器并启动相机。
        /// </summary>
        private void CameraShowForm_Shown(object sender, EventArgs e)
        {
            previewWidth = previewPanel.ClientSize.Width;
            previewHeight = previewPanel.ClientSize.Height;
            previewHandle = previewPanel.Handle;
            frozenScanPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Normal,
                Visible = false
            };
            previewPanel.Controls.Add(frozenScanPictureBox);
            selectionOverlay = new RectangleSelectionOverlay();
            UpdateOverlayGridSize();

            performanceTimer = new System.Windows.Forms.Timer { Interval = 500 };
            performanceTimer.Tick += PerformanceTimer_Tick;
            performanceTimer.Start();
            StartCamera();
        }

        /// <summary>
        /// 初始化 SDK、打开设备、配置采集并启动后台采集线程。
        /// </summary>
        private void StartCamera()
        {
            IntPtr configPath = IntPtr.Zero;
            try
            {
                ShowCameraStatus("正在以低延迟对焦参数连接 MIchrome 20…", Color.Gainsboro);
                configPath = Marshal.StringToHGlobalAnsi(AppDomain.CurrentDomain.BaseDirectory);
                TucamInit init = new TucamInit { ConfigPath = configPath };
                EnsureSuccess(TUCamNative.TUCAM_Api_Init(ref init, 1000), "初始化 SDK");
                apiInitialized = true;

                if (init.CameraCount == 0)
                    throw new InvalidOperationException("未发现 TUCam 相机。请关闭官方软件或其他正在使用相机的 BoardControl 窗口后重试。");

                camera = new TucamOpen { Index = 0 };
                EnsureSuccess(TUCamNative.TUCAM_Dev_Open(ref camera), "打开第一个相机");

                ApplyFocusSettings();

                // 关闭队列模式，让 WaitForFrame 优先取得最新帧，避免绘制稍慢时累积延迟。
                TUCamNative.TUCAM_Vendor_SetQueueMode(camera.CameraHandle, false);

                frame = new TucamFrame
                {
                    Signature = new byte[8],
                    RequestedFormat = TUCamNative.UsualFrameFormat,
                    ReservedSize = 1
                };
                EnsureSuccess(TUCamNative.TUCAM_Buf_Alloc(camera.CameraHandle, ref frame), "分配图像缓冲区");
                bufferAllocated = true;

                EnsureSuccess(TUCamNative.TUCAM_Cap_Start(camera.CameraHandle, TUCamNative.SequenceCaptureMode), "开始连续采集");
                captureStarted = true;
                Interlocked.Exchange(ref framesSinceUpdate, 0);
                frameRateWatch.Restart();
                capturing = true;
                captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "TUCam low-latency capture"
                };
                captureThread.Start();
            }
            catch (DllNotFoundException)
            {
                ShowCameraError("找不到 TUCam.dll 或其依赖项。请确认官方 SDK 已安装且项目使用 x64 构建。");
                StopCamera();
            }
            catch (BadImageFormatException)
            {
                ShowCameraError("TUCam.dll 与程序位数不一致。当前项目应使用 x64 构建。");
                StopCamera();
            }
            catch (Exception ex)
            {
                ShowCameraError(ex.Message);
                StopCamera();
            }
            finally
            {
                if (configPath != IntPtr.Zero)
                    Marshal.FreeHGlobal(configPath);
            }
        }

        /// <summary>
        /// 将分辨率、曝光方式、曝光时间和增益写入相机。
        /// </summary>
        private void ApplyFocusSettings()
        {
            int resolution = Math.Max(0, resolutionComboBox.SelectedIndex);
            EnsureSuccess(
                TUCamNative.TUCAM_Capa_SetValue(camera.CameraHandle, TUCamNative.ResolutionCapability, resolution),
                "设置预览分辨率");

            if (autoExposureCheckBox.Checked)
            {
                // 与官方配套软件一致：由 SDK 根据当前显微镜照明持续调整亮度。
                EnsureSuccess(
                    TUCamNative.TUCAM_Capa_SetValue(camera.CameraHandle, TUCamNative.AutoExposureCapability, 1),
                    "开启自动曝光");
            }
            else
            {
                EnsureSuccess(
                    TUCamNative.TUCAM_Capa_SetValue(camera.CameraHandle, TUCamNative.AutoExposureCapability, 0),
                    "关闭自动曝光");
                EnsureSuccess(
                    TUCamNative.TUCAM_Prop_SetValue(camera.CameraHandle, TUCamNative.ExposureTimeProperty, (double)exposureNumeric.Value, 0),
                    "设置曝光时间");
                EnsureSuccess(
                    TUCamNative.TUCAM_Prop_SetValue(camera.CameraHandle, TUCamNative.GlobalGainProperty, (double)gainNumeric.Value, 0),
                    "设置全局增益");
            }
        }

        /// <summary>
        /// 持续接收相机帧并完成快照、显示和标注合成。
        /// </summary>
        private void CaptureLoop()
        {
            IntPtr framePointer = IntPtr.Zero;
            try
            {
                framePointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TucamFrame)));
                while (capturing)
                {
                    TucamResult result = TUCamNative.TUCAM_Buf_WaitForFrame(camera.CameraHandle, ref frame, 1000);
                    if (result == TucamResult.Timeout)
                        continue;
                    if (result == TucamResult.Abort && !capturing)
                        break;
                    EnsureSuccess(result, "获取相机图像");

                    imageWidth = frame.Width;
                    imageHeight = frame.Height;
                    Interlocked.Increment(ref framesSinceUpdate);
                    Interlocked.Increment(ref capturedFrameSequence);
                    lock (frameArrivalSync)
                        Monitor.PulseAll(frameArrivalSync);
                    FulfillSnapshotRequest();
                    FulfillSelectionPreviewRequest();

                    if (!drawInitialized)
                    {
                        InitializeNativeDrawing();
                        HideStatusLabel();
                    }

                    Marshal.StructureToPtr(frame, framePointer, false);
                    DrawFrameWithRecovery(framePointer);
                }
            }
            catch (Exception ex)
            {
                if (capturing)
                {
                    capturing = false;
                    ShowCameraError(ex.Message);
                }
            }
            finally
            {
                if (framePointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(framePointer);
            }
        }

        /// <summary>
        /// 使用预览窗口句柄初始化 TUCam 原生绘制器。
        /// </summary>
        private void InitializeNativeDrawing()
        {
            TucamDrawInit drawInit = new TucamDrawInit
            {
                WindowHandle = previewHandle,
                // 官方 WinForms 示例使用 TUDRAW_DFT。SDK 会自行选择兼容的绘制后端。
                Mode = TUCamNative.DefaultDrawMode,
                Channels = (sbyte)frame.Channels,
                Width = frame.Width,
                Height = frame.Height
            };
            EnsureSuccess(TUCamNative.TUCAM_Draw_Init(camera.CameraHandle, drawInit), "初始化图像显示");
            drawInitialized = true;
        }

        /// <summary>
        /// 绘制当前帧，并立即在同一显示表面叠加扫描标注。
        /// </summary>
        private void DrawFrameWithRecovery(IntPtr framePointer)
        {
            TucamResult result;
            lock (previewDrawSync)
            {
                if (IsScanPreviewFrozen())
                    return;

                if (Interlocked.Exchange(ref previewSurfaceClearPending, 0) != 0 && previewHandle != IntPtr.Zero)
                {
                    using (Graphics graphics = Graphics.FromHwnd(previewHandle))
                        graphics.Clear(Color.Black);
                }

                TucamDraw draw = CreateDrawRectangle(framePointer, frame.Width, frame.Height);
                result = TUCamNative.TUCAM_Draw_Frame(camera.CameraHandle, ref draw);
                RectangleSelectionOverlay overlay = selectionOverlay;
                if (result == TucamResult.Success && overlay != null && previewHandle != IntPtr.Zero)
                {
                    using (Graphics graphics = Graphics.FromHwnd(previewHandle))
                        overlay.Draw(graphics, new Size(previewWidth, previewHeight));
                }
            }
            if (result == TucamResult.Success)
                return;

            EnsureSuccess(result, "显示相机图像");
        }

        /// <summary>
        /// 检查扫描冻结预览是否已接管显示区域。
        /// </summary>
        private bool IsScanPreviewFrozen()
        {
            lock (scanPreviewSync)
                return scanPreviewFrozen;
        }

        /// <summary>
        /// 按相机预览比例绘制冻结图像及固定扫描网格。
        /// </summary>
        private void DrawFrozenScanPreview(Graphics graphics, Size clientSize)
        {
            graphics.Clear(Color.Black);
            if (selectionReferenceFrame != null)
            {
                Rectangle source;
                Rectangle destination;
                GetCameraViewRectangles(
                    selectionReferenceFrame.Width,
                    selectionReferenceFrame.Height,
                    out source,
                    out destination);
                graphics.DrawImage(selectionReferenceFrame, destination, source, GraphicsUnit.Pixel);
            }

            RectangleSelectionOverlay overlay = selectionOverlay;
            if (overlay != null)
                overlay.Draw(graphics, clientSize);
        }

        /// <summary>
        /// 根据当前窗口尺寸构造 SDK 帧绘制参数。
        /// </summary>
        private TucamDraw CreateDrawRectangle(IntPtr framePointer, int frameWidth, int frameHeight)
        {
            Rectangle source;
            Rectangle destination;
            GetCameraViewRectangles(frameWidth, frameHeight, out source, out destination);
            return new TucamDraw
            {
                SourceX = source.X,
                SourceY = source.Y,
                SourceWidth = source.Width,
                SourceHeight = source.Height,
                DestinationX = destination.X,
                DestinationY = destination.Y,
                DestinationWidth = destination.Width,
                DestinationHeight = destination.Height,
                Frame = framePointer
            };
        }

        /// <summary>
        /// 计算保持宽高比的相机源区域和预览目标区域。
        /// </summary>
        private void GetCameraViewRectangles(int frameWidth, int frameHeight, out Rectangle source, out Rectangle destination)
        {
            int targetWidth = Math.Max(4, previewWidth);
            int targetHeight = Math.Max(4, previewHeight);

            double scale = Math.Min((double)targetWidth / frameWidth, (double)targetHeight / frameHeight);
            int width = AlignToFour((int)(frameWidth * scale));
            int height = AlignToFour((int)(frameHeight * scale));
            source = new Rectangle(0, 0, frameWidth, frameHeight);
            destination = new Rectangle(
                (targetWidth - width) / 2,
                (targetHeight - height) / 2,
                width,
                height);
        }

        /// <summary>
        /// 将显示尺寸向下对齐到 4 像素边界。
        /// </summary>
        private static int AlignToFour(int value)
        {
            return Math.Max(4, (value / 4) * 4);
        }

        /// <summary>
        /// 检查 SDK 返回码，并在失败时抛出带操作名的异常。
        /// </summary>
        private static void EnsureSuccess(TucamResult result, string operation)
        {
            if (result != TucamResult.Success)
                throw new InvalidOperationException(string.Format("{0}失败（TUCAM 返回码 0x{1:X8}）。", operation, (uint)result));
        }

        /// <summary>
        /// 定时刷新 FPS、分辨率和自动曝光后的实时参数。
        /// </summary>
        private void PerformanceTimer_Tick(object sender, EventArgs e)
        {
            long elapsed = Math.Max(1, frameRateWatch.ElapsedMilliseconds);
            int frames = Interlocked.Exchange(ref framesSinceUpdate, 0);
            double fps = frames * 1000.0 / elapsed;
            frameRateWatch.Restart();
            performanceLabel.Text = string.Format(
                "{0:F1} FPS | {1}×{2}",
                fps,
                imageWidth,
                imageHeight);

            if (autoExposureCheckBox.Checked && camera.CameraHandle != IntPtr.Zero)
                UpdateAutomaticExposureValues();

            QueueStagePositionUpdate();
        }

        /// <summary>
        /// 在后台线程中使用与扫描相同的 ?pos 指令持续刷新平台坐标。
        /// </summary>
        private void QueueStagePositionUpdate()
        {
            if (Interlocked.CompareExchange(ref stagePositionQueryPending, 1, 0) != 0)
                return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    StagePosition position = stagePositionCommand.ReadPosition();

                    RunOnUiThread(delegate
                    {
                        stagePositionLabel.Text = string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "平台坐标\r\nX: {0:F4} mm\r\nY: {1:F4} mm\r\nZ: {2:F4} mm",
                            position.X,
                            position.Y,
                            position.Z);
                    });
                }
                catch (Exception ex)
                {
                    RunOnUiThread(delegate
                    {
                        stagePositionLabel.Text = "平台坐标\r\n读取失败\r\n" + ex.Message;
                    });
                }
                finally
                {
                    Interlocked.Exchange(ref stagePositionQueryPending, 0);
                }
            });
        }
        /// <summary>
        /// 读取自动曝光当前计算出的曝光时间和增益。
        /// </summary>
        private void UpdateAutomaticExposureValues()
        {
            double exposure = 0;
            double gain = 0;
            if (TUCamNative.TUCAM_Prop_GetValue(camera.CameraHandle, TUCamNative.ExposureTimeProperty, ref exposure, 0) == TucamResult.Success)
                exposureNumeric.Value = ClampDecimal((decimal)exposure, exposureNumeric.Minimum, exposureNumeric.Maximum);
            if (TUCamNative.TUCAM_Prop_GetValue(camera.CameraHandle, TUCamNative.GlobalGainProperty, ref gain, 0) == TucamResult.Success)
                gainNumeric.Value = ClampDecimal((decimal)gain, gainNumeric.Minimum, gainNumeric.Maximum);
        }

        /// <summary>
        /// 将数值限制到控件允许的范围。
        /// </summary>
        private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
        {
            return Math.Min(maximum, Math.Max(minimum, value));
        }

        /// <summary>
        /// 自动曝光开关变化时同步手动参数控件状态。
        /// </summary>
        private void AutoExposureCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            bool manual = !autoExposureCheckBox.Checked;
            exposureNumeric.Enabled = manual;
            gainNumeric.Enabled = manual;
        }

        /// <summary>
        /// 重新启动采集以应用当前相机参数。
        /// </summary>
        private void ApplySettingsButton_Click(object sender, EventArgs e)
        {
            applySettingsButton.Enabled = false;
            try
            {
                StopCamera();
                StartCamera();
            }
            finally
            {
                applySettingsButton.Enabled = true;
            }
        }

        /// <summary>
        /// 切换矩形框选模式及鼠标样式。
        /// </summary>
        private void RectangleToolButton_Click(object sender, EventArgs e)
        {
            rectangleToolEnabled = !rectangleToolEnabled;
            if (!rectangleToolEnabled)
                CancelRectangleDrawing();
            UpdateRectangleToolAppearance();
        }

        /// <summary>
        /// 退出框选模式并恢复普通鼠标与按钮外观。
        /// </summary>
        private void ExitRectangleTool()
        {
            rectangleToolEnabled = false;
            UpdateRectangleToolAppearance();
        }

        /// <summary>
        /// 同步框选按钮的激活外观。
        /// </summary>
        private void UpdateRectangleToolAppearance()
        {
            previewPanel.Cursor = rectangleToolEnabled ? Cursors.Cross : Cursors.Default;
            rectangleToolButton.BackColor = rectangleToolEnabled
                ? Color.FromArgb(55, 95, 115)
                : controlPanel.BackColor;
            rectangleToolButton.FlatAppearance.BorderColor = rectangleToolEnabled
                ? Color.DeepSkyBlue
                : Color.DimGray;
            rectangleToolButton.Invalidate();
        }

        /// <summary>
        /// 创建与截图工具一致的虚线选区和蓝色控制点图标。
        /// </summary>
        private static Bitmap CreateSelectionToolIcon()
        {
            Bitmap icon = new Bitmap(24, 24);
            using (Graphics graphics = Graphics.FromImage(icon))
            using (Pen selectionPen = new Pen(Color.OrangeRed, 2F))
            using (Pen handlePen = new Pen(Color.DeepSkyBlue, 1.5F))
            using (Brush handleBrush = new SolidBrush(Color.White))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                selectionPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                graphics.DrawRectangle(selectionPen, 2, 2, 15, 15);

                Rectangle rightHandle = new Rectangle(15, 8, 6, 6);
                Rectangle bottomHandle = new Rectangle(8, 15, 6, 6);
                graphics.FillEllipse(handleBrush, rightHandle);
                graphics.DrawEllipse(handlePen, rightHandle);
                graphics.FillEllipse(handleBrush, bottomHandle);
                graphics.DrawEllipse(handlePen, bottomHandle);
            }
            return icon;
        }
        /// <summary>
        /// X/Y 点数变化时立即更新预览网格。
        /// </summary>
        private void ScanPointCount_ValueChanged(object sender, EventArgs e)
        {
            UpdateOverlayGridSize();
            if (!selectionImageRegion.IsEmpty && capturing && !scanPreviewFrozen)
                RequestSelectionPreview();
        }

        /// <summary>
        /// 将当前 X/Y 点数写入标注绘制器。
        /// </summary>
        private void UpdateOverlayGridSize()
        {
            if (selectionOverlay == null)
                return;

            selectionOverlay.SetGridSize((int)xPointCountNumeric.Value, (int)yPointCountNumeric.Value);
        }

        /// <summary>
        /// 开始新的矩形拖拽，并清除上一轮扫描标记。
        /// </summary>
        private void PreviewPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (!rectangleToolEnabled || scanPreviewFrozen || e.Button != MouseButtons.Left)
                return;

            Rectangle source;
            Rectangle destination;
            if (!TryGetCameraViewRectangles(out source, out destination) || !destination.Contains(e.Location))
                return;

            CancelRectangleDrawing();
            recordedScanPointsImage.Clear();
            selectionImageRegion = RectangleF.Empty;
            displayedSelectionImageRegion = RectangleF.Empty;
            if (selectionOverlay != null)
            {
                selectionOverlay.ClearSelection();
                selectionOverlay.SetRecordedScanPoints(null);
            }
            rectangleStart = ClampToRectangle(e.Location, destination);
            drawingRectangle = true;
            previewPanel.Capture = true;
        }

        /// <summary>
        /// 拖拽过程中实时更新临时矩形。
        /// </summary>
        private void PreviewPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (!drawingRectangle)
                return;

            Rectangle source;
            Rectangle destination;
            if (!TryGetCameraViewRectangles(out source, out destination))
                return;

            Rectangle rectangle = NormalizeRectangle(rectangleStart, ClampToRectangle(e.Location, destination));
            ShowOverlayClientRectangle(rectangle);
        }

        /// <summary>
        /// 结束拖拽，将有效矩形保存为归一化图像区域。
        /// </summary>
        private void PreviewPanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (!drawingRectangle || e.Button != MouseButtons.Left)
                return;

            Rectangle source;
            Rectangle destination;
            if (!TryGetCameraViewRectangles(out source, out destination))
            {
                CancelRectangleDrawing();
                return;
            }

            Rectangle completed = NormalizeRectangle(rectangleStart, ClampToRectangle(e.Location, destination));
            drawingRectangle = false;
            previewPanel.Capture = false;
            if (completed.Width > 3 && completed.Height > 3)
            {
                selectionImageRegion = ClientToImageRegion(completed, source, destination);
                displayedSelectionImageRegion = selectionImageRegion;
                UpdateSelectionOverlayFromImageCoordinates();
                RequestSelectionPreview();
                ExitRectangleTool();
            }
            else
            {
                selectionImageRegion = RectangleF.Empty;
                displayedSelectionImageRegion = RectangleF.Empty;
                if (selectionOverlay != null)
                    selectionOverlay.ClearSelection();
            }
        }

        /// <summary>
        /// 取消尚未完成的矩形拖拽。
        /// </summary>
        private void CancelRectangleDrawing()
        {
            if (!drawingRectangle)
                return;

            drawingRectangle = false;
            previewPanel.Capture = false;
            selectionImageRegion = RectangleF.Empty;
            displayedSelectionImageRegion = RectangleF.Empty;
            if (selectionOverlay != null)
                selectionOverlay.ClearSelection();
        }

        /// <summary>
        /// 把预览控件像素矩形转换成绘制器使用的归一化矩形。
        /// </summary>
        private void ShowOverlayClientRectangle(Rectangle rectangle)
        {
            int clientWidth = previewWidth;
            int clientHeight = previewHeight;
            if (selectionOverlay == null || clientWidth <= 0 || clientHeight <= 0)
                return;

            if (rectangle.IsEmpty)
            {
                selectionOverlay.ClearSelection();
                return;
            }

            selectionOverlay.SetSelection(new RectangleF(
                (float)rectangle.X / clientWidth,
                (float)rectangle.Y / clientHeight,
                (float)rectangle.Width / clientWidth,
                (float)rectangle.Height / clientHeight));
        }

        /// <summary>
        /// 获取当前有效的相机源区域和预览目标区域。
        /// </summary>
        private bool TryGetCameraViewRectangles(out Rectangle source, out Rectangle destination)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                source = Rectangle.Empty;
                destination = Rectangle.Empty;
                return false;
            }

            GetCameraViewRectangles(imageWidth, imageHeight, out source, out destination);
            return source.Width > 0 && source.Height > 0 && destination.Width > 0 && destination.Height > 0;
        }

        /// <summary>
        /// 将预览控件中的矩形换算为归一化相机图像区域。
        /// </summary>
        private RectangleF ClientToImageRegion(Rectangle rectangle, Rectangle source, Rectangle destination)
        {
            float left = source.Left + (rectangle.Left - destination.Left) * (float)source.Width / destination.Width;
            float top = source.Top + (rectangle.Top - destination.Top) * (float)source.Height / destination.Height;
            float right = source.Left + (rectangle.Right - destination.Left) * (float)source.Width / destination.Width;
            float bottom = source.Top + (rectangle.Bottom - destination.Top) * (float)source.Height / destination.Height;
            return RectangleF.FromLTRB(
                left / imageWidth,
                top / imageHeight,
                right / imageWidth,
                bottom / imageHeight);
        }

        /// <summary>
        /// 按当前图像位移和窗口缩放更新框线及红点显示位置。
        /// </summary>
        private void UpdateSelectionOverlayFromImageCoordinates()
        {
            lock (overlayModelSync)
            {
                if (selectionOverlay == null || drawingRectangle)
                    return;

                if (selectionOverlayHiddenForCalibration)
                {
                    selectionOverlay.ClearSelection();
                    return;
                }

                if (displayedSelectionImageRegion.IsEmpty)
                {
                    selectionOverlay.ClearSelection();
                    return;
                }

                Rectangle source;
                Rectangle destination;
                if (!TryGetCameraViewRectangles(out source, out destination))
                    return;

                float imageLeft = displayedSelectionImageRegion.Left * imageWidth;
                float imageTop = displayedSelectionImageRegion.Top * imageHeight;
                float imageRight = displayedSelectionImageRegion.Right * imageWidth;
                float imageBottom = displayedSelectionImageRegion.Bottom * imageHeight;
                Rectangle clientRectangle = Rectangle.FromLTRB(
                    (int)Math.Round(destination.Left + (imageLeft - source.Left) * destination.Width / source.Width),
                    (int)Math.Round(destination.Top + (imageTop - source.Top) * destination.Height / source.Height),
                    (int)Math.Round(destination.Left + (imageRight - source.Left) * destination.Width / source.Width),
                    (int)Math.Round(destination.Top + (imageBottom - source.Top) * destination.Height / source.Height));

                clientRectangle = Rectangle.Intersect(clientRectangle, destination);
                ShowOverlayClientRectangle(clientRectangle);
                UpdateRecordedScanPointsOverlay(source, destination);
            }
        }

        /// <summary>
        /// 定标期间临时隐藏已有框选，不修改框选的原始图像坐标。
        /// </summary>
        internal void HideSelectionOverlayForCalibration()
        {
            RunOnUiThread(() =>
            {
                if (selectionImageRegion.IsEmpty)
                    return;
                selectionOverlayHiddenForCalibration = true;
                if (selectionOverlay != null)
                    selectionOverlay.ClearSelection();
            });
        }

        /// <summary>
        /// 定标结束后恢复定标前的框选显示。
        /// </summary>
        internal void RestoreSelectionOverlayAfterCalibration()
        {
            RunOnUiThread(() =>
            {
                if (!selectionOverlayHiddenForCalibration)
                    return;
                selectionOverlayHiddenForCalibration = false;
                UpdateSelectionOverlayFromImageCoordinates();
            });
        }

        /// <summary>
        /// 将记录在原始图像坐标中的红点转换为当前预览坐标。
        /// </summary>
        private void UpdateRecordedScanPointsOverlay(Rectangle source, Rectangle destination)
        {
            int clientWidth = previewWidth;
            int clientHeight = previewHeight;
            if (selectionOverlay == null || clientWidth <= 0 || clientHeight <= 0)
                return;

            float offsetX = displayedSelectionImageRegion.X - selectionImageRegion.X;
            float offsetY = displayedSelectionImageRegion.Y - selectionImageRegion.Y;
            List<PointF> clientPoints = new List<PointF>(recordedScanPointsImage.Count);
            foreach (PointF point in recordedScanPointsImage)
            {
                float imageX = (point.X + offsetX) * imageWidth;
                float imageY = (point.Y + offsetY) * imageHeight;
                float clientX = destination.Left + (imageX - source.Left) * destination.Width / source.Width;
                float clientY = destination.Top + (imageY - source.Top) * destination.Height / source.Height;
                clientPoints.Add(new PointF(
                    clientX / clientWidth,
                    clientY / clientHeight));
            }
            selectionOverlay.SetRecordedScanPoints(clientPoints);
        }

        /// <summary>
        /// 将鼠标点限制在相机实际显示区域内。
        /// </summary>
        private static Point ClampToRectangle(Point point, Rectangle rectangle)
        {
            return new Point(
                Math.Max(rectangle.Left, Math.Min(rectangle.Right, point.X)),
                Math.Max(rectangle.Top, Math.Min(rectangle.Bottom, point.Y)));
        }

        /// <summary>
        /// 由任意拖拽方向的起止点生成左上到右下矩形。
        /// </summary>
        private static Rectangle NormalizeRectangle(Point start, Point end)
        {
            return Rectangle.FromLTRB(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X),
                Math.Max(start.Y, end.Y));
        }

        /// <summary>
        /// 根据框选区域和点数生成从左到右、再从右到左的蛇形路径。
        /// </summary>
        public bool TryGetSnakeScanPoints(
            out List<PointF> normalizedImagePoints,
            out string errorMessage,
            out float selectionPixelAspectRatio)
        {
            normalizedImagePoints = new List<PointF>();
            errorMessage = null;
            selectionPixelAspectRatio = 1f;

            if (selectionImageRegion.IsEmpty)
            {
                errorMessage = "请先在相机窗口中框选扫描区域。";
                return false;
            }

            selectionPixelAspectRatio = selectionImageRegion.Width * imageWidth
                / Math.Max(0.0001f, selectionImageRegion.Height * imageHeight);

            int xPointCount = (int)xPointCountNumeric.Value;
            int yPointCount = (int)yPointCountNumeric.Value;
            if (xPointCount < 2 || yPointCount < 2)
            {
                errorMessage = "X、Y 方向的扫描点数都不得少于 2（最小矩阵为 2×2）。";
                return false;
            }
            for (int row = 0; row < yPointCount; row++)
            {
                for (int step = 0; step < xPointCount; step++)
                {
                    int column = row % 2 == 0 ? step : xPointCount - 1 - step;
                    float x = xPointCount == 1
                        ? selectionImageRegion.Left + selectionImageRegion.Width / 2f
                        : selectionImageRegion.Left + column * selectionImageRegion.Width / (xPointCount - 1);
                    float y = yPointCount == 1
                        ? selectionImageRegion.Top + selectionImageRegion.Height / 2f
                        : selectionImageRegion.Top + row * selectionImageRegion.Height / (yPointCount - 1);
                    normalizedImagePoints.Add(new PointF(x, y));
                }
            }

            return true;
        }

        /// <summary>
        /// 按默认 768 像素级配准尺寸取得一张未来灰度帧。
        /// </summary>
        internal GrayFrameSnapshot CaptureGrayFrame(int framesToSkip, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            return CaptureGrayFrame(framesToSkip, timeoutMilliseconds, cancellationToken, 768);
        }

        /// <summary>
        /// 等待指定数量的新帧后，取得指定最大尺寸的灰度快照。
        /// </summary>
        internal GrayFrameSnapshot CaptureGrayFrame(
            int framesToSkip,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            int maximumDimension)
        {
            lock (snapshotCaptureSync)
            {
                snapshotReady.Reset();
                lock (snapshotStateSync)
                {
                    if (!capturing)
                        throw new InvalidOperationException("相机未开始采集，无法执行图像标定。");
                    snapshotResult = null;
                    snapshotException = null;
                    snapshotFramesToSkip = Math.Max(0, framesToSkip);
                    snapshotMaximumDimension = Math.Max(128, maximumDimension);
                    snapshotRequestId++;
                    snapshotRequested = true;
                }

                int waitResult = WaitHandle.WaitAny(
                    new WaitHandle[] { snapshotReady, cancellationToken.WaitHandle },
                    timeoutMilliseconds);
                if (waitResult == 1)
                {
                    CancelSnapshotRequest();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (waitResult == WaitHandle.WaitTimeout)
                {
                    CancelSnapshotRequest();
                    throw new TimeoutException("等待相机校准帧超时。");
                }

                lock (snapshotStateSync)
                {
                    if (snapshotException != null)
                        throw new InvalidOperationException("读取相机校准帧失败。", snapshotException);
                    if (snapshotResult == null)
                        throw new InvalidOperationException("没有取得有效的相机校准帧。");
                    return snapshotResult;
                }
            }
        }

        /// <summary>
        /// 等待相机产生指定数量的新帧，用于确保平台移动后的画面已经刷新。
        /// </summary>
        internal void WaitForFreshFrames(int count, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            long target = Interlocked.Read(ref capturedFrameSequence) + Math.Max(1, count);
            Stopwatch timeout = Stopwatch.StartNew();
            lock (frameArrivalSync)
            {
                while (Interlocked.Read(ref capturedFrameSequence) < target)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int remaining = timeoutMilliseconds - (int)timeout.ElapsedMilliseconds;
                    if (remaining <= 0 || !Monitor.Wait(frameArrivalSync, Math.Min(remaining, 200)))
                    {
                        if (timeout.ElapsedMilliseconds >= timeoutMilliseconds)
                            throw new TimeoutException("等待相机刷新画面超时。");
                    }
                }
            }
        }

        /// <summary>
        /// 在 UI 线程设置框选区域相对原始图像的临时像素位移。
        /// </summary>
        internal void SetTemporaryOverlayPixelOffset(float pixelX, float pixelY)
        {
            RunOnUiThread(() => ApplyTemporaryOverlayPixelOffset(pixelX, pixelY));
        }

        /// <summary>
        /// 应用像素位移并立即刷新线程安全的标注绘图模型。
        /// </summary>
        private void ApplyTemporaryOverlayPixelOffset(float pixelX, float pixelY)
        {
            lock (overlayModelSync)
            {
                if (selectionImageRegion.IsEmpty || imageWidth <= 0 || imageHeight <= 0)
                    return;
                displayedSelectionImageRegion = new RectangleF(
                    selectionImageRegion.X + pixelX / imageWidth,
                    selectionImageRegion.Y + pixelY / imageHeight,
                    selectionImageRegion.Width,
                    selectionImageRegion.Height);
                UpdateSelectionOverlayFromImageCoordinates();
            }
        }

        /// <summary>
        /// 开始新扫描前清空历史红点并把框选区域恢复到原始位置。
        /// </summary>
        internal void PrepareForNewScan()
        {
            RunOnUiThread(() =>
            {
                displayedSelectionImageRegion = selectionImageRegion;
                recordedScanPointsImage.Clear();
                if (selectionOverlay != null)
                    selectionOverlay.SetRecordedScanPoints(null);
                UpdateSelectionOverlayFromImageCoordinates();
            });
        }

        /// <summary>
        /// 将刚完成检测的蛇形路径点标为红色。
        /// </summary>
        internal void RecordScanVisit(PointF normalizedImagePoint)
        {
            RunOnUiThread(() =>
            {
                if (imageWidth <= 0 || imageHeight <= 0)
                    return;

                recordedScanPointsImage.Add(normalizedImagePoint);
                if (IsScanPreviewFrozen())
                    DrawCompletedScanPointOnFrozenPreview(normalizedImagePoint);
                else
                    UpdateSelectionOverlayFromImageCoordinates();
            });
        }

        /// <summary>
        /// 将刚完成的一个扫描点直接叠加到冻结位图，避免重画完整高密度网格。
        /// </summary>
        private void DrawCompletedScanPointOnFrozenPreview(PointF normalizedImagePoint)
        {
            if (frozenScanDisplayFrame == null || frozenScanPictureBox == null)
                return;

            Rectangle source;
            Rectangle destination;
            if (!TryGetCameraViewRectangles(out source, out destination))
                return;

            float imageX = normalizedImagePoint.X * imageWidth;
            float imageY = normalizedImagePoint.Y * imageHeight;
            float clientX = destination.Left + (imageX - source.Left) * destination.Width / source.Width;
            float clientY = destination.Top + (imageY - source.Top) * destination.Height / source.Height;
            const float radius = 3f;
            RectangleF marker = new RectangleF(
                clientX - radius,
                clientY - radius,
                radius * 2,
                radius * 2);
            using (Graphics graphics = Graphics.FromImage(frozenScanDisplayFrame))
            using (Brush fill = new SolidBrush(Color.Red))
            using (Pen outline = new Pen(Color.White, 1f))
            {
                graphics.FillEllipse(fill, marker);
                graphics.DrawEllipse(outline, marker);
            }
            frozenScanPictureBox.Invalidate(Rectangle.Ceiling(marker));
        }

        /// <summary>
        /// 框选完成后请求下一张明场帧，并保存当次框选和完整网格参数。
        /// </summary>
        private void RequestSelectionPreview()
        {
            lock (selectionPreviewSync)
            {
                selectionPreviewRequestId++;
                selectionPreviewRegion = selectionImageRegion;
                selectionPreviewXCount = (int)xPointCountNumeric.Value;
                selectionPreviewYCount = (int)yPointCountNumeric.Value;
                selectionPreviewRequested = true;
            }
        }

        /// <summary>
        /// 把青色框和所有黄色网格点直接画入明场展示图。
        /// </summary>
        private static void DrawSelectionPreviewOverlay(
            Bitmap bitmap,
            RectangleF normalizedRegion,
            int xCount,
            int yCount)
        {
            if (bitmap == null || normalizedRegion.IsEmpty)
                return;

            float left = normalizedRegion.Left * bitmap.Width;
            float top = normalizedRegion.Top * bitmap.Height;
            float width = normalizedRegion.Width * bitmap.Width;
            float height = normalizedRegion.Height * bitmap.Height;
            float borderWidth = Math.Max(2f, Math.Min(bitmap.Width, bitmap.Height) / 500f);
            // 位图会在 MainForm 中按比例缩小，使用与缩放后约 3~4 像素相当的源图半径。
            float radius = Math.Max(6f, Math.Min(bitmap.Width, bitmap.Height) / 180f);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen shadow = new Pen(Color.Black, borderWidth + 2f))
            using (Pen border = new Pen(Color.DeepSkyBlue, borderWidth))
            using (Brush pointBrush = new SolidBrush(Color.Yellow))
            using (Pen pointOutline = new Pen(Color.Black, 1f))
            {
                graphics.DrawRectangle(shadow, left, top, width, height);
                graphics.DrawRectangle(border, left, top, width, height);
                for (int yIndex = 0; yIndex < yCount; yIndex++)
                {
                    float y = yCount == 1 ? top + height / 2f : top + yIndex * height / (yCount - 1f);
                    for (int xIndex = 0; xIndex < xCount; xIndex++)
                    {
                        float x = xCount == 1 ? left + width / 2f : left + xIndex * width / (xCount - 1f);
                        RectangleF marker = new RectangleF(x - radius, y - radius, radius * 2f, radius * 2f);
                        graphics.FillEllipse(pointBrush, marker);
                        graphics.DrawEllipse(pointOutline, marker);
                    }
                }
            }
        }

        /// <summary>
        /// 蛇形扫描开始时冻结最近一次框选后保存的明场图。
        /// </summary>
        internal void BeginFrozenScanPreview(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearSelectionReferenceFrame();

            lock (selectionPreviewSync)
            {
                Stopwatch timeout = Stopwatch.StartNew();
                while ((savedSelectionFrame == null
                        || savedSelectionFrameRequestId != selectionPreviewRequestId)
                    && timeout.ElapsedMilliseconds < 5000)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Monitor.Wait(selectionPreviewSync, 100);
                }
                if (savedSelectionFrame != null
                    && savedSelectionFrameRequestId == selectionPreviewRequestId)
                    selectionReferenceFrame = new Bitmap(savedSelectionFrame);
            }
            if (selectionReferenceFrame == null)
                throw new InvalidOperationException("尚未保存框选明场图，请重新画框后再执行蛇形扫描。");

            lock (previewDrawSync)
            lock (scanPreviewSync)
                scanPreviewFrozen = true;

            RunOnUiThread(() =>
            {
                rectangleToolButton.Enabled = false;
                xPointCountNumeric.Enabled = false;
                yPointCountNumeric.Enabled = false;
                RefreshFrozenScanPreview();
            });
        }

        /// <summary>
        /// 扫描完成或停止后恢复实时视频和原始框选位置。
        /// </summary>
        internal void EndFrozenScanPreview()
        {
            lock (previewDrawSync)
            lock (scanPreviewSync)
            {
                scanPreviewFrozen = false;
                if (selectionReferenceFrame != null)
                {
                    selectionReferenceFrame.Dispose();
                    selectionReferenceFrame = null;
                }
            }

            RunOnUiThread(() =>
            {
                displayedSelectionImageRegion = selectionImageRegion;
                rectangleToolButton.Enabled = true;
                xPointCountNumeric.Enabled = true;
                yPointCountNumeric.Enabled = true;
                UpdateSelectionOverlayFromImageCoordinates();
                HideFrozenScanPreview();
            });
        }

        /// <summary>
        /// 生成完整冻结预览；只在开始或窗口缩放时更新。
        /// </summary>
        private void RefreshFrozenScanPreview()
        {
            if (!IsScanPreviewFrozen() || frozenScanPictureBox == null)
                return;

            int width = Math.Max(1, previewPanel.ClientSize.Width);
            int height = Math.Max(1, previewPanel.ClientSize.Height);
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                lock (scanPreviewSync)
                {
                    if (!scanPreviewFrozen)
                        return;
                    DrawFrozenScanPreview(graphics, new Size(width, height));
                }

                Bitmap old = frozenScanDisplayFrame;
                frozenScanDisplayFrame = bitmap;
                bitmap = null;
                frozenScanPictureBox.Image = frozenScanDisplayFrame;
                frozenScanPictureBox.Visible = true;
                frozenScanPictureBox.BringToFront();
                statusLabel.BringToFront();
                stagePositionLabel.BringToFront();
                if (old != null)
                    old.Dispose();
            }
            finally
            {
                if (bitmap != null)
                    bitmap.Dispose();
            }
        }

        /// <summary>
        /// 释放冻结预览位图，让原生相机预览重新显示。
        /// </summary>
        private void HideFrozenScanPreview()
        {
            if (frozenScanPictureBox == null)
                return;

            frozenScanPictureBox.Visible = false;
            frozenScanPictureBox.Image = null;
            if (frozenScanDisplayFrame != null)
            {
                frozenScanDisplayFrame.Dispose();
                frozenScanDisplayFrame = null;
            }
        }

        /// <summary>
        /// 释放本轮扫描使用的冻结图副本。
        /// </summary>
        private void ClearSelectionReferenceFrame()
        {
            lock (scanPreviewSync)
            {
                if (selectionReferenceFrame != null)
                {
                    selectionReferenceFrame.Dispose();
                    selectionReferenceFrame = null;
                }
            }
        }

        /// <summary>
        /// 在采集线程中生成框选后的明场展示图，并将位图所有权交给订阅者。
        /// </summary>
        private void FulfillSelectionPreviewRequest()
        {
            int requestId;
            RectangleF region;
            int xCount;
            int yCount;
            lock (selectionPreviewSync)
            {
                if (!selectionPreviewRequested)
                    return;
                requestId = selectionPreviewRequestId;
                region = selectionPreviewRegion;
                xCount = selectionPreviewXCount;
                yCount = selectionPreviewYCount;
                selectionPreviewRequested = false;
            }

            Bitmap capturedFrame = null;
            Bitmap preview = null;
            try
            {
                capturedFrame = CreateColorFrameBitmap(frame);
                preview = new Bitmap(capturedFrame);
                DrawSelectionPreviewOverlay(preview, region, xCount, yCount);
                lock (selectionPreviewSync)
                {
                    if (requestId != selectionPreviewRequestId)
                        return;
                    Bitmap old = savedSelectionFrame;
                    savedSelectionFrame = capturedFrame;
                    savedSelectionFrameRequestId = requestId;
                    capturedFrame = null;
                    if (old != null)
                        old.Dispose();
                    Monitor.PulseAll(selectionPreviewSync);
                }

                Action<Bitmap> handler = SelectionPreviewUpdated;
                if (handler != null)
                {
                    handler(preview);
                    preview = null;
                }
            }
            catch
            {
                // 展示图生成失败不应中断实时相机采集。
            }
            finally
            {
                if (capturedFrame != null)
                    capturedFrame.Dispose();
                if (preview != null)
                    preview.Dispose();
            }
        }

        /// <summary>
        /// 将 TUCam 底朝上的帧缓冲复制为托管 RGB 位图。
        /// </summary>
        private static Bitmap CreateColorFrameBitmap(TucamFrame source)
        {
            int width = source.Width;
            int height = source.Height;
            int channels = Math.Max(1, (int)source.Channels);
            int elementBytes = Math.Max(1, (int)source.ElementBytes);
            int bytesPerPixel = channels * elementBytes;
            int sourceStride = source.WidthStep > 0 ? checked((int)source.WidthStep) : width * bytesPerPixel;
            int imageSize = checked((int)source.ImageSize);
            if (source.Buffer == IntPtr.Zero || width <= 0 || height <= 0 || imageSize < sourceStride * height)
                throw new InvalidOperationException("TUCam 帧缓冲区信息无效，无法保存扫描参考图。");

            byte[] sourceBytes = new byte[imageSize];
            Marshal.Copy(IntPtr.Add(source.Buffer, source.HeaderSize), sourceBytes, 0, imageSize);
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            Rectangle bounds = new Rectangle(0, 0, width, height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            bool completed = false;
            try
            {
                int destinationStride = Math.Abs(data.Stride);
                byte[] destinationBytes = new byte[destinationStride * height];
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = (height - 1 - y) * sourceStride;
                    int destinationRow = y * destinationStride;
                    for (int x = 0; x < width; x++)
                    {
                        int sourcePixel = sourceRow + x * bytesPerPixel;
                        int destinationPixel = destinationRow + x * 3;
                        if (channels >= 3)
                        {
                            destinationBytes[destinationPixel] = ReadFrameChannel(sourceBytes, sourcePixel, 0, elementBytes);
                            destinationBytes[destinationPixel + 1] = ReadFrameChannel(sourceBytes, sourcePixel, 1, elementBytes);
                            destinationBytes[destinationPixel + 2] = ReadFrameChannel(sourceBytes, sourcePixel, 2, elementBytes);
                        }
                        else
                        {
                            byte gray = ReadFrameChannel(sourceBytes, sourcePixel, 0, elementBytes);
                            destinationBytes[destinationPixel] = gray;
                            destinationBytes[destinationPixel + 1] = gray;
                            destinationBytes[destinationPixel + 2] = gray;
                        }
                    }
                }
                Marshal.Copy(destinationBytes, 0, data.Scan0, destinationBytes.Length);
                completed = true;
            }
            finally
            {
                bitmap.UnlockBits(data);
                if (!completed)
                    bitmap.Dispose();
            }
            return bitmap;
        }

        /// <summary>
        /// 读取 8 位通道；高位深帧使用最高有效字节用于预览。
        /// </summary>
        private static byte ReadFrameChannel(byte[] bytes, int pixelOffset, int channel, int elementBytes)
        {
            return bytes[pixelOffset + channel * elementBytes + elementBytes - 1];
        }

        /// <summary>
        /// 在采集线程中满足等待中的单次灰度快照请求。
        /// </summary>
        private void FulfillSnapshotRequest()
        {
            int requestId;
            lock (snapshotStateSync)
            {
                if (!snapshotRequested)
                    return;
                if (snapshotFramesToSkip > 0)
                {
                    snapshotFramesToSkip--;
                    return;
                }
                requestId = snapshotRequestId;
                snapshotRequested = false;
            }

            GrayFrameSnapshot result = null;
            Exception error = null;
            try
            {
                result = CreateGrayFrameSnapshot(frame, snapshotMaximumDimension);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            lock (snapshotStateSync)
            {
                if (requestId != snapshotRequestId)
                    return;
                snapshotResult = result;
                snapshotException = error;
            }
            snapshotReady.Set();
        }

        /// <summary>
        /// 复制 SDK 帧缓冲并生成用于配准的降采样灰度图。
        /// </summary>
        private GrayFrameSnapshot CreateGrayFrameSnapshot(TucamFrame source, int maximumDimension)
        {
            int width = source.Width;
            int height = source.Height;
            int channels = Math.Max(1, (int)source.Channels);
            int elementBytes = Math.Max(1, (int)source.ElementBytes);
            int bytesPerPixel = channels * elementBytes;
            int stride = source.WidthStep > 0 ? checked((int)source.WidthStep) : width * bytesPerPixel;
            int imageSize = checked((int)source.ImageSize);
            if (source.Buffer == IntPtr.Zero || width <= 0 || height <= 0 || imageSize < stride * height)
                throw new InvalidOperationException("TUCam 帧缓冲区信息无效。");

            if (snapshotRawBuffer == null || snapshotRawBuffer.Length < imageSize)
                snapshotRawBuffer = new byte[imageSize];
            byte[] raw = snapshotRawBuffer;
            Marshal.Copy(IntPtr.Add(source.Buffer, source.HeaderSize), raw, 0, imageSize);
            int samplingStep = Math.Max(1, (Math.Max(width, height) + maximumDimension - 1) / maximumDimension);
            int outputWidth = (width + samplingStep - 1) / samplingStep;
            int outputHeight = (height + samplingStep - 1) / samplingStep;
            byte[] gray = new byte[outputWidth * outputHeight];

            for (int outputY = 0; outputY < outputHeight; outputY++)
            {
                int firstY = outputY * samplingStep;
                int lastY = Math.Min(height, firstY + samplingStep);
                for (int outputX = 0; outputX < outputWidth; outputX++)
                {
                    int firstX = outputX * samplingStep;
                    int lastX = Math.Min(width, firstX + samplingStep);
                    int sampleWidth = Math.Min(2, lastX - firstX);
                    int sampleHeight = Math.Min(2, lastY - firstY);
                    int sampleX = firstX + (lastX - firstX - sampleWidth) / 2;
                    int sampleY = firstY + (lastY - firstY - sampleHeight) / 2;
                    long sum = 0;
                    int count = 0;
                    for (int y = sampleY; y < sampleY + sampleHeight; y++)
                    {
                        // TUFRM_FMT_USUAl stores image rows bottom-up on Windows,
                        // while the preview and mouse coordinates use a top-left origin.
                        int row = (height - 1 - y) * stride;
                        for (int x = sampleX; x < sampleX + sampleWidth; x++)
                        {
                            int pixel = row + x * bytesPerPixel;
                            int channelSum = 0;
                            for (int channel = 0; channel < channels; channel++)
                            {
                                int component = pixel + channel * elementBytes;
                                channelSum += elementBytes == 1 ? raw[component] : raw[component + elementBytes - 1];
                            }
                            sum += channelSum / channels;
                            count++;
                        }
                    }
                    gray[outputY * outputWidth + outputX] = (byte)(sum / Math.Max(1, count));
                }
            }

            return new GrayFrameSnapshot(outputWidth, outputHeight, width, height, samplingStep, gray);
        }

        /// <summary>
        /// 取消当前快照请求，并使迟到结果失效。
        /// </summary>
        private void CancelSnapshotRequest()
        {
            lock (snapshotStateSync)
            {
                snapshotRequestId++;
                snapshotRequested = false;
            }
        }

        /// <summary>
        /// 在 UI 线程隐藏相机状态提示。
        /// </summary>
        private void HideStatusLabel()
        {
            RunOnUiThread(() => statusLabel.Visible = false);
        }

        /// <summary>
        /// 在 UI 线程显示指定颜色的相机状态信息。
        /// </summary>
        private void ShowCameraStatus(string message, Color color)
        {
            RunOnUiThread(() =>
            {
                statusLabel.Text = message;
                statusLabel.ForeColor = color;
                statusLabel.Visible = true;
                statusLabel.BringToFront();
            });
        }

        /// <summary>
        /// 以橙红色显示相机错误。
        /// </summary>
        private void ShowCameraError(string message)
        {
            ShowCameraStatus(message, Color.OrangeRed);
        }

        /// <summary>
        /// 安全地将界面更新投递到 UI 线程。
        /// </summary>
        private void RunOnUiThread(Action action)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                if (InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch (InvalidOperationException)
            {
                // 窗口正在关闭时忽略迟到的状态更新。
            }
        }

        /// <summary>
        /// 预览控件缩放时更新绘制尺寸和标注坐标。
        /// </summary>
        private void PreviewPanel_Resize(object sender, EventArgs e)
        {
            previewWidth = previewPanel.ClientSize.Width;
            previewHeight = previewPanel.ClientSize.Height;
            Interlocked.Exchange(ref previewSurfaceClearPending, 1);
            UpdateSelectionOverlayFromImageCoordinates();
            RefreshFrozenScanPreview();
        }

        /// <summary>
        /// 窗口关闭时释放计时器、相机和绘图状态。
        /// </summary>
        private void CameraShowForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            CancelRectangleDrawing();
            Image selectionToolIcon = rectangleToolButton.Image;
            rectangleToolButton.Image = null;
            if (selectionToolIcon != null)
                selectionToolIcon.Dispose();
            RectangleSelectionOverlay overlay = selectionOverlay;
            selectionOverlay = null;
            if (overlay != null)
                overlay.Dispose();
            if (performanceTimer != null)
            {
                performanceTimer.Stop();
                performanceTimer.Tick -= PerformanceTimer_Tick;
                performanceTimer.Dispose();
                performanceTimer = null;
            }
            StopCamera();
            HideFrozenScanPreview();
            ClearSelectionReferenceFrame();
            lock (selectionPreviewSync)
            {
                selectionPreviewRequestId++;
                selectionPreviewRequested = false;
                Monitor.PulseAll(selectionPreviewSync);
                if (savedSelectionFrame != null)
                {
                    savedSelectionFrame.Dispose();
                    savedSelectionFrame = null;
                }
            }
            SelectionPreviewUpdated = null;
        }

        /// <summary>
        /// 按 SDK 要求依次停止采集并释放绘制器、缓冲区、设备和 API。
        /// </summary>
        private void StopCamera()
        {
            capturing = false;
            if (camera.CameraHandle != IntPtr.Zero && captureThread != null)
                TUCamNative.TUCAM_Buf_AbortWait(camera.CameraHandle);

            if (captureThread != null && captureThread != Thread.CurrentThread)
            {
                captureThread.Join();
                captureThread = null;
            }

            if (drawInitialized)
            {
                TUCamNative.TUCAM_Draw_Uninit(camera.CameraHandle);
                drawInitialized = false;
            }
            if (captureStarted)
            {
                TUCamNative.TUCAM_Cap_Stop(camera.CameraHandle);
                captureStarted = false;
            }
            if (bufferAllocated)
            {
                TUCamNative.TUCAM_Buf_Release(camera.CameraHandle);
                bufferAllocated = false;
            }
            if (camera.CameraHandle != IntPtr.Zero)
            {
                TUCamNative.TUCAM_Dev_Close(camera.CameraHandle);
                camera.CameraHandle = IntPtr.Zero;
            }
            if (apiInitialized)
            {
                TUCamNative.TUCAM_Api_Uninit();
                apiInitialized = false;
            }

            imageWidth = 0;
            imageHeight = 0;
            Interlocked.Exchange(ref framesSinceUpdate, 0);
        }
    }
}

