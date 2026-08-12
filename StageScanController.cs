using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace MicroRaman
{
    /// <summary>
    /// 表示平台三轴坐标；当前扫描只改变 X、Y，Z 始终保持原值。
    /// </summary>
    internal struct StagePosition
    {
        internal double X;
        internal double Y;
        internal double Z;
    }

    /// <summary>
    /// 明场定位校验的像素误差汇总。
    /// </summary>
    internal struct AlignmentVerificationResult
    {
        internal int PointCount;
        internal double AverageErrorPixels;
        internal double MaximumErrorPixels;
        internal double InitialAverageErrorPixels;
        internal double InitialMaximumErrorPixels;
        internal bool CalibrationRefined;
    }

    /// <summary>
    /// 保存平台 X/Y 位移到相机图像 X/Y 位移的二维线性标定矩阵。
    /// </summary>
    internal sealed class StagePixelCalibration
    {
        internal double PixelXPerStageX;
        internal double PixelYPerStageX;
        internal double PixelXPerStageY;
        internal double PixelYPerStageY;

        /// <summary>
        /// 将原点图像中的目标点换算成使其移动到视野中心的平台绝对坐标。
        /// </summary>
        internal StagePosition ImagePointToStage(
            PointF imagePoint,
            int imageWidth,
            int imageHeight,
            StagePosition origin)
        {
            double imageDeltaX = imagePoint.X - imageWidth / 2.0;
            double imageDeltaY = imagePoint.Y - imageHeight / 2.0;
            double determinant = GetDeterminant();
            if (Math.Abs(determinant) < 1e-9)
                throw new InvalidOperationException("X、Y 标定矩阵不可逆，无法计算平台坐标。");

            // 样品固定点在图像中的移动方向与平台视野中心移动方向相反。
            double stageDeltaX = -(PixelYPerStageY * imageDeltaX - PixelXPerStageY * imageDeltaY) / determinant;
            double stageDeltaY = -(-PixelYPerStageX * imageDeltaX + PixelXPerStageX * imageDeltaY) / determinant;
            return new StagePosition
            {
                X = origin.X + stageDeltaX,
                Y = origin.Y + stageDeltaY,
                Z = origin.Z
            };
        }

        /// <summary>
        /// 计算二维标定矩阵的行列式，用于判断 X、Y 标定方向是否可区分。
        /// </summary>
        internal double GetDeterminant()
        {
            return PixelXPerStageX * PixelYPerStageY - PixelXPerStageY * PixelYPerStageX;
        }
    }

    /// <summary>
    /// 负责明场图像标定、标定数据保存以及基于平台绝对坐标的蛇形扫描。
    /// </summary>
    internal sealed class StageScanController
    {
        private readonly Command command = new Command();
        private StagePixelCalibration savedCalibration;
        private int[] savedDimensions;
        private int savedImageWidth;
        private int savedImageHeight;
        private double? savedCalibrationZ;
        // 定标移动后的稳定等待；蛇形扫描使用下面独立的点内时序。
        private const int CalibrationSettlingDelayMilliseconds = 750;
        private const int ScanPointSettlingDelayMilliseconds = 350;
        private const int LaserTecWarmupDelayMilliseconds = 5000;
        private const int LaserOffBeforeMoveDelayMilliseconds = 150;
        // 暗谱和亮谱均由读谱入口丢弃首帧，并保证每帧至少持续 200 ms；
        // 因此不再在扫描控制器中叠加固定的开关前后等待。
        private const double MaximumAllowedCenteringErrorPixels = 15.0;
        // 常规定标先使用三个独立点快速校验；误差较大时再自动执行完整九点微调。
        // 这样不会以牺牲坐标精度为代价缩短定标时间。
        private const double FastVerificationTargetErrorPixels = 10.0;

        /// <summary>
        /// 获取当前控制器是否保存了可用于扫描的完整标定数据。
        /// </summary>
        internal bool HasCalibration
        {
            get
            {
                return savedCalibration != null
                    && savedDimensions != null
                    && savedImageWidth > 0
                    && savedImageHeight > 0
                    && savedCalibrationZ.HasValue;
            }
        }

        /// <summary>
        /// 清除像素与平台坐标标定；重新连接控制器或更换相机后必须调用。
        /// </summary>
        internal void ResetOrigin()
        {
            savedCalibration = null;
            savedDimensions = null;
            savedImageWidth = 0;
            savedImageHeight = 0;
            savedCalibrationZ = null;
        }

        /// <summary>
        /// 在明场图像下重复移动 X、Y 轴，计算并保存像素与平台坐标的换算矩阵。
        /// </summary>
        internal AlignmentVerificationResult Calibrate(
            CameraShowForm camera,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (!SerialPortManager.IsOpen)
                throw new InvalidOperationException("请先连接 TANGO 控制器。");
            if (camera == null || camera.IsDisposed)
                throw new InvalidOperationException("请先打开相机窗口。");
            if (camera.CameraImageWidth <= 0 || camera.CameraImageHeight <= 0)
                throw new InvalidOperationException("相机尚未取得有效图像，无法执行标定。");

            ResetOrigin();
            int[] dimensions = command.ReadDimensions();
            StagePosition origin = command.ReadPosition();
            camera.PrepareForNewScan();

            double xDistance = GetCalibrationDistance(dimensions[0]);
            double yDistance = GetCalibrationDistance(dimensions[1]);
            // X+/X−、Y+/Y−已经能完整拟合二维仿射换算，并可抵消两个方向的反向间隙。
            // 对角线两次移动只提供重复样本，改由后续独立三点复测承担校验。
            CalibrationMove[] moves = new[]
            {
                new CalibrationMove(xDistance, 0, "X+"),
                new CalibrationMove(-xDistance, 0, "X−"),
                new CalibrationMove(0, yDistance, "Y+"),
                new CalibrationMove(0, -yDistance, "Y−")
            };
            List<CenteringMeasurement> measurements = new List<CenteringMeasurement>(moves.Length);
            for (int index = 0; index < moves.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CalibrationMove move = moves[index];
                progress.Report(string.Format("定标 {0} {1}/{2}", move.Name, index + 1, moves.Length));
                measurements.Add(MeasureCalibrationMove(
                    camera, origin, dimensions, move.DeltaX, move.DeltaY, cancellationToken));
            }

            StagePixelCalibration calibration = FitCalibration(measurements);
            if (Math.Abs(calibration.GetDeterminant()) < 1e-6)
                throw new InvalidOperationException("图像标定失败：X、Y 两次移动得到的图像方向无法区分。");

            VerifyPosition(origin, command.ReadPosition(), dimensions);
            savedCalibration = calibration;
            savedDimensions = (int[])dimensions.Clone();
            savedImageWidth = camera.CameraImageWidth;
            savedImageHeight = camera.CameraImageHeight;
            savedCalibrationZ = origin.Z;
            try
            {
            progress.Report("快速复测定位精度");
            AlignmentVerificationResult verification = VerifyCenteringQuick(camera, progress, cancellationToken);
            if (verification.MaximumErrorPixels > FastVerificationTargetErrorPixels)
            {
                progress.Report(string.Format(
                    "快速复测偏差 {0:F2} 像素，执行完整微调复测…",
                    verification.MaximumErrorPixels));
                verification = VerifyCentering(camera, progress, cancellationToken);
            }
            if (verification.MaximumErrorPixels > MaximumAllowedCenteringErrorPixels)
            {
                throw new InvalidOperationException(string.Format(
                    "自动微调后最大定位偏差仍为 {0:F2} 像素，超过允许的 {1:F0} 像素。请确认明场样品纹理清晰、平台无松动后重新定标。",
                    verification.MaximumErrorPixels,
                    MaximumAllowedCenteringErrorPixels));
            }
            progress.Report(string.Format("标定完成（最大偏差 {0:F2} 像素）", verification.MaximumErrorPixels));
            return verification;
            }
            catch
            {
                // 验证失败时绝不能留下可供扫描使用的临时标定结果。
                ResetOrigin();
                throw;
            }
        }

        /// <summary>
        /// 常规定标完成后的快速独立校验。 三个非共线点能够同时覆盖 X、Y 和斜向位移；只有其误差偏大时才进入耗时的九点微调。
        /// </summary>
        private AlignmentVerificationResult VerifyCenteringQuick(
            CameraShowForm camera,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            StagePosition verificationOrigin = command.ReadPosition();
            List<PointF> testPoints = CreateQuickVerificationPoints(savedImageWidth, savedImageHeight);
            StagePixelCalibration originalCalibration = savedCalibration;

            try
            {
                List<CenteringMeasurement> initialMeasurements;
                AlignmentErrorSummary initial = MeasureCentering(
                    camera,
                    originalCalibration,
                    verificationOrigin,
                    testPoints,
                    "快速复测",
                    progress,
                    cancellationToken,
                    out initialMeasurements);

                if (initial.MaximumErrorPixels <= FastVerificationTargetErrorPixels)
                    return CreateVerificationResult(testPoints.Count, initial, initial, false);

                // 快速复测不合格时，先仅使用这三个非共线实测点做一次矩阵微调，
                // 再以另一组三点独立验证。常见的轻微反向间隙无需进入耗时九点流程。
                StagePixelCalibration refinedCalibration = FitCalibration(initialMeasurements);
                ReturnToScanOrigin(camera, verificationOrigin, savedDimensions);

                List<PointF> confirmationPoints = CreateQuickConfirmationPoints(savedImageWidth, savedImageHeight);
                List<CenteringMeasurement> confirmationMeasurements;
                AlignmentErrorSummary refined = MeasureCentering(
                    camera,
                    refinedCalibration,
                    verificationOrigin,
                    confirmationPoints,
                    "快速微调复测",
                    progress,
                    cancellationToken,
                    out confirmationMeasurements);

                bool improved = refined.MaximumErrorPixels < initial.MaximumErrorPixels;
                if (improved)
                    savedCalibration = refinedCalibration;

                return CreateVerificationResult(
                    testPoints.Count + confirmationPoints.Count,
                    initial,
                    improved ? refined : initial,
                    improved);
            }
            finally
            {
                ReturnToScanOrigin(camera, verificationOrigin, savedDimensions);
            }
        }

        /// <summary>
        /// 创建VerificationResult相关的内部处理。
        /// </summary>
        private static AlignmentVerificationResult CreateVerificationResult(
            int pointCount,
            AlignmentErrorSummary initial,
            AlignmentErrorSummary final,
            bool refined)
        {
            return new AlignmentVerificationResult
            {
                PointCount = pointCount,
                InitialAverageErrorPixels = initial.AverageErrorPixels,
                InitialMaximumErrorPixels = initial.MaximumErrorPixels,
                AverageErrorPixels = final.AverageErrorPixels,
                MaximumErrorPixels = final.MaximumErrorPixels,
                CalibrationRefined = refined
            };
        }

        /// <summary>
        /// 使用已保存的标定矩阵按蛇形路径移动；全程只依赖绝对坐标和 ?pos，不读取图像纹理。
        /// </summary>
        internal void Scan(
            CameraShowForm camera,
            IList<PointF> normalizedPoints,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            Action<bool> setLaserOutput,
            Action<bool> setTecOutput,
            Action<CancellationToken> warmUpSpectrum,
            Action captureDarkSpectrum,
            int laserPreAcquisitionDelayMilliseconds,
            Func<int, bool> acquireSpectrum)
        {
            if (!SerialPortManager.IsOpen)
                throw new InvalidOperationException("请先连接 TANGO 控制器。");
            if (!HasCalibration)
                throw new InvalidOperationException("尚未完成平台定标。请在关闭激光、打开照明后先点击“平台定标”。");
            if (normalizedPoints == null || normalizedPoints.Count == 0)
                throw new InvalidOperationException("扫描路径为空。");
            if (setLaserOutput == null)
                throw new ArgumentNullException(nameof(setLaserOutput));
            if (setTecOutput == null)
                throw new ArgumentNullException(nameof(setTecOutput));
            if (warmUpSpectrum == null)
                throw new ArgumentNullException(nameof(warmUpSpectrum));
            if (captureDarkSpectrum == null)
                throw new ArgumentNullException(nameof(captureDarkSpectrum));
            if (laserPreAcquisitionDelayMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(laserPreAcquisitionDelayMilliseconds));
            if (acquireSpectrum == null)
                throw new ArgumentNullException(nameof(acquireSpectrum));
            if (camera.CameraImageWidth != savedImageWidth || camera.CameraImageHeight != savedImageHeight)
                throw new InvalidOperationException("相机分辨率已在标定后改变，请重新执行平台定标。");

            int[] currentDimensions = command.ReadDimensions();
            if (currentDimensions[0] != savedDimensions[0] || currentDimensions[1] != savedDimensions[1])
                throw new InvalidOperationException("平台坐标单位已在标定后改变，请重新执行平台定标。");

            StagePosition scanOrigin = command.ReadPosition();
            if (HasCalibrationZChanged(scanOrigin.Z))
            {
                ResetOrigin();
                throw new InvalidOperationException("检测到 Z 轴已在平台定标后移动。已清空标定缓存，请重新执行平台定标。");
            }

            StagePixelCalibration calibration = savedCalibration;
            camera.BeginFrozenScanPreview(cancellationToken);

            try
            {
                // 冻结点击扫描时的当前画面后，移动平台前统一确保 LD 关闭。
                setLaserOutput(false);
                // TEC 在整段扫描期间保持开启，必须在第一次平台移动前启动。
                setTecOutput(true);
                progress.Report("激光器 TEC 稳定中…");
                WaitForScanDelay(LaserTecWarmupDelayMilliseconds, cancellationToken);
                progress.Report("光谱仪温控稳定中…");
                warmUpSpectrum(cancellationToken);
                camera.PrepareForNewScan();

                for (int index = 0; index < normalizedPoints.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Stopwatch pointTimer = Stopwatch.StartNew();
                    PointF normalized = normalizedPoints[index];
                    PointF imagePoint = new PointF(
                        normalized.X * savedImageWidth,
                        normalized.Y * savedImageHeight);
                    StagePosition target = calibration.ImagePointToStage(
                        imagePoint,
                        savedImageWidth,
                        savedImageHeight,
                        scanOrigin);

                    progress.Report(string.Format("扫描 {0}/{1}", index + 1, normalizedPoints.Count));
                    MoveToAndVerify(target, savedDimensions);
                    VerifySettledScanPoint(target, command.ReadPosition(), savedDimensions);
                    WaitForScanDelay(ScanPointSettlingDelayMilliseconds, cancellationToken);

                    // 每一行只使用一张暗谱：第一点以及蛇形路径换行后的第一个点更新，
                    // 行内不再按时间切换背景基准，避免同一行出现人为的强度跳变。
                    bool refreshDarkSpectrum = true;
                    long darkAcquisitionMilliseconds = 0;
                    if (refreshDarkSpectrum)
                    {
                        // 确认 LD 已关闭且队列中无旧亮帧后，才开始采集暗谱。
                        setLaserOutput(false);
                        progress.Report(string.Format("更新暗谱 {0}/{1}", index + 1, normalizedPoints.Count));
                        Stopwatch darkTimer = Stopwatch.StartNew();
                        captureDarkSpectrum();
                        darkTimer.Stop();
                        darkAcquisitionMilliseconds = darkTimer.ElapsedMilliseconds;
                    }

                    setLaserOutput(true);
                    Stopwatch brightTimer = Stopwatch.StartNew();
                    try
                    {
                        if (laserPreAcquisitionDelayMilliseconds > 0)
                        {
                            progress.Report(string.Format(
                                "激光稳定中 {0}/{1}", index + 1, normalizedPoints.Count));
                            WaitForScanDelay(laserPreAcquisitionDelayMilliseconds, cancellationToken);
                        }
                        progress.Report(string.Format("开激光采谱 {0}/{1}", index + 1, normalizedPoints.Count));
                        acquireSpectrum(index);
                    }
                    finally
                    {
                        brightTimer.Stop();
                        setLaserOutput(false);
                    }

                    // LD 完全关闭后再允许平台开始下一次移动。
                    WaitForScanDelay(LaserOffBeforeMoveDelayMilliseconds, cancellationToken);

                    camera.RecordScanVisit(normalized);
                    pointTimer.Stop();
                    progress.Report(string.Format(
                        "完成 {0}/{1}（暗谱 {2}，亮谱及稳定 {3} ms，本点 {4} ms）",
                        index + 1,
                        normalizedPoints.Count,
                        refreshDarkSpectrum
                            ? darkAcquisitionMilliseconds + " ms"
                            : "复用",
                        brightTimer.ElapsedMilliseconds,
                        pointTimer.ElapsedMilliseconds));
                }
            }
            finally
            {
                try
                {
                    // 无论正常结束、停止还是异常，移动平台前都再次强制关闭 LD。
                    setLaserOutput(false);
                    Thread.Sleep(LaserOffBeforeMoveDelayMilliseconds);
                }
                finally
                {
                    try
                    {
                        // 安全顺序：先关闭 LD，再关闭 TEC。
                        setTecOutput(false);
                    }
                    finally
                    {
                        try
                        {
                            ReturnToScanOrigin(camera, scanOrigin, savedDimensions);
                        }
                        finally
                        {
                            camera.EndFrozenScanPreview();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 在明场下校验并微调标定矩阵，使中心附近目标点能更准确地移动到图像中心。 只有复测误差变小时才保存微调后的矩阵，不执行激光扫描。
        /// </summary>
        internal AlignmentVerificationResult VerifyCentering(
            CameraShowForm camera,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (!SerialPortManager.IsOpen)
                throw new InvalidOperationException("请先连接 TANGO 控制器。");
            if (!HasCalibration)
                throw new InvalidOperationException("请先完成平台定标。");
            if (camera == null || camera.IsDisposed)
                throw new InvalidOperationException("请先打开相机窗口。");
            if (camera.CameraImageWidth != savedImageWidth || camera.CameraImageHeight != savedImageHeight)
                throw new InvalidOperationException("相机分辨率已在标定后改变，请重新执行平台定标。");

            int[] currentDimensions = command.ReadDimensions();
            if (currentDimensions[0] != savedDimensions[0] || currentDimensions[1] != savedDimensions[1])
                throw new InvalidOperationException("平台坐标单位已在标定后改变，请重新执行平台定标。");

            StagePosition verificationOrigin = command.ReadPosition();
            List<PointF> testPoints = CreateCenterVerificationPoints(savedImageWidth, savedImageHeight);
            StagePixelCalibration originalCalibration = savedCalibration;

            try
            {
                List<CenteringMeasurement> firstMeasurements;
                AlignmentErrorSummary initial = MeasureCentering(
                    camera,
                    originalCalibration,
                    verificationOrigin,
                    testPoints,
                    "初测",
                    progress,
                    cancellationToken,
                    out firstMeasurements);

                StagePixelCalibration refinedCalibration = FitCalibration(firstMeasurements);
                ReturnToScanOrigin(camera, verificationOrigin, savedDimensions);

                List<CenteringMeasurement> secondMeasurements;
                AlignmentErrorSummary refined = MeasureCentering(
                    camera,
                    refinedCalibration,
                    verificationOrigin,
                    testPoints,
                    "复测",
                    progress,
                    cancellationToken,
                    out secondMeasurements);

                bool improved = refined.MaximumErrorPixels < initial.MaximumErrorPixels;
                if (improved)
                    savedCalibration = refinedCalibration;

                return new AlignmentVerificationResult
                {
                    PointCount = testPoints.Count,
                    InitialAverageErrorPixels = initial.AverageErrorPixels,
                    InitialMaximumErrorPixels = initial.MaximumErrorPixels,
                    AverageErrorPixels = improved ? refined.AverageErrorPixels : initial.AverageErrorPixels,
                    MaximumErrorPixels = improved ? refined.MaximumErrorPixels : initial.MaximumErrorPixels,
                    CalibrationRefined = improved
                };
            }
            finally
            {
                ReturnToScanOrigin(camera, verificationOrigin, savedDimensions);
            }
        }

        /// <summary>
        /// 以指定矩阵完成一轮定位实测，并保留用于微调的实际位移数据。
        /// </summary>
        private AlignmentErrorSummary MeasureCentering(
            CameraShowForm camera,
            StagePixelCalibration calibration,
            StagePosition origin,
            IList<PointF> testPoints,
            string phase,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            out List<CenteringMeasurement> measurements)
        {
            GrayFrameSnapshot reference = camera.CaptureGrayFrame(2, 15000, cancellationToken);
            measurements = new List<CenteringMeasurement>(testPoints.Count);
            double totalError = 0;
            double maximumError = 0;

            for (int index = 0; index < testPoints.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PointF imagePoint = testPoints[index];
                StagePosition target = calibration.ImagePointToStage(
                    imagePoint,
                    savedImageWidth,
                    savedImageHeight,
                    origin);
                progress.Report(string.Format("{0} {1}/{2}", phase, index + 1, testPoints.Count));
                MoveToAndVerify(target, savedDimensions);
                WaitForCalibrationSettling(cancellationToken);
                VerifySettledScanPoint(target, command.ReadPosition(), savedDimensions);

                GrayFrameSnapshot shifted = camera.CaptureGrayFrame(2, 15000, cancellationToken);
                ImageTranslation translation = ImageRegistration.MeasureTranslation(reference, shifted);
                if (translation.Confidence < 8)
                    throw new InvalidOperationException("定位校验图像纹理不足，无法可靠计算像素偏差。请保持明场照明并选择有纹理的样品区域。");

                double expectedX = savedImageWidth / 2.0 - imagePoint.X;
                double expectedY = savedImageHeight / 2.0 - imagePoint.Y;
                double errorX = translation.X - expectedX;
                double errorY = translation.Y - expectedY;
                double error = Math.Sqrt(errorX * errorX + errorY * errorY);
                totalError += error;
                maximumError = Math.Max(maximumError, error);
                measurements.Add(new CenteringMeasurement
                {
                    StageDeltaX = target.X - origin.X,
                    StageDeltaY = target.Y - origin.Y,
                    ImageShiftX = translation.X,
                    ImageShiftY = translation.Y
                });
            }

            return new AlignmentErrorSummary
            {
                AverageErrorPixels = totalError / testPoints.Count,
                MaximumErrorPixels = maximumError
            };
        }

        /// <summary>
        /// 由实测点最小二乘拟合完整的二维平台到像素变换矩阵。
        /// </summary>
        private static StagePixelCalibration FitCalibration(IList<CenteringMeasurement> measurements)
        {
            double sumXX = 0;
            double sumXY = 0;
            double sumYY = 0;
            double sumStageXImageX = 0;
            double sumStageYImageX = 0;
            double sumStageXImageY = 0;
            double sumStageYImageY = 0;
            foreach (CenteringMeasurement measurement in measurements)
            {
                sumXX += measurement.StageDeltaX * measurement.StageDeltaX;
                sumXY += measurement.StageDeltaX * measurement.StageDeltaY;
                sumYY += measurement.StageDeltaY * measurement.StageDeltaY;
                sumStageXImageX += measurement.StageDeltaX * measurement.ImageShiftX;
                sumStageYImageX += measurement.StageDeltaY * measurement.ImageShiftX;
                sumStageXImageY += measurement.StageDeltaX * measurement.ImageShiftY;
                sumStageYImageY += measurement.StageDeltaY * measurement.ImageShiftY;
            }

            double determinant = sumXX * sumYY - sumXY * sumXY;
            if (Math.Abs(determinant) < 1e-12)
                throw new InvalidOperationException("定位校验点的运动范围不足，无法微调二维标定矩阵。");

            StagePixelCalibration refined = new StagePixelCalibration
            {
                PixelXPerStageX = (sumStageXImageX * sumYY - sumStageYImageX * sumXY) / determinant,
                PixelXPerStageY = (sumStageYImageX * sumXX - sumStageXImageX * sumXY) / determinant,
                PixelYPerStageX = (sumStageXImageY * sumYY - sumStageYImageY * sumXY) / determinant,
                PixelYPerStageY = (sumStageYImageY * sumXX - sumStageXImageY * sumXY) / determinant
            };
            if (Math.Abs(refined.GetDeterminant()) < 1e-6)
                throw new InvalidOperationException("定位校验得到的微调矩阵不可逆，未保存该结果。");
            return refined;
        }

        private struct CenteringMeasurement
        {
            internal double StageDeltaX;
            internal double StageDeltaY;
            internal double ImageShiftX;
            internal double ImageShiftY;
        }

        private struct AlignmentErrorSummary
        {
            internal double AverageErrorPixels;
            internal double MaximumErrorPixels;
        }

        /// <summary>
        /// 建立中心周围 15% 视野范围内的 3×3 校验点，保证图像有足够重叠用于配准。
        /// </summary>
        private static List<PointF> CreateCenterVerificationPoints(int imageWidth, int imageHeight)
        {
            float offsetX = imageWidth * 0.15f;
            float offsetY = imageHeight * 0.15f;
            List<PointF> points = new List<PointF>(9);
            for (int yIndex = -1; yIndex <= 1; yIndex++)
            {
                for (int xIndex = -1; xIndex <= 1; xIndex++)
                {
                    points.Add(new PointF(
                        imageWidth / 2f + xIndex * offsetX,
                        imageHeight / 2f + yIndex * offsetY));
                }
            }
            return points;
        }

        /// <summary>
        /// 建立快速校验用的三个非共线点。它们与原点保持足够重叠，且不与初始六方向标定重复。
        /// </summary>
        private static List<PointF> CreateQuickVerificationPoints(int imageWidth, int imageHeight)
        {
            float offsetX = imageWidth * 0.15f;
            float offsetY = imageHeight * 0.15f;
            return new List<PointF>(3)
            {
                new PointF(imageWidth / 2f - offsetX, imageHeight / 2f - offsetY),
                new PointF(imageWidth / 2f + offsetX, imageHeight / 2f - offsetY),
                new PointF(imageWidth / 2f - offsetX, imageHeight / 2f + offsetY)
            };
        }

        /// <summary>
        /// 快速微调后的独立确认点，避免用参与拟合的同一批点自证精度。
        /// </summary>
        private static List<PointF> CreateQuickConfirmationPoints(int imageWidth, int imageHeight)
        {
            float offsetX = imageWidth * 0.15f;
            float offsetY = imageHeight * 0.15f;
            return new List<PointF>(3)
            {
                new PointF(imageWidth / 2f + offsetX, imageHeight / 2f + offsetY),
                new PointF(imageWidth / 2f - offsetX, imageHeight / 2f),
                new PointF(imageWidth / 2f, imageHeight / 2f + offsetY)
            };
        }

        /// <summary>
        /// 定标或精度复测移动后的可取消稳定延时。
        /// </summary>
        private static void WaitForCalibrationSettling(CancellationToken cancellationToken)
        {
            if (cancellationToken.WaitHandle.WaitOne(CalibrationSettlingDelayMilliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 蛇形扫描点内的可取消短延时。
        /// </summary>
        private static void WaitForScanDelay(int milliseconds, CancellationToken cancellationToken)
        {
            if (cancellationToken.WaitHandle.WaitOne(milliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// 移动到绝对目标坐标并用 ?pos 校验；若首次未到容差范围则再执行一次绝对定位。
        /// </summary>
        private void MoveToAndVerify(StagePosition target, int[] dimensions)
        {
            command.MoveAbsoluteXY(target.X, target.Y);
            StagePosition actual = command.ReadPosition();
            if (IsAtTarget(target, actual, dimensions))
                return;

            command.MoveAbsoluteXY(target.X, target.Y);
            actual = command.ReadPosition();
            if (!IsAtTarget(target, actual, dimensions))
            {
                throw new InvalidOperationException(string.Format(
                    "平台未到达目标坐标：目标 ({0:F4}, {1:F4})，实际 ({2:F4}, {3:F4})。",
                    target.X,
                    target.Y,
                    actual.X,
                    actual.Y));
            }

        }

        /// <summary>
        /// 判断平台实测 X、Y 坐标是否处于目标坐标容差范围内。
        /// </summary>
        private static bool IsAtTarget(StagePosition target, StagePosition actual, int[] dimensions)
        {
            return Math.Abs(actual.X - target.X) <= GetPositionTolerance(dimensions[0])
                && Math.Abs(actual.Y - target.Y) <= GetPositionTolerance(dimensions[1]);
        }

        /// <summary>
        /// 判断扫描前的 Z 是否仍与定标时完全一致。
        /// </summary>
        private bool HasCalibrationZChanged(double currentZ)
        {
            return !savedCalibrationZ.HasValue || Math.Abs(currentZ - savedCalibrationZ.Value) > 1e-6;
        }

        /// <summary>
        /// 稳定等待结束后再次确认平台仍停在当前扫描点。
        /// </summary>
        private static void VerifySettledScanPoint(
            StagePosition target,
            StagePosition actual,
            int[] dimensions)
        {
            if (IsAtTarget(target, actual, dimensions))
                return;

            throw new InvalidOperationException(string.Format(
                "平台稳定后偏离扫描点：目标 ({0:F4}, {1:F4})，实际 ({2:F4}, {3:F4})。",
                target.X,
                target.Y,
                actual.X,
                actual.Y));
        }

        /// <summary>
        /// 执行一次平台校准位移，等待图像稳定后采集配准帧，并始终返回原点。
        /// </summary>
        private CenteringMeasurement MeasureCalibrationMove(
            CameraShowForm camera,
            StagePosition origin,
            int[] dimensions,
            double deltaX,
            double deltaY,
            CancellationToken cancellationToken)
        {
            GrayFrameSnapshot reference = camera.CaptureGrayFrame(2, 15000, cancellationToken);
            bool moved = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                moved = true;
                command.MoveAbsoluteXY(origin.X + deltaX, origin.Y + deltaY);
                StagePosition reached = command.ReadPosition();
                WaitForCalibrationSettling(cancellationToken);
                GrayFrameSnapshot shifted = camera.CaptureGrayFrame(2, 15000, cancellationToken);
                ImageTranslation translation = ImageRegistration.MeasureTranslation(reference, shifted);

                double magnitude = Math.Sqrt(translation.X * translation.X + translation.Y * translation.Y);
                double maximum = Math.Min(reference.OriginalWidth, reference.OriginalHeight) * 0.30;
                if (translation.Confidence < 8 || magnitude < 4)
                    throw new InvalidOperationException(
                        "图像纹理位移过小或不清晰，无法可靠标定。请关闭激光并打开明场照明后重试。");
                if (magnitude > maximum)
                    throw new InvalidOperationException(
                        "标定位移超过视野的 30%，为保证前后图像仍有重叠区域，标定已停止。");

                double actualDeltaX = reached.X - origin.X;
                double actualDeltaY = reached.Y - origin.Y;
                if ((Math.Abs(deltaX) > 0 && Math.Abs(actualDeltaX) < Math.Abs(deltaX) * 0.5)
                    || (Math.Abs(deltaY) > 0 && Math.Abs(actualDeltaY) < Math.Abs(deltaY) * 0.5))
                    throw new InvalidOperationException("平台实际标定位移过小，可能已经接近软限位。");

                camera.SetTemporaryOverlayPixelOffset((float)translation.X, (float)translation.Y);
                return new CenteringMeasurement
                {
                    StageDeltaX = actualDeltaX,
                    StageDeltaY = actualDeltaY,
                    ImageShiftX = translation.X,
                    ImageShiftY = translation.Y
                };
            }
            finally
            {
                if (moved)
                {
                    try
                    {
                        command.MoveAbsoluteXY(origin.X, origin.Y);
                        VerifyPosition(origin, command.ReadPosition(), dimensions);
                        camera.WaitForFreshFrames(2, 10000, CancellationToken.None);
                    }
                    finally
                    {
                        camera.SetTemporaryOverlayPixelOffset(0, 0);
                    }
                }
            }
        }

        /// <summary>
        /// 将平台移回本次扫描开始位置、校验坐标并恢复实时框选标注。
        /// </summary>
        private void ReturnToScanOrigin(CameraShowForm camera, StagePosition origin, int[] dimensions)
        {
            command.MoveAbsoluteXY(origin.X, origin.Y);
            // 平台返回起点后不能立刻切回实时画面。否则相机仍可能显示最后一个扫描点
            // 的旧帧，造成冻结参考图中的框选区域和实时样品纹理看起来错位。
            WaitForScanDelay(ScanPointSettlingDelayMilliseconds, CancellationToken.None);

            StagePosition actual = command.ReadPosition();
            VerifyPosition(origin, actual, dimensions);
            camera.WaitForFreshFrames(2, 10000, CancellationToken.None);
            camera.SetTemporaryOverlayPixelOffset(0, 0);
        }

        /// <summary>
        /// 验证平台实测位置是否已经返回指定位置。
        /// </summary>
        private static void VerifyPosition(StagePosition expected, StagePosition actual, int[] dimensions)
        {
            if (!IsAtTarget(expected, actual, dimensions))
                throw new InvalidOperationException("平台未返回预期位置，请检查平台状态和软限位。");
        }

        /// <summary>
        /// 根据控制器坐标单位返回平台位置比较容差。
        /// </summary>
        private static double GetPositionTolerance(int dimension)
        {
            return dimension == 1 || dimension == 10 ? 0.2 : 0.0002;
        }

        /// <summary>
        /// 根据控制器坐标单位选择约 10 微米的安全标定距离。
        /// </summary>
        private static double GetCalibrationDistance(int dimension)
        {
            switch (dimension)
            {
                case 1:
                case 10:
                    return 10.0;
                case 2:
                case 9:
                    return 0.01;
                default:
                    throw new InvalidOperationException(
                        "自动标定仅支持 X、Y 使用 mm 或 μm 单位（dim 1、2、9、10）。");
            }
        }

        /// <summary>
        /// 初始标定的单次相对平台位移。
        /// </summary>
        private struct CalibrationMove
        {
            /// <summary>
            /// 执行 CalibrationMove 相关的内部处理。
            /// </summary>
            internal CalibrationMove(double deltaX, double deltaY, string name)
            {
                DeltaX = deltaX;
                DeltaY = deltaY;
                Name = name;
            }

            internal double DeltaX;
            internal double DeltaY;
            internal string Name;
        }
    }
}
