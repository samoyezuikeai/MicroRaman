using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroRaman
{
    /// <summary>
    /// 集中实现应用程序使用的全部拉曼 Mapping 计算。
    /// 嵌套的数据与结果类型使 Mapping 实现保持内聚。
    /// </summary>
    internal static class RamanMappingAnalyzer
    {
        private const double MinimumRamanShift = 100.0;
        private const double MaximumRamanShift = 3100.0;
        // 通道亮度使用固定的原始峰高尺度，不从当前图像动态推导，使所有格子遵循同一颜色标准。
        // 所有 Mapping 光谱在分析前换算到 1000 ms 等效曝光；固定下限可防止全弱峰图被拉伸成亮色。
        private const double MidPeakStrength = 1000.0;
        private const double MinimumAreaStrengthPerRamanShift = 250.0;
        private const double StableColorLevelCount = 32.0;
        private const double AutomaticPeakSearchHalfWidth = 40.0;
        private const double AutomaticPeakBoundaryHalfWidth = 100.0;
        // 已确认的低强度峰至少保留约 10% 亮度，使其弱于普通峰但仍能与白色无信号背景区分。
        private const double MinimumDetectedPeakOpacity = 0.10;
        // 稳健降噪后的光谱夹角阈值。短积分时间需要略宽容差；真正不同的峰位仍会产生远低于此值的余弦相似度。
        private const double MaterialProfileSimilarityThreshold = 0.82;
        private const int MaximumMaterialGroupCount = 6;
        private static readonly Color BackgroundColor = Color.White;
        private static readonly Color[] MaterialColors =
        {
            Color.FromArgb(220, 45, 45),
            Color.FromArgb(230, 130, 30),
            Color.FromArgb(35, 165, 105),
            Color.FromArgb(125, 70, 190),
            Color.FromArgb(20, 150, 180),
            Color.FromArgb(190, 55, 145)
        };

        /// <summary>
        /// 一个已完成扫描点所对应的保存光谱。
        /// </summary>
        internal sealed class Spectrum
        {
            internal Spectrum(int scanIndex, double[] ramanShifts, double[] intensities)
            {
                ScanIndex = scanIndex;
                RamanShifts = ramanShifts;
                Intensities = intensities;
            }

            internal int ScanIndex { get; private set; }
            internal double[] RamanShifts { get; private set; }
            internal double[] Intensities { get; private set; }
        }

        /// <summary>
        /// 用户配置的单个拉曼峰通道；其颜色会与同一扫描点检测到的其他通道混合。
        /// </summary>
        internal sealed class PeakDefinition
        {
            internal PeakDefinition(
                double rangeStart,
                double rangeEnd,
                Color color,
                RamanPeakMetric metric)
            {
                RangeStart = rangeStart;
                RangeEnd = rangeEnd;
                Color = color;
                Metric = metric;
            }

            internal double RangeStart { get; private set; }
            internal double RangeEnd { get; private set; }
            internal Color Color { get; private set; }
            internal RamanPeakMetric Metric { get; private set; }
        }

        /// <summary>
        /// 所有基于波峰的 Mapping 模式共用的结果。
        /// </summary>
        internal sealed class PeakMappingResult
        {
            internal IDictionary<int, Color> Colors { get; set; }
            internal string MetricDisplayName { get; set; }
        }

        /// <summary>
        /// 全谱差异 Mapping 的结果。
        /// </summary>
        internal sealed class FullSpectrumDifferenceMappingResult
        {
            internal IDictionary<int, Color> Colors { get; set; }
        }

        /// <summary>
        /// PCA 全谱异常 Mapping 的结果。
        /// </summary>
        internal sealed class PcaMappingResult
        {
            internal IDictionary<int, Color> Colors { get; set; }
            internal int ComponentCount { get; set; }
        }

        private struct PeakMeasurement
        {
            internal double Height;
            internal double Area;
            internal double Position;
            internal double Width;
        }

        private struct PeakPoint
        {
            internal double Shift;
            internal double Value;
        }

        /// <summary>
        /// 使用归一化全谱轮廓表示一个自动识别的材料类别。
        /// </summary>
        private sealed class MaterialGroup
        {
            internal readonly List<int> Rows = new List<int>();
            internal double[] Profile;
        }

        /// <summary>
        /// A material class defined only by the strongest peak inside the user-selected interval.
        /// </summary>
        /// <summary>
        /// 去除缓慢变化的荧光或背景基线，供光谱显示和 PCA 处理使用。
        /// </summary>
        internal static double[] RemoveBaseline(double[] intensities)
        {
            if (intensities == null)
                throw new ArgumentNullException(nameof(intensities));

            double[] values = (double[])intensities.Clone();
            for (int index = 0; index < values.Length; index++)
            {
                if (double.IsNaN(values[index]) || double.IsInfinity(values[index]))
                    values[index] = 0.0;
            }
            if (values.Length < 3)
                return values;

            double[] baseline = FitLowerEnvelopeQuadratic(values);
            for (int index = 0; index < values.Length; index++)
                values[index] -= baseline[index];
            return values;
        }

        /// <summary>
        /// 在平滑前替换孤立的单像素宇宙射线尖峰。
        /// A real Raman band is preserved because both of its immediate neighbors must be quiet.
        /// </summary>
        internal static double[] RemoveCosmicRaySpikes(double[] intensities)
        {
            if (intensities == null)
                throw new ArgumentNullException(nameof(intensities));

            double[] result = (double[])intensities.Clone();
            if (result.Length < 5)
                return result;

            for (int index = 2; index < result.Length - 2; index++)
            {
                double current = intensities[index];
                if (!IsFinite(current))
                {
                    result[index] = 0.0;
                    continue;
                }

                List<double> neighbors = new List<double>(4)
                {
                    SafeValue(intensities[index - 2]),
                    SafeValue(intensities[index - 1]),
                    SafeValue(intensities[index + 1]),
                    SafeValue(intensities[index + 2])
                };
                double localMedian = Median(neighbors);
                List<double> deviations = new List<double>(neighbors.Count);
                for (int neighbor = 0; neighbor < neighbors.Count; neighbor++)
                    deviations.Add(Math.Abs(neighbors[neighbor] - localMedian));
                double localNoise = 1.4826 * Median(deviations);
                double threshold = Math.Max(6.0 * localNoise,
                    Math.Max(1.0, Math.Abs(localMedian) * 0.02));

                bool isolated = Math.Abs(current - localMedian) > threshold
                    && Math.Abs(SafeValue(intensities[index - 1]) - localMedian) <= threshold
                    && Math.Abs(SafeValue(intensities[index + 1]) - localMedian) <= threshold;
                if (isolated)
                    result[index] = (SafeValue(intensities[index - 1])
                        + SafeValue(intensities[index + 1])) * 0.5;
            }
            return result;
        }

        /// <summary>
        /// 将每个配置波峰映射到独立颜色通道；信号强度控制通道亮度，同一格子的多个波峰按色相混合。
        /// </summary>
        internal static PeakMappingResult AnalyzePeaks(
            IList<Spectrum> spectra,
            IList<PeakDefinition> peakDefinitions,
            RamanMappingMode mappingMode)
        {
            ValidateSpectra(spectra, 2);
            if (peakDefinitions == null || peakDefinitions.Count == 0)
                throw new ArgumentException("At least one peak range is required.", nameof(peakDefinitions));

            PeakMeasurement[,] measurements = new PeakMeasurement[
                spectra.Count, peakDefinitions.Count];
            double[] opacityReferences = new double[peakDefinitions.Count];
            for (int peakIndex = 0; peakIndex < peakDefinitions.Count; peakIndex++)
            {
                PeakDefinition definition = peakDefinitions[peakIndex];
                if (definition.Metric == RamanPeakMetric.Area
                    && definition.RangeEnd - definition.RangeStart < 2.0)
                    throw new ArgumentOutOfRangeException(nameof(peakDefinitions));

                List<double> detectedStrengths = new List<double>();
                for (int row = 0; row < spectra.Count; row++)
                {
                    PeakMeasurement measurement = definition.Metric == RamanPeakMetric.Height
                        ? MeasurePeakAtPosition(
                            spectra[row].RamanShifts,
                            spectra[row].Intensities,
                            definition.RangeStart)
                        : MeasurePeak(
                            spectra[row].RamanShifts,
                            spectra[row].Intensities,
                            definition.RangeStart,
                            definition.RangeEnd,
                            true);
                    measurements[row, peakIndex] = measurement;
                    RamanMappingMode peakMode = GetPeakMode(definition, mappingMode);
                    double strength = GetPeakStrength(measurement, peakMode);
                    if (IsFinite(strength) && strength > 0.0)
                        detectedStrengths.Add(strength);
                }
                opacityReferences[peakIndex] = GetOpacityReference(
                    detectedStrengths, definition, GetPeakMode(definition, mappingMode));
            }

            Dictionary<int, Color> colors = new Dictionary<int, Color>();
            for (int row = 0; row < spectra.Count; row++)
            {
                List<Color> channelColors = new List<Color>();
                List<double> channelOpacities = new List<double>();
                for (int peakIndex = 0; peakIndex < peakDefinitions.Count; peakIndex++)
                {
                    PeakDefinition definition = peakDefinitions[peakIndex];
                    double strength = GetPeakStrength(
                        measurements[row, peakIndex], GetPeakMode(definition, mappingMode));
                    double opacity = GetStableOpacity(strength, opacityReferences[peakIndex]);
                    if (opacity <= 0.0)
                        continue;
                    channelColors.Add(peakDefinitions[peakIndex].Color);
                    channelOpacities.Add(opacity);
                }
                colors[spectra[row].ScanIndex] = MixPeakChannels(channelColors, channelOpacities);
            }

            return new PeakMappingResult
            {
                Colors = colors,
                MetricDisplayName = GetMetricDisplayName(mappingMode)
            };
        }

        /// <summary>
        /// 映射每条原始光谱与中位光谱之间的均方根差异。
        /// </summary>
        internal static FullSpectrumDifferenceMappingResult AnalyzeFullSpectrumDifference(
            IList<Spectrum> spectra)
        {
            ValidateSpectra(spectra, 3);
            List<int> indexes = GetFeatureIndexes(spectra[0].RamanShifts);
            if (indexes.Count < 20)
                throw new InvalidOperationException("Not enough Raman samples are available for full-spectrum mapping.");

            int stride = Math.Max(1, indexes.Count / 350);
            List<int> sampled = SampleIndexes(indexes, stride);
            double[][] values = new double[spectra.Count][];
            for (int row = 0; row < spectra.Count; row++)
            {
                values[row] = new double[sampled.Count];
                for (int column = 0; column < sampled.Count; column++)
                {
                    double value = spectra[row].Intensities[sampled[column]];
                    values[row][column] = IsFinite(value) ? value : 0.0;
                }
            }

            double[] background = new double[sampled.Count];
            double[] columnValues = new double[spectra.Count];
            for (int column = 0; column < sampled.Count; column++)
            {
                for (int row = 0; row < spectra.Count; row++)
                    columnValues[row] = values[row][column];
                background[column] = Percentile(columnValues, 0.50);
            }

            double[] scores = new double[spectra.Count];
            for (int row = 0; row < spectra.Count; row++)
            {
                double sum = 0.0;
                for (int column = 0; column < sampled.Count; column++)
                {
                    double difference = values[row][column] - background[column];
                    sum += difference * difference;
                }
                scores[row] = Math.Sqrt(sum / sampled.Count);
            }

            double center = Percentile(scores, 0.50);
            double sigma = Math.Max(1e-9, 1.4826 * MedianAbsoluteDeviation(scores, center));
            double colorStart = center + 3.0 * sigma;
            double colorEnd = Math.Max(center + 10.0 * sigma, Percentile(scores, 0.98));
            if (colorEnd <= colorStart)
                colorEnd = colorStart + sigma;

            return new FullSpectrumDifferenceMappingResult
            {
                Colors = BuildMaterialAwareColors(spectra, scores, colorStart, colorEnd)
            };
        }

        /// <summary>
        /// 映射基线校正光谱前几个主成分中的稳健距离。
        /// </summary>
        internal static PcaMappingResult AnalyzePca(IList<Spectrum> spectra)
        {
            ValidateSpectra(spectra, 3);
            List<int> featureIndexes = GetFeatureIndexes(spectra[0].RamanShifts);
            if (featureIndexes.Count < 20)
                throw new InvalidOperationException("Not enough Raman samples are available for PCA mapping.");

            int stride = Math.Max(1, featureIndexes.Count / 300);
            List<int> sampledIndexes = SampleIndexes(featureIndexes, stride);
            int rowCount = spectra.Count;
            int columnCount = sampledIndexes.Count;
            double[,] matrix = new double[rowCount, columnCount];
            for (int row = 0; row < rowCount; row++)
            {
                double[] corrected = RemoveBaseline(spectra[row].Intensities);
                double normSquared = 0.0;
                for (int column = 0; column < columnCount; column++)
                {
                    double value = corrected[sampledIndexes[column]];
                    value = IsFinite(value) ? value : 0.0;
                    matrix[row, column] = value;
                    normSquared += value * value;
                }
                double norm = Math.Sqrt(normSquared);
                if (norm > 1e-12)
                    for (int column = 0; column < columnCount; column++)
                        matrix[row, column] /= norm;
            }

            CenterColumns(matrix, rowCount, columnCount);
            int componentCount = Math.Min(3, rowCount - 1);
            double[,] componentScores = new double[rowCount, componentCount];
            for (int component = 0; component < componentCount; component++)
            {
                double[] loading = ExtractComponent(matrix, rowCount, columnCount);
                double scoreNorm = 0.0;
                for (int row = 0; row < rowCount; row++)
                {
                    double score = 0.0;
                    for (int column = 0; column < columnCount; column++)
                        score += matrix[row, column] * loading[column];
                    componentScores[row, component] = score;
                    scoreNorm += score * score;
                }
                if (scoreNorm <= 1e-20)
                {
                    componentCount = component;
                    break;
                }
                for (int row = 0; row < rowCount; row++)
                    for (int column = 0; column < columnCount; column++)
                        matrix[row, column] -= componentScores[row, component] * loading[column];
            }
            if (componentCount == 0)
                throw new InvalidOperationException("All spectra are nearly identical, so PCA has no usable component.");

            double[] distances = CalculateRobustDistances(componentScores, rowCount, componentCount);
            double center = Percentile(distances, 0.50);
            double sigma = Math.Max(1e-6, 1.4826 * MedianAbsoluteDeviation(distances, center));
            double colorStart = center + 3.0 * sigma;
            double colorEnd = Math.Max(center + 8.0 * sigma, Percentile(distances, 0.98));
            if (colorEnd <= colorStart)
                colorEnd = colorStart + sigma;

            return new PcaMappingResult
            {
                Colors = BuildMaterialAwareColors(spectra, distances, colorStart, colorEnd),
                ComponentCount = componentCount
            };
        }

        /// <summary>
        /// 相对全谱稳健背景测量选定的拉曼窗口。窗口内只要存在明显高于普通背景的点，
        /// 即视为材料通道存在，不要求它是全谱最强峰。
        /// </summary>
        private static PeakMeasurement MeasurePeak(
            double[] shifts,
            double[] intensities,
            double rangeStart,
            double rangeEnd,
            bool integrateEntireRange = false)
        {
            if (rangeEnd <= rangeStart)
                return InvalidPeak();

            List<PeakPoint> peakPoints = new List<PeakPoint>();
            for (int index = 0; index < shifts.Length; index++)
            {
                double shift = shifts[index];
                double intensity = intensities[index];
                if (!IsFinite(shift) || !IsFinite(intensity))
                    continue;

                if (shift >= rangeStart && shift <= rangeEnd)
                    peakPoints.Add(new PeakPoint { Shift = shift, Value = intensity });
            }
            if (peakPoints.Count < 3)
                return InvalidPeak();

            peakPoints.Sort((first, second) => first.Shift.CompareTo(second.Shift));
            double spectrumBackground;
            double spectrumVariation;
            CalculateWholeSpectrumBackground(
                shifts, intensities, out spectrumBackground, out spectrumVariation);

            // 局部基线仅由用户窗口两端的水平确定，避免宽阔抬升背景被误判为该材料通道的波峰。
            double leftBaseline = MedianEdge(peakPoints, true);
            double rightBaseline = MedianEdge(peakPoints, false);
            double rangeWidth = rangeEnd - rangeStart;

            List<double> corrected = new List<double>(peakPoints.Count);
            for (int index = 0; index < peakPoints.Count; index++)
            {
                double fraction = Clamp01((peakPoints[index].Shift - rangeStart) / rangeWidth);
                double localBaseline = leftBaseline
                    + (rightBaseline - leftBaseline) * fraction;
                double value = Math.Max(0.0, peakPoints[index].Value - localBaseline);
                corrected.Add(value);
            }

            int maximumIndex = GetMaximumIndex(corrected);
            double height = corrected[maximumIndex];
            double minimumPeakHeight = Math.Max(5.0, spectrumVariation * 4.0);
            double rawPeakAboveSpectrumBackground = peakPoints[maximumIndex].Value
                - spectrumBackground;
            if (maximumIndex <= 0 || maximumIndex >= peakPoints.Count - 1
                || rawPeakAboveSpectrumBackground < minimumPeakHeight || height < 5.0)
                return InvalidPeak();
            double position = InterpolatePeakPosition(peakPoints, corrected, maximumIndex);
            double width = CalculateFwhm(peakPoints, corrected, maximumIndex, height);
            if (!integrateEntireRange && (!IsFinite(width) || width <= 0.0))
                return InvalidPeak();
            double area = integrateEntireRange
                ? CalculateEntireRangeArea(peakPoints, corrected)
                : CalculatePeakArea(peakPoints, corrected, maximumIndex, height);
            return new PeakMeasurement
            {
                Height = height,
                Area = area,
                Position = position,
                Width = width
            };
        }

        /// <summary>
        /// 定位最接近用户输入拉曼位置的峰顶，再向两侧扩展到首个局部谷底，并在该窗口内测量峰高。
        /// </summary>
        private static PeakMeasurement MeasurePeakAtPosition(
            double[] shifts,
            double[] intensities,
            double targetPosition)
        {
            List<PeakPoint> points = new List<PeakPoint>();
            for (int index = 0; index < shifts.Length && index < intensities.Length; index++)
            {
                if (IsFinite(shifts[index]) && IsFinite(intensities[index])
                    && Math.Abs(shifts[index] - targetPosition) <= AutomaticPeakBoundaryHalfWidth)
                    points.Add(new PeakPoint { Shift = shifts[index], Value = intensities[index] });
            }
            points.Sort((first, second) => first.Shift.CompareTo(second.Shift));
            if (points.Count < 5)
                return InvalidPeak();

            int apex = -1;
            for (int index = 1; index < points.Count - 1; index++)
            {
                if (Math.Abs(points[index].Shift - targetPosition) > AutomaticPeakSearchHalfWidth)
                    continue;
                if (points[index].Value >= points[index - 1].Value
                    && points[index].Value >= points[index + 1].Value
                    && (apex < 0 || points[index].Value > points[apex].Value))
                    apex = index;
            }
            if (apex < 1 || apex >= points.Count - 1)
                return InvalidPeak();

            int left = apex - 1;
            while (left > 0 && points[left - 1].Value <= points[left].Value)
                left--;
            int right = apex + 1;
            while (right < points.Count - 1 && points[right + 1].Value <= points[right].Value)
                right++;
            if (left >= apex || right <= apex || right - left < 2)
                return InvalidPeak();

            return MeasurePeak(shifts, intensities, points[left].Shift, points[right].Shift);
        }

        private static double CalculateEntireRangeArea(
            IList<PeakPoint> points,
            IList<double> values)
        {
            double area = 0.0;
            for (int index = 1; index < points.Count; index++)
            {
                double dx = Math.Abs(points[index].Shift - points[index - 1].Shift);
                area += (Math.Max(0.0, values[index - 1])
                    + Math.Max(0.0, values[index])) * 0.5 * dx;
            }
            return area;
        }

        /// <summary>
        /// 从完整有效拉曼光谱计算普通背景及稳健离散程度，仅用于判断窗口内是否存在真实抬升峰，
        /// 不参与通道亮度计算。
        /// </summary>
        private static void CalculateWholeSpectrumBackground(
            double[] shifts,
            double[] intensities,
            out double background,
            out double variation)
        {
            List<double> values = new List<double>();
            for (int index = 0; index < shifts.Length && index < intensities.Length; index++)
            {
                if (IsFinite(shifts[index]) && IsFinite(intensities[index])
                    && shifts[index] >= MinimumRamanShift
                    && shifts[index] <= MaximumRamanShift)
                    values.Add(intensities[index]);
            }
            background = Median(values);
            variation = CalculateRobustNoise(values);
        }

        /// <summary>
        /// 返回选定范围最低位移端或最高位移端十分之一数据的中位数。
        /// </summary>
        private static double MedianEdge(IList<PeakPoint> points, bool firstEdge)
        {
            int count = Math.Max(1, points.Count / 10);
            List<double> values = new List<double>(count);
            for (int index = 0; index < count; index++)
            {
                int pointIndex = firstEdge ? index : points.Count - 1 - index;
                values.Add(points[pointIndex].Value);
            }
            return Median(values);
        }

        private static double CalculateRobustNoise(IList<double> values)
        {
            if (values == null || values.Count < 4)
                return 0.0;

            double center = Median(values);
            List<double> deviations = new List<double>(values.Count);
            for (int index = 0; index < values.Count; index++)
                deviations.Add(Math.Abs(values[index] - center));
            return 1.4826 * Median(deviations);
        }

        /// <summary>
        /// 定位目标区间内经局部基线校正后的最强采样点。
        /// </summary>
        private static int GetMaximumIndex(IList<double> values)
        {
            int maximumIndex = 0;
            for (int index = 1; index < values.Count; index++)
                if (values[index] > values[maximumIndex])
                    maximumIndex = index;
            return maximumIndex;
        }

        /// <summary>
        /// 仅积分高于峰高小比例阈值的连续主峰区域，避免宽搜索窗口中的正基线噪声累积成
        /// 空间不稳定的大面积，同时保留真实峰宽变化。
        /// </summary>
        private static double CalculatePeakArea(
            IList<PeakPoint> points,
            IList<double> values,
            int maximumIndex,
            double height)
        {
            // 只积分连续峰瓣；前面的峰存在性检查已排除平坦或纯噪声窗口，此处无需再设局部噪声阈值。
            double floor = height * 0.02;

            int leftIndex = maximumIndex;
            while (leftIndex > 0 && values[leftIndex - 1] > floor)
                leftIndex--;
            if (leftIndex > 0)
                leftIndex--;

            int rightIndex = maximumIndex;
            while (rightIndex < values.Count - 1 && values[rightIndex + 1] > floor)
                rightIndex++;
            if (rightIndex < values.Count - 1)
                rightIndex++;

            double area = 0.0;
            for (int index = leftIndex + 1; index <= rightIndex; index++)
            {
                double first = Math.Max(0.0, values[index - 1] - floor);
                double second = Math.Max(0.0, values[index] - floor);
                double dx = Math.Abs(points[index].Shift - points[index - 1].Shift);
                area += (first + second) * 0.5 * dx;
            }
            return area;
        }

        /// <summary>
        /// 使用三点抛物线插值细化采样峰位。
        /// </summary>
        private static double InterpolatePeakPosition(
            IList<PeakPoint> points,
            IList<double> values,
            int maximumIndex)
        {
            double position = points[maximumIndex].Shift;
            if (maximumIndex == 0 || maximumIndex == values.Count - 1)
                return position;

            double y1 = values[maximumIndex - 1];
            double y2 = values[maximumIndex];
            double y3 = values[maximumIndex + 1];
            double denominator = y1 - 2.0 * y2 + y3;
            if (Math.Abs(denominator) <= 1e-12)
                return position;

            double offset = Clamp(-1.0, 1.0, 0.5 * (y1 - y3) / denominator);
            double halfStep = (points[maximumIndex + 1].Shift
                - points[maximumIndex - 1].Shift) * 0.5;
            return position + offset * halfStep;
        }

        /// <summary>
        /// 仅当左右半高交点均位于选定范围内时计算半高宽。
        /// </summary>
        private static double CalculateFwhm(
            IList<PeakPoint> points,
            IList<double> values,
            int maximumIndex,
            double height)
        {
            double halfHeight = height * 0.5;
            double leftCrossing = double.NaN;
            double rightCrossing = double.NaN;
            for (int index = maximumIndex; index > 0; index--)
            {
                if (values[index - 1] <= halfHeight)
                {
                    leftCrossing = InterpolateCrossing(
                        points[index - 1].Shift, values[index - 1],
                        points[index].Shift, values[index], halfHeight);
                    break;
                }
            }
            for (int index = maximumIndex; index < values.Count - 1; index++)
            {
                if (values[index + 1] <= halfHeight)
                {
                    rightCrossing = InterpolateCrossing(
                        points[index].Shift, values[index],
                        points[index + 1].Shift, values[index + 1], halfHeight);
                    break;
                }
            }
            if (!IsFinite(leftCrossing) || !IsFinite(rightCrossing))
                return double.NaN;
            return Math.Abs(rightCrossing - leftCrossing);
        }

        /// <summary>
        /// 返回 Mapping 面板标题使用的指标名称。
        /// </summary>
        private static string GetMetricDisplayName(RamanMappingMode mode)
        {
            switch (mode)
            {
                case RamanMappingMode.PeakHeight: return "Peak height / area";
                case RamanMappingMode.PeakArea: return "Peak height / area";
                case RamanMappingMode.PeakPosition: return "Peak position";
                case RamanMappingMode.PeakWidth: return "FWHM";
                default: return "Peak metric";
            }
        }

        /// <summary>
        /// 将已确认波峰的归一化强度转换为稳定的通道亮度。
        /// </summary>
        private static double GetStableOpacity(double strength, double opacityReference)
        {
            if (!IsFinite(strength) || strength <= 0.0)
                return 0.0;
            double reference = Math.Max(1e-9, opacityReference);
            // 稳健参考值代表清晰波峰，应呈现明确颜色。指数响应连续地压暗弱峰并平滑趋近满亮度，
            // 同时避免单个离群值重新定义整张图的颜色尺度。
            double opacity = 1.0 - Math.Exp(-2.3 * strength / reference);
            double stableOpacity = Math.Round(opacity * StableColorLevelCount)
                / StableColorLevelCount;
            return Math.Max(MinimumDetectedPeakOpacity, stableOpacity);
        }

        private static double GetPeakStrength(PeakMeasurement measurement, RamanMappingMode mode)
        {
            return mode == RamanMappingMode.PeakArea
                ? measurement.Area
                : measurement.Height;
        }

        private static RamanMappingMode GetPeakMode(
            PeakDefinition definition,
            RamanMappingMode mappingMode)
        {
            if (mappingMode == RamanMappingMode.PeakHeight
                || mappingMode == RamanMappingMode.PeakArea)
            {
                return definition.Metric == RamanPeakMetric.Area
                    ? RamanMappingMode.PeakArea
                    : RamanMappingMode.PeakHeight;
            }
            return mappingMode;
        }

        /// <summary>
        /// 使用已确认波峰的稳健高百分位作为扫描级增益基准，同时保留固定物理下限。
        /// 因此整体曝光或激光倍率不会改变相对亮度，而只有真实弱峰的图也不会被拉伸成亮色。
        /// </summary>
        private static double GetOpacityReference(
            IList<double> detectedStrengths,
            PeakDefinition definition,
            RamanMappingMode mode)
        {
            double fixedFloor = mode == RamanMappingMode.PeakArea
                ? MinimumAreaStrengthPerRamanShift
                    * Math.Max(1.0, definition.RangeEnd - definition.RangeStart)
                : MidPeakStrength;
            if (detectedStrengths == null || detectedStrengths.Count == 0)
                return fixedFloor;

            double[] strengths = new double[detectedStrengths.Count];
            for (int index = 0; index < strengths.Length; index++)
                strengths[index] = detectedStrengths[index];
            return Math.Max(fixedFloor, Percentile(strengths, 0.90));
        }

        /// <summary>
        /// 按亮度权重混合多个波峰颜色通道，例如黄色与蓝色会得到预期的绿色系，
        /// 而不是由其中一个通道直接覆盖另一个。
        /// </summary>
        private static Color MixPeakChannels(
            IList<Color> channelColors,
            IList<double> channelOpacities)
        {
            if (channelColors == null || channelColors.Count == 0)
                return BackgroundColor;

            double cosine = 0.0;
            double sine = 0.0;
            double saturation = 0.0;
            double colorValue = 0.0;
            double weight = 0.0;
            double remainingTransparency = 1.0;
            double fallbackHue = 0.0;
            for (int index = 0; index < channelColors.Count; index++)
            {
                double opacity = Clamp01(channelOpacities[index]);
                if (opacity <= 0.0)
                    continue;
                double hue;
                double channelSaturation;
                double value;
                ColorToHsv(channelColors[index], out hue, out channelSaturation, out value);
                if (weight <= 0.0)
                    fallbackHue = hue;
                double radians = hue * Math.PI / 180.0;
                cosine += Math.Cos(radians) * opacity;
                sine += Math.Sin(radians) * opacity;
                saturation += channelSaturation * opacity;
                colorValue += value * opacity;
                weight += opacity;
                remainingTransparency *= 1.0 - opacity;
            }
            if (weight <= 0.0)
                return BackgroundColor;

            double hueDegrees = Math.Abs(cosine) < 1e-12 && Math.Abs(sine) < 1e-12
                ? fallbackHue
                : Math.Atan2(sine, cosine) * 180.0 / Math.PI;
            if (hueDegrees < 0.0)
                hueDegrees += 360.0;
            double finalOpacity = 1.0 - remainingTransparency;
            Color mixedColor = HsvToColor(
                hueDegrees,
                saturation / weight,
                colorValue / weight);
            return BlendOverWhite(mixedColor, finalOpacity);
        }

        /// <summary>
        /// 按指定不透明度将目标颜色叠加到白色背景；弱信号接近白色，强信号接近目标颜色。
        /// </summary>
        private static Color BlendOverWhite(Color color, double opacity)
        {
            opacity = Clamp01(opacity);
            return Color.FromArgb(
                (int)Math.Round(255.0 + (color.R - 255.0) * opacity),
                (int)Math.Round(255.0 + (color.G - 255.0) * opacity),
                (int)Math.Round(255.0 + (color.B - 255.0) * opacity));
        }

        private static void ColorToHsv(Color color, out double hue, out double saturation, out double value)
        {
            double red = color.R / 255.0;
            double green = color.G / 255.0;
            double blue = color.B / 255.0;
            double maximum = Math.Max(red, Math.Max(green, blue));
            double minimum = Math.Min(red, Math.Min(green, blue));
            double delta = maximum - minimum;
            value = maximum;
            saturation = maximum <= 1e-12 ? 0.0 : delta / maximum;
            if (delta <= 1e-12)
            {
                hue = 0.0;
                return;
            }
            if (maximum == red)
                hue = 60.0 * (((green - blue) / delta) % 6.0);
            else if (maximum == green)
                hue = 60.0 * ((blue - red) / delta + 2.0);
            else
                hue = 60.0 * ((red - green) / delta + 4.0);
            if (hue < 0.0)
                hue += 360.0;
        }

        private static Color HsvToColor(double hue, double saturation, double value)
        {
            hue = (hue % 360.0 + 360.0) % 360.0;
            saturation = Clamp01(saturation);
            value = Clamp01(value);
            double chroma = value * saturation;
            double hueSegment = hue / 60.0;
            double middle = chroma * (1.0 - Math.Abs(hueSegment % 2.0 - 1.0));
            double red;
            double green;
            double blue;
            if (hueSegment < 1.0) { red = chroma; green = middle; blue = 0.0; }
            else if (hueSegment < 2.0) { red = middle; green = chroma; blue = 0.0; }
            else if (hueSegment < 3.0) { red = 0.0; green = chroma; blue = middle; }
            else if (hueSegment < 4.0) { red = 0.0; green = middle; blue = chroma; }
            else if (hueSegment < 5.0) { red = middle; green = 0.0; blue = chroma; }
            else { red = chroma; green = 0.0; blue = middle; }
            double minimum = value - chroma;
            return Color.FromArgb(255,
                (int)Math.Round((red + minimum) * 255.0),
                (int)Math.Round((green + minimum) * 255.0),
                (int)Math.Round((blue + minimum) * 255.0));
        }

        /// <summary>
        /// 按全谱形状为材料类别着色，同一类别内只改变明度；标量 Mapping 值不会改变材料色相。
        /// </summary>
        private static Dictionary<int, Color> BuildMaterialAwareColors(
            IList<Spectrum> spectra,
            double[] values,
            double colorStart,
            double colorEnd)
        {
            bool[] isMaterialSignal = new bool[spectra.Count];
            for (int row = 0; row < spectra.Count; row++)
                isMaterialSignal[row] = IsFinite(values[row]) && values[row] > colorStart;

            List<MaterialGroup> groups = BuildMaterialGroups(spectra, isMaterialSignal);
            int[] materialIndexes = new int[spectra.Count];
            for (int row = 0; row < materialIndexes.Length; row++)
                materialIndexes[row] = -1;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                for (int member = 0; member < groups[groupIndex].Rows.Count; member++)
                    materialIndexes[groups[groupIndex].Rows[member]] = groupIndex;

            Dictionary<int, Color> colors = new Dictionary<int, Color>();
            double range = Math.Max(1e-12, colorEnd - colorStart);
            for (int row = 0; row < spectra.Count; row++)
            {
                int materialIndex = materialIndexes[row];
                if (materialIndex < 0)
                {
                    colors[spectra[row].ScanIndex] = BackgroundColor;
                    continue;
                }

                double shade = Clamp01((values[row] - colorStart) / range);
                Color hue = MaterialColors[materialIndex % MaterialColors.Length];
                colors[spectra[row].ScanIndex] = GetMaterialShade(hue, shade);
            }
            return colors;
        }

        /// <summary>
        /// 仅当归一化拉曼带形状差异明显时才分离信号光谱。比较前去除幅度影响，
        /// 因此同一材料的强弱光谱共享相同色相。
        /// </summary>
        private static List<MaterialGroup> BuildMaterialGroups(
            IList<Spectrum> spectra,
            bool[] isMaterialSignal)
        {
            List<MaterialGroup> groups = new List<MaterialGroup>();
            double[][] profiles = BuildNormalizedMaterialProfiles(spectra);
            for (int row = 0; row < spectra.Count; row++)
            {
                if (!isMaterialSignal[row] || profiles[row] == null)
                    continue;

                int bestGroup = -1;
                double bestSimilarity = double.NegativeInfinity;
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    double similarity = DotProduct(profiles[row], groups[groupIndex].Profile);
                    if (similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestGroup = groupIndex;
                    }
                }

                if (bestGroup >= 0 && bestSimilarity >= MaterialProfileSimilarityThreshold)
                {
                    AddProfileToGroup(groups[bestGroup], row, profiles[row]);
                }
                else if (groups.Count < MaximumMaterialGroupCount)
                {
                    MaterialGroup group = new MaterialGroup { Profile = (double[])profiles[row].Clone() };
                    group.Rows.Add(row);
                    groups.Add(group);
                }
                else
                {
                    AddProfileToGroup(groups[bestGroup], row, profiles[row]);
                }
            }
            return groups;
        }

        /// <summary>
        /// 从已保存、扣暗谱并校正基线的拉曼光谱构建单位长度轮廓；
        /// 最后一次去尖峰可避免坏 CCD 像素被误判为新材料。
        /// </summary>
        private static double[][] BuildNormalizedMaterialProfiles(IList<Spectrum> spectra)
        {
            List<int> featureIndexes = GetFeatureIndexes(spectra[0].RamanShifts);
            int stride = Math.Max(1, featureIndexes.Count / 240);
            List<int> sampledIndexes = SampleIndexes(featureIndexes, stride);
            double[][] profiles = new double[spectra.Count][];
            for (int row = 0; row < spectra.Count; row++)
            {
                double[] corrected = RemoveCosmicRaySpikes(spectra[row].Intensities);
                double[] smoothed = SmoothMaterialProfile(corrected, 11);
                List<double> featureValues = new List<double>(featureIndexes.Count);
                for (int index = 0; index < featureIndexes.Count; index++)
                    if (IsFinite(smoothed[featureIndexes[index]]))
                        featureValues.Add(smoothed[featureIndexes[index]]);

                double center = Median(featureValues);
                List<double> deviations = new List<double>(featureValues.Count);
                for (int index = 0; index < featureValues.Count; index++)
                    deviations.Add(Math.Abs(featureValues[index] - center));
                double noise = 1.4826 * Median(deviations);
                double signalFloor = center + 3.0 * noise;

                double[] profile = new double[sampledIndexes.Count];
                double normSquared = 0.0;
                for (int column = 0; column < sampledIndexes.Count; column++)
                {
                    double value = smoothed[sampledIndexes[column]] - signalFloor;
                    profile[column] = IsFinite(value) && value > 0.0 ? value : 0.0;
                    normSquared += profile[column] * profile[column];
                }

                // 极弱但已通过局部验证的波峰仍需可比较轮廓；若稳健下限移除了全部样本，
                // 则回退到减去中位数后的正值。
                if (normSquared <= 1e-12)
                {
                    for (int column = 0; column < sampledIndexes.Count; column++)
                    {
                        double value = smoothed[sampledIndexes[column]] - center;
                        profile[column] = IsFinite(value) && value > 0.0 ? value : 0.0;
                        normSquared += profile[column] * profile[column];
                    }
                }

                double norm = Math.Sqrt(normSquared);
                if (norm <= 1e-12)
                    continue;
                for (int column = 0; column < profile.Length; column++)
                    profile[column] /= norm;
                profiles[row] = profile;
            }
            return profiles;
        }

        /// <summary>
        /// 在比较材料指纹前降低宽带随机噪声。
        /// </summary>
        private static double[] SmoothMaterialProfile(double[] values, int windowSize)
        {
            double[] result = new double[values.Length];
            int halfWindow = windowSize / 2;
            for (int index = 0; index < values.Length; index++)
            {
                int start = Math.Max(0, index - halfWindow);
                int end = Math.Min(values.Length - 1, index + halfWindow);
                double sum = 0.0;
                for (int sample = start; sample <= end; sample++)
                    sum += SafeValue(values[sample]);
                result[index] = sum / (end - start + 1);
            }
            return result;
        }

        /// <summary>
        /// 更新材料质心并重新归一化，以保持余弦相似度比较有效。
        /// </summary>
        private static void AddProfileToGroup(MaterialGroup group, int row, double[] profile)
        {
            int oldCount = group.Rows.Count;
            for (int column = 0; column < group.Profile.Length; column++)
                group.Profile[column] = (group.Profile[column] * oldCount + profile[column]) / (oldCount + 1.0);

            double normSquared = DotProduct(group.Profile, group.Profile);
            double norm = Math.Sqrt(normSquared);
            if (norm > 1e-12)
                for (int column = 0; column < group.Profile.Length; column++)
                    group.Profile[column] /= norm;
            group.Rows.Add(row);
        }

        /// <summary>
        /// 计算余弦相似度；输入轮廓均已归一化为单位长度。
        /// </summary>
        private static double DotProduct(double[] first, double[] second)
        {
            double result = 0.0;
            for (int index = 0; index < first.Length; index++)
                result += first[index] * second[index];
            return result;
        }

        /// <summary>
        /// 每种已识别材料使用一种色相，仅依据选定 Mapping 值调整亮度。
        /// </summary>
        private static Color GetMaterialShade(Color hue, double value)
        {
            double shade = Clamp01(value);
            return BlendOverWhite(hue, 0.10 + 0.90 * shade);
        }

        /// <summary>
        /// 验证扫描光谱，并确认所有点使用相同的拉曼坐标轴。
        /// </summary>
        private static void ValidateSpectra(IList<Spectrum> spectra, int minimumCount)
        {
            if (spectra == null || spectra.Count < minimumCount)
                throw new InvalidOperationException("Not enough complete scan spectra are available for mapping.");

            Spectrum first = spectra[0];
            if (first == null || first.RamanShifts == null || first.Intensities == null
                || first.RamanShifts.Length < 20
                || first.RamanShifts.Length != first.Intensities.Length)
                throw new InvalidOperationException("The first saved spectrum is incomplete.");

            for (int row = 0; row < spectra.Count; row++)
            {
                Spectrum spectrum = spectra[row];
                if (spectrum == null || spectrum.RamanShifts == null || spectrum.Intensities == null
                    || spectrum.RamanShifts.Length != first.RamanShifts.Length
                    || spectrum.Intensities.Length != first.Intensities.Length)
                    throw new InvalidOperationException("The saved spectra do not have matching lengths.");

                for (int index = 0; index < first.RamanShifts.Length; index++)
                {
                    if (!IsFinite(spectrum.RamanShifts[index])
                        || Math.Abs(spectrum.RamanShifts[index] - first.RamanShifts[index]) > 0.01)
                        throw new InvalidOperationException("The saved spectra do not share the same Raman axis.");
                }
            }
        }

        /// <summary>
        /// 获取 100～3100 cm⁻¹ 范围内供全谱算法使用的采样索引。
        /// </summary>
        private static List<int> GetFeatureIndexes(double[] shifts)
        {
            List<int> indexes = new List<int>();
            for (int index = 0; index < shifts.Length; index++)
            {
                double shift = shifts[index];
                if (IsFinite(shift) && shift >= MinimumRamanShift && shift <= MaximumRamanShift)
                    indexes.Add(index);
            }
            return indexes;
        }

        /// <summary>
        /// 等间距保留采样点，使较长的 Mapping 计算保持响应速度。
        /// </summary>
        private static List<int> SampleIndexes(IList<int> indexes, int stride)
        {
            List<int> sampled = new List<int>();
            for (int index = 0; index < indexes.Count; index += stride)
                sampled.Add(indexes[index]);
            return sampled;
        }

        /// <summary>
        /// 将每个 PCA 特征列中心化到其平均值。
        /// </summary>
        private static void CenterColumns(double[,] matrix, int rows, int columns)
        {
            for (int column = 0; column < columns; column++)
            {
                double mean = 0.0;
                for (int row = 0; row < rows; row++)
                    mean += matrix[row, column];
                mean /= rows;
                for (int row = 0; row < rows; row++)
                    matrix[row, column] -= mean;
            }
        }

        /// <summary>
        /// 使用幂迭代提取一个主成分载荷向量。
        /// </summary>
        private static double[] ExtractComponent(double[,] matrix, int rows, int columns)
        {
            double[] loading = new double[columns];
            for (int column = 0; column < columns; column++)
                loading[column] = 1.0 / Math.Sqrt(columns);

            for (int iteration = 0; iteration < 30; iteration++)
            {
                double[] scores = new double[rows];
                for (int row = 0; row < rows; row++)
                    for (int column = 0; column < columns; column++)
                        scores[row] += matrix[row, column] * loading[column];

                double[] next = new double[columns];
                for (int column = 0; column < columns; column++)
                    for (int row = 0; row < rows; row++)
                        next[column] += matrix[row, column] * scores[row];
                double norm = 0.0;
                for (int column = 0; column < columns; column++)
                    norm += next[column] * next[column];
                norm = Math.Sqrt(norm);
                if (norm <= 1e-20)
                    break;
                for (int column = 0; column < columns; column++)
                    next[column] /= norm;
                loading = next;
            }
            return loading;
        }

        /// <summary>
        /// 为每个 PCA 数据行计算稳健的多分量距离。
        /// </summary>
        private static double[] CalculateRobustDistances(double[,] scores, int rows, int components)
        {
            double[] result = new double[rows];
            for (int component = 0; component < components; component++)
            {
                double[] values = new double[rows];
                for (int row = 0; row < rows; row++)
                    values[row] = scores[row, component];
                double median = Percentile(values, 0.50);
                double scale = Math.Max(1e-9, 1.4826 * MedianAbsoluteDeviation(values, median));
                for (int row = 0; row < rows; row++)
                {
                    double standardized = (scores[row, component] - median) / scale;
                    result[row] += standardized * standardized;
                }
            }
            for (int row = 0; row < rows; row++)
                result[row] = Math.Sqrt(result[row]);
            return result;
        }

        /// <summary>
        /// 拟合下包络二次曲线以去除缓慢变化的基线。
        /// </summary>
        private static double[] FitLowerEnvelopeQuadratic(double[] values)
        {
            int count = values.Length;
            double[] weights = new double[count];
            double[] baseline = new double[count];
            for (int index = 0; index < count; index++)
                weights[index] = 1.0;

            for (int iteration = 0; iteration < 8; iteration++)
            {
                double[] coefficients = FitWeightedQuadratic(values, weights);
                for (int index = 0; index < count; index++)
                {
                    double x = -1.0 + 2.0 * index / (count - 1.0);
                    baseline[index] = coefficients[0] + coefficients[1] * x + coefficients[2] * x * x;
                    weights[index] = values[index] > baseline[index] ? 0.03 : 1.0;
                }
            }
            return baseline;
        }

        /// <summary>
        /// 求解加权二次最小二乘正规方程。
        /// </summary>
        private static double[] FitWeightedQuadratic(double[] values, double[] weights)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
            double y0 = 0, y1 = 0, y2 = 0;
            int count = values.Length;
            for (int index = 0; index < count; index++)
            {
                double x = -1.0 + 2.0 * index / (count - 1.0);
                double x2 = x * x;
                double weight = weights[index];
                double value = values[index];
                s0 += weight;
                s1 += weight * x;
                s2 += weight * x2;
                s3 += weight * x2 * x;
                s4 += weight * x2 * x2;
                y0 += weight * value;
                y1 += weight * x * value;
                y2 += weight * x2 * value;
            }
            double[,] matrix =
            {
                { s0, s1, s2, y0 },
                { s1, s2, s3, y1 },
                { s2, s3, s4, y2 }
            };
            return SolveThreeByThree(matrix);
        }

        /// <summary>
        /// 使用高斯消元求解三变量增广矩阵。
        /// </summary>
        private static double[] SolveThreeByThree(double[,] matrix)
        {
            for (int pivot = 0; pivot < 3; pivot++)
            {
                int bestRow = pivot;
                for (int row = pivot + 1; row < 3; row++)
                    if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[bestRow, pivot]))
                        bestRow = row;
                if (bestRow != pivot)
                {
                    for (int column = pivot; column < 4; column++)
                    {
                        double temporary = matrix[pivot, column];
                        matrix[pivot, column] = matrix[bestRow, column];
                        matrix[bestRow, column] = temporary;
                    }
                }
                double divisor = matrix[pivot, pivot];
                if (Math.Abs(divisor) < 1e-12)
                    return new double[3];
                for (int column = pivot; column < 4; column++)
                    matrix[pivot, column] /= divisor;
                for (int row = 0; row < 3; row++)
                {
                    if (row == pivot)
                        continue;
                    double factor = matrix[row, pivot];
                    for (int column = pivot; column < 4; column++)
                        matrix[row, column] -= factor * matrix[pivot, column];
                }
            }
            return new[] { matrix[0, 3], matrix[1, 3], matrix[2, 3] };
        }

        /// <summary>
        /// 在两个相邻采样点之间线性插值阈值交点。
        /// </summary>
        private static double InterpolateCrossing(
            double x1, double y1, double x2, double y2, double target)
        {
            double difference = y2 - y1;
            if (Math.Abs(difference) <= 1e-12)
                return (x1 + x2) * 0.5;
            double fraction = Clamp01((target - y1) / difference);
            return x1 + (x2 - x1) * fraction;
        }

        /// <summary>
        /// 在不修改调用方数组或列表的情况下返回稳健中位数。
        /// </summary>
        private static double Median(IList<double> values)
        {
            if (values == null || values.Count == 0)
                return 0.0;
            double[] ordered = new double[values.Count];
            for (int index = 0; index < values.Count; index++)
                ordered[index] = values[index];
            Array.Sort(ordered);
            int middle = ordered.Length / 2;
            return ordered.Length % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) * 0.5
                : ordered[middle];
        }

        /// <summary>
        /// 在不修改调用方数据的情况下返回插值百分位数。
        /// </summary>
        private static double Percentile(double[] values, double percentile)
        {
            if (values == null || values.Length == 0)
                return 0.0;
            double[] ordered = (double[])values.Clone();
            Array.Sort(ordered);
            if (ordered.Length == 1)
                return ordered[0];
            double position = Clamp01(percentile) * (ordered.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return ordered[lower];
            double fraction = position - lower;
            return ordered[lower] * (1.0 - fraction) + ordered[upper] * fraction;
        }

        /// <summary>
        /// 返回相对指定中心的绝对偏差中位数。
        /// </summary>
        private static double MedianAbsoluteDeviation(double[] values, double center)
        {
            double[] deviations = new double[values.Length];
            for (int index = 0; index < values.Length; index++)
                deviations[index] = Math.Abs(values[index] - center);
            return Percentile(deviations, 0.50);
        }

        /// <summary>
        /// 检查数值是否可用于数值测量。
        /// </summary>
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// 在局部预处理中将无效仪器数值转换为零。
        /// </summary>
        private static double SafeValue(double value)
        {
            return IsFinite(value) ? value : 0.0;
        }

        /// <summary>
        /// 创建用于缺失波峰或纯噪声局部波峰的无效标记。
        /// </summary>
        private static PeakMeasurement InvalidPeak()
        {
            return new PeakMeasurement
            {
                Height = double.NaN,
                Area = double.NaN,
                Position = double.NaN,
                Width = double.NaN
            };
        }

        /// <summary>
        /// 将数值限制在零到一之间。
        /// </summary>
        private static double Clamp01(double value)
        {
            return Clamp(0.0, 1.0, value);
        }

        /// <summary>
        /// 将数值限制在指定闭区间内。
        /// </summary>
        private static double Clamp(double minimum, double maximum, double value)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
