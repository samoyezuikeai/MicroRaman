using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroRaman
{
    /// <summary>
    /// 一条已按扫描顺序保存的拉曼光谱。
    /// </summary>
    internal sealed class RamanMappingSpectrum
    {
        internal RamanMappingSpectrum(int scanIndex, double[] ramanShifts, double[] intensities)
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
    /// 每个扫描点的光谱差异得分及其伪彩色。
    /// </summary>
    internal sealed class RamanMappingResult
    {
        /// <summary>
        /// 执行 RamanMappingResult 相关的内部处理。
        /// </summary>
        internal RamanMappingResult(
            IDictionary<int, double> scores,
            IDictionary<int, Color> colors,
            double colorMinimum,
            double colorMaximum)
        {
            Scores = scores;
            Colors = colors;
            ColorMinimum = colorMinimum;
            ColorMaximum = colorMaximum;
        }

        internal IDictionary<int, double> Scores { get; private set; }
        internal IDictionary<int, Color> Colors { get; private set; }
        internal double ColorMinimum { get; private set; }
        internal double ColorMaximum { get; private set; }
    }

    /// <summary>
    /// 自动比较整条拉曼谱并为每个实测点分配伪彩色；不进行空间插值。 蓝色表示接近全图中位参考谱，红色表示光谱形状差异较大。
    /// </summary>
    internal static class RamanMappingAnalyzer
    {
        private const double MinimumRamanShift = 100.0;
        private const double MaximumRamanShift = 3100.0;
        private const double CommonPeakMaskMinimum = 480.0;
        private const double CommonPeakMaskMaximum = 560.0;

        /// <summary>
        /// 分析相关的内部处理。
        /// </summary>
        internal static RamanMappingResult Analyze(IList<RamanMappingSpectrum> spectra)
        {
            if (spectra == null || spectra.Count < 2)
                throw new InvalidOperationException("至少需要两个完整扫描点才能生成拉曼 Mapping。");

            RamanMappingSpectrum first = spectra[0];
            ValidateSpectrum(first);
            HashSet<int> scanIndexes = new HashSet<int>();
            List<int> validIndexes = GetValidIndexes(first.RamanShifts, true);
            if (validIndexes.Count < 20)
                validIndexes = GetValidIndexes(first.RamanShifts, false);
            if (validIndexes.Count < 20)
                throw new InvalidOperationException("有效拉曼位移范围内的数据点不足，无法生成 Mapping。");

            double[][] processed = new double[spectra.Count][];
            for (int index = 0; index < spectra.Count; index++)
            {
                RamanMappingSpectrum spectrum = spectra[index];
                ValidateCompatibleSpectrum(first, spectrum);
                if (!scanIndexes.Add(spectrum.ScanIndex))
                    throw new InvalidOperationException("扫描点序号重复，无法生成 Mapping。");
                processed[index] = Preprocess(
                    spectrum.RamanShifts,
                    spectrum.Intensities,
                    validIndexes);
            }

            double[] median = NormalizeWaveform(CalculateFeatureMedian(processed));
            double[] reference = SelectNearestMeasuredSpectrum(processed, median);
            double[] scoreValues = new double[spectra.Count];
            Dictionary<int, double> scores = new Dictionary<int, double>();
            for (int index = 0; index < spectra.Count; index++)
            {
                double score = CalculateWaveformDistance(processed[index], reference);
                scoreValues[index] = score;
                scores[spectra[index].ScanIndex] = score;
            }

            // 波形距离小于约 0.02（谱角约 3.6°）通常只是噪声，不应被自动拉伸成红色。
            double colorMinimum = 0.0;
            double colorMaximum = Math.Max(0.02, Percentile(scoreValues, 0.98));
            bool hasContrast = colorMaximum - colorMinimum > 1e-12;
            Dictionary<int, Color> colors = new Dictionary<int, Color>();
            for (int index = 0; index < spectra.Count; index++)
            {
                double normalized = hasContrast
                    ? Clamp01((scoreValues[index] - colorMinimum) / (colorMaximum - colorMinimum))
                    : 0.5;
                colors[spectra[index].ScanIndex] = GetPseudoColor(normalized);
            }

            return new RamanMappingResult(scores, colors, colorMinimum, colorMaximum);
        }

        /// <summary>
        /// 校验Spectrum相关的内部处理。
        /// </summary>
        private static void ValidateSpectrum(RamanMappingSpectrum spectrum)
        {
            if (spectrum == null || spectrum.RamanShifts == null || spectrum.Intensities == null
                || spectrum.RamanShifts.Length < 20
                || spectrum.RamanShifts.Length != spectrum.Intensities.Length)
            {
                throw new InvalidOperationException("保存的扫描点光谱数据不完整，无法生成 Mapping。");
            }

            for (int index = 0; index < spectrum.RamanShifts.Length; index++)
            {
                double shift = spectrum.RamanShifts[index];
                if (double.IsNaN(shift) || double.IsInfinity(shift))
                    throw new InvalidOperationException("扫描点包含无效的拉曼位移，无法生成 Mapping。");
            }
        }

        /// <summary>
        /// 校验CompatibleSpectrum相关的内部处理。
        /// </summary>
        private static void ValidateCompatibleSpectrum(
            RamanMappingSpectrum reference,
            RamanMappingSpectrum spectrum)
        {
            ValidateSpectrum(spectrum);
            if (spectrum.RamanShifts.Length != reference.RamanShifts.Length)
                throw new InvalidOperationException("各扫描点的拉曼位移长度不一致，无法生成 Mapping。");

            for (int index = 0; index < reference.RamanShifts.Length; index++)
            {
                if (Math.Abs(spectrum.RamanShifts[index] - reference.RamanShifts[index]) > 0.01)
                    throw new InvalidOperationException("各扫描点的拉曼位移坐标不一致，无法生成 Mapping。");
            }
        }

        /// <summary>
        /// 获取ValidIndexes相关的内部处理。
        /// </summary>
        private static List<int> GetValidIndexes(double[] ramanShifts, bool maskCommonPeak)
        {
            List<int> indexes = new List<int>();
            for (int index = 0; index < ramanShifts.Length; index++)
            {
                double shift = ramanShifts[index];
                if (double.IsNaN(shift) || double.IsInfinity(shift))
                    continue;
                if (shift < MinimumRamanShift || shift > MaximumRamanShift)
                    continue;
                if (maskCommonPeak && shift >= CommonPeakMaskMinimum && shift <= CommonPeakMaskMaximum)
                    continue;
                indexes.Add(index);
            }
            return indexes;
        }

        /// <summary>
        /// 低包络基线校正后做向量归一化，比较谱形而不是总亮度。
        /// </summary>
        private static double[] Preprocess(
            double[] ramanShifts,
            double[] intensities,
            IList<int> validIndexes)
        {
            double[] baselineCorrected = RemoveBaseline(intensities);
            int count = validIndexes.Count;
            double[] values = new double[count];
            for (int index = 0; index < count; index++)
            {
                double value = baselineCorrected[validIndexes[index]];
                values[index] = double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
            }

            // Mapping 只比较波形：减去平均值去除常量偏移，再单位化去除整体强度变化。
            // 520 cm^-1 区域已由 validIndexes 排除，因此其峰高变化不会影响颜色。
            return NormalizeWaveform(values);
        }

        /// <summary>
        /// 使用低包络二次拟合去除缓慢变化的基线，不改变窄拉曼峰的位置。
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
        /// 归一化Waveform相关的内部处理。
        /// </summary>
        private static double[] NormalizeWaveform(double[] values)
        {
            double[] normalized = (double[])values.Clone();
            if (normalized.Length == 0)
                return normalized;
            double mean = 0.0;
            for (int index = 0; index < normalized.Length; index++)
                mean += normalized[index];
            mean /= normalized.Length;

            double normSquared = 0.0;
            for (int index = 0; index < normalized.Length; index++)
            {
                normalized[index] -= mean;
                normSquared += normalized[index] * normalized[index];
            }
            double norm = Math.Sqrt(normSquared);
            if (norm <= 1e-12)
                return normalized;
            for (int index = 0; index < normalized.Length; index++)
                normalized[index] /= norm;
            return normalized;
        }

        /// <summary>
        /// 拟合LowerEnvelopeQuadratic相关的内部处理。
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
                    double x = count == 1 ? 0.0 : -1.0 + 2.0 * index / (count - 1.0);
                    baseline[index] = coefficients[0] + coefficients[1] * x + coefficients[2] * x * x;
                    weights[index] = values[index] > baseline[index] ? 0.03 : 1.0;
                }
            }
            return baseline;
        }

        /// <summary>
        /// 拟合WeightedQuadratic相关的内部处理。
        /// </summary>
        private static double[] FitWeightedQuadratic(double[] values, double[] weights)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
            double y0 = 0, y1 = 0, y2 = 0;
            int count = values.Length;
            for (int index = 0; index < count; index++)
            {
                double x = count == 1 ? 0.0 : -1.0 + 2.0 * index / (count - 1.0);
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
        /// 求解ThreeByThree相关的内部处理。
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
        /// 计算FeatureMedian相关的内部处理。
        /// </summary>
        private static double[] CalculateFeatureMedian(double[][] spectra)
        {
            int featureCount = spectra[0].Length;
            double[] median = new double[featureCount];
            double[] column = new double[spectra.Length];
            for (int feature = 0; feature < featureCount; feature++)
            {
                for (int row = 0; row < spectra.Length; row++)
                    column[row] = spectra[row][feature];
                Array.Sort(column);
                int middle = column.Length / 2;
                median[feature] = column.Length % 2 == 0
                    ? (column[middle - 1] + column[middle]) / 2.0
                    : column[middle];
            }
            return median;
        }

        /// <summary>
        /// 用最接近全图中位谱的实际测量谱作参考，避免把两个不同谱形平均成一条不存在的谱。
        /// </summary>
        private static double[] SelectNearestMeasuredSpectrum(double[][] spectra, double[] median)
        {
            int bestIndex = 0;
            double bestDifference = double.MaxValue;
            for (int index = 0; index < spectra.Length; index++)
            {
                double difference = CalculateWaveformDistance(spectra[index], median);
                if (difference < bestDifference)
                {
                    bestDifference = difference;
                    bestIndex = index;
                }
            }
            return spectra[bestIndex];
        }

        /// <summary>
        /// 谱角距离：0 表示波形完全一致，整体强度缩放不会改变结果。
        /// </summary>
        private static double CalculateWaveformDistance(double[] spectrum, double[] reference)
        {
            double dotProduct = 0.0;
            for (int index = 0; index < spectrum.Length; index++)
                dotProduct += spectrum[index] * reference[index];
            dotProduct = Math.Max(-1.0, Math.Min(1.0, dotProduct));
            return Math.Acos(dotProduct) / Math.PI;
        }

        /// <summary>
        /// 执行 Percentile 相关的内部处理。
        /// </summary>
        private static double Percentile(double[] values, double percentile)
        {
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
        /// 获取PseudoColor相关的内部处理。
        /// </summary>
        private static Color GetPseudoColor(double value)
        {
            Color[] anchors =
            {
                Color.FromArgb(48, 18, 123),
                Color.FromArgb(0, 170, 255),
                Color.FromArgb(70, 210, 95),
                Color.FromArgb(255, 225, 40),
                Color.FromArgb(210, 25, 25)
            };
            double scaled = Clamp01(value) * (anchors.Length - 1);
            int lower = Math.Min(anchors.Length - 2, (int)Math.Floor(scaled));
            double fraction = scaled - lower;
            Color first = anchors[lower];
            Color second = anchors[lower + 1];
            return Color.FromArgb(
                Interpolate(first.R, second.R, fraction),
                Interpolate(first.G, second.G, fraction),
                Interpolate(first.B, second.B, fraction));
        }

        /// <summary>
        /// 插值相关的内部处理。
        /// </summary>
        private static int Interpolate(int first, int second, double fraction)
        {
            return (int)Math.Round(first + (second - first) * fraction);
        }

        /// <summary>
        /// 限制01相关的内部处理。
        /// </summary>
        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}

#region 其它 Raman Mapping 分析标准

namespace MicroRaman
{
    internal sealed class AutoDetectedRamanPeak
    {
        internal double Center { get; set; }
        internal double HalfWidth { get; set; }
        internal double ReferenceCenter { get; set; }
        internal double ReferenceHalfWidth { get; set; }
    }

    internal sealed class LabSpecPeakMappingResult
    {
        internal IDictionary<int, Color> Colors { get; set; }
        internal double TargetShift { get; set; }
        internal double HalfWidth { get; set; }
        internal bool UsedReferenceNormalization { get; set; }
        internal double ReferenceShift { get; set; }
        internal string MetricDisplayName { get; set; }
        internal double QualityScore { get; set; }
        internal double ValidFraction { get; set; }
    }

    /// <summary>
    /// 仿照 LabSpec Instant Image 的单变量分析：对用户指定拉曼峰做局部基线扣除和峰面积积分。 颜色阈值以全图背景的中位数和 MAD 计算，使正常背景保持蓝色，仅突出少量显著区域。
    /// </summary>
    internal static class LabSpecPeakMappingAnalyzer
    {
        internal static double DetectTargetPeak(IList<RamanMappingSpectrum> spectra)
        {
            return DetectTargetPeakParameters(spectra).Center;
        }

        /// <summary>
        /// 检测TargetPeakParameters相关的内部处理。
        /// </summary>
        internal static AutoDetectedRamanPeak DetectTargetPeakParameters(
            IList<RamanMappingSpectrum> spectra)
        {
            ValidateSpectra(spectra);
            RamanMappingSpectrum first = spectra[0];
            int length = first.Intensities.Length;
            double[][] correctedSpectra = new double[spectra.Count][];
            double[] firstMaximum = CreateFilledArray(length, double.NegativeInfinity);
            double[] secondMaximum = CreateFilledArray(length, double.NegativeInfinity);
            double[] thirdMaximum = CreateFilledArray(length, double.NegativeInfinity);

            for (int spectrumIndex = 0; spectrumIndex < spectra.Count; spectrumIndex++)
            {
                RamanMappingSpectrum spectrum = spectra[spectrumIndex];
                double[] corrected = RamanMappingAnalyzer.RemoveBaseline(spectrum.Intensities);
                correctedSpectra[spectrumIndex] = corrected;
                for (int index = 0; index < length; index++)
                {
                    double value = corrected[index];
                    if (value > firstMaximum[index])
                    {
                        thirdMaximum[index] = secondMaximum[index];
                        secondMaximum[index] = firstMaximum[index];
                        firstMaximum[index] = value;
                    }
                    else if (value > secondMaximum[index])
                    {
                        thirdMaximum[index] = secondMaximum[index];
                        secondMaximum[index] = value;
                    }
                    else if (value > thirdMaximum[index])
                    {
                        thirdMaximum[index] = value;
                    }
                }
            }

            double[] contrast = new double[length];
            double[] medianProfile = new double[length];
            double[] relativeDispersion = new double[length];
            double[] wavelengthValues = new double[spectra.Count];
            for (int index = 0; index < length; index++)
            {
                for (int spectrumIndex = 0; spectrumIndex < spectra.Count; spectrumIndex++)
                    wavelengthValues[spectrumIndex] = correctedSpectra[spectrumIndex][index];
                double background = Percentile(wavelengthValues, 0.50);
                medianProfile[index] = Math.Max(0.0, background);
                double[] deviations = new double[wavelengthValues.Length];
                for (int spectrumIndex = 0; spectrumIndex < wavelengthValues.Length; spectrumIndex++)
                    deviations[spectrumIndex] = Math.Abs(wavelengthValues[spectrumIndex] - background);
                double dispersion = 1.4826 * Percentile(deviations, 0.50);
                relativeDispersion[index] = dispersion
                    / Math.Max(1e-9, Math.Abs(background));
                double third = double.IsNegativeInfinity(thirdMaximum[index])
                    ? firstMaximum[index]
                    : thirdMaximum[index];
                double upper = (firstMaximum[index] + secondMaximum[index] + third) / 3.0;
                contrast[index] = Math.Max(0.0, upper - background);
            }

            double bestScore = double.NegativeInfinity;
            double bestShift = 960.0;
            int bestIndex = -1;
            for (int index = 2; index < length - 2; index++)
            {
                double shift = first.RamanShifts[index];
                if (shift < 150.0 || shift > 3100.0)
                    continue;
                double score = 0.0;
                for (int neighbor = index - 2; neighbor <= index + 2; neighbor++)
                    score += contrast[neighbor];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestShift = shift;
                    bestIndex = index;
                }
            }

            double halfWidth = EstimatePeakHalfWidth(first.RamanShifts, contrast, bestIndex);
            int referenceIndex = DetectStableReferencePeak(
                first.RamanShifts,
                medianProfile,
                relativeDispersion,
                bestShift,
                halfWidth);
            double referenceCenter = referenceIndex >= 0
                ? first.RamanShifts[referenceIndex]
                : double.NaN;
            double referenceHalfWidth = referenceIndex >= 0
                ? EstimatePeakHalfWidth(first.RamanShifts, medianProfile, referenceIndex)
                : double.NaN;
            return new AutoDetectedRamanPeak
            {
                Center = bestShift,
                HalfWidth = halfWidth,
                ReferenceCenter = referenceCenter,
                ReferenceHalfWidth = referenceHalfWidth
            };
        }

        /// <summary>
        /// 检测StableReferencePeak相关的内部处理。
        /// </summary>
        private static int DetectStableReferencePeak(
            double[] shifts,
            double[] medianProfile,
            double[] relativeDispersion,
            double targetCenter,
            double targetHalfWidth)
        {
            double minimumSeparation = Math.Max(50.0, targetHalfWidth * 2.0);
            double minimumStrength = Percentile(medianProfile, 0.85);
            double bestScore = double.NegativeInfinity;
            int bestIndex = -1;
            for (int index = 2; index < shifts.Length - 2; index++)
            {
                double shift = shifts[index];
                if (shift < 150.0 || shift > 3100.0
                    || Math.Abs(shift - targetCenter) < minimumSeparation)
                    continue;

                double strength = 0.0;
                double dispersion = 0.0;
                for (int neighbor = index - 2; neighbor <= index + 2; neighbor++)
                {
                    strength += medianProfile[neighbor];
                    dispersion += relativeDispersion[neighbor];
                }
                strength /= 5.0;
                dispersion /= 5.0;
                bool isLocalMaximum = medianProfile[index] >= medianProfile[index - 1]
                    && medianProfile[index] >= medianProfile[index + 1];
                // 参考峰必须明显、普遍存在且跨点相对波动小；否则宁可不归一化。
                if (!isLocalMaximum || strength < minimumStrength || dispersion > 0.12)
                    continue;
                double score = strength / (1.0 + 20.0 * dispersion);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = index;
                }
            }
            return bestIndex;
        }

        /// <summary>
        /// 估算PeakHalfWidth相关的内部处理。
        /// </summary>
        private static double EstimatePeakHalfWidth(double[] shifts, double[] contrast, int peakIndex)
        {
            if (peakIndex < 1 || peakIndex >= shifts.Length - 1 || contrast[peakIndex] <= 0.0)
                return 20.0;

            double halfHeight = contrast[peakIndex] * 0.5;
            int left = peakIndex;
            while (left > 0 && contrast[left] > halfHeight) left--;
            int right = peakIndex;
            while (right < contrast.Length - 1 && contrast[right] > halfHeight) right++;
            double fwhm = Math.Abs(shifts[right] - shifts[left]);
            if (double.IsNaN(fwhm) || double.IsInfinity(fwhm) || fwhm <= 0.0)
                return 20.0;

            // 搜索窗口略宽于 FWHM，既覆盖整个峰，又尽量避免相邻峰混入。
            return Math.Max(10.0, Math.Min(80.0, fwhm * 0.85));
        }

        /// <summary>
        /// 分析相关的内部处理。
        /// </summary>
        internal static LabSpecPeakMappingResult Analyze(
            IList<RamanMappingSpectrum> spectra,
            double targetShift,
            double halfWidth,
            double referenceShift,
            double referenceHalfWidth,
            RamanMappingMode mappingMode)
        {
            ValidateSpectra(spectra);
            if (halfWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(halfWidth));

            bool supportsNormalization = mappingMode == RamanMappingMode.PeakHeight
                || mappingMode == RamanMappingMode.PeakArea;
            bool useReference = supportsNormalization
                && !double.IsNaN(referenceShift)
                && !double.IsInfinity(referenceShift)
                && referenceHalfWidth > 0.0
                && Math.Abs(targetShift - referenceShift) > halfWidth + referenceHalfWidth;
            double[] values = new double[spectra.Count];
            for (int index = 0; index < spectra.Count; index++)
            {
                RamanMappingSpectrum spectrum = spectra[index];
                double[] corrected = RamanMappingAnalyzer.RemoveBaseline(spectrum.Intensities);
                PeakMeasurement target = MeasurePeak(
                    spectrum.RamanShifts, corrected, targetShift, halfWidth);
                double value = SelectMetric(target, mappingMode);
                if (useReference)
                {
                    PeakMeasurement reference = MeasurePeak(
                        spectrum.RamanShifts, corrected, referenceShift, referenceHalfWidth);
                    double referenceValue = mappingMode == RamanMappingMode.PeakHeight
                        ? reference.Height
                        : reference.Area;
                    value = referenceValue > 1e-9 ? value / referenceValue : double.NaN;
                }
                values[index] = value;
            }

            Dictionary<int, Color> colors = BuildColors(spectra, values, mappingMode);

            return new LabSpecPeakMappingResult
            {
                Colors = colors,
                TargetShift = targetShift,
                HalfWidth = halfWidth,
                UsedReferenceNormalization = useReference,
                ReferenceShift = referenceShift,
                MetricDisplayName = GetMetricDisplayName(mappingMode),
                QualityScore = CalculateQualityScore(values),
                ValidFraction = CalculateValidFraction(values)
            };
        }

        /// <summary>
        /// 计算ValidFraction相关的内部处理。
        /// </summary>
        private static double CalculateValidFraction(double[] values)
        {
            int validCount = 0;
            for (int index = 0; index < values.Length; index++)
                if (!double.IsNaN(values[index]) && !double.IsInfinity(values[index]))
                    validCount++;
            return values.Length == 0 ? 0.0 : (double)validCount / values.Length;
        }

        /// <summary>
        /// 计算QualityScore相关的内部处理。
        /// </summary>
        private static double CalculateQualityScore(double[] values)
        {
            List<double> valid = new List<double>();
            for (int index = 0; index < values.Length; index++)
                if (!double.IsNaN(values[index]) && !double.IsInfinity(values[index]))
                    valid.Add(values[index]);
            if (valid.Count < 3) return 0.0;

            double[] ordered = valid.ToArray();
            double center = Percentile(ordered, 0.50);
            double[] deviations = new double[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
                deviations[index] = Math.Abs(ordered[index] - center);
            double sigma = Math.Max(1e-12, 1.4826 * Percentile(deviations, 0.50));
            return Math.Max(0.0, (Percentile(ordered, 0.98) - center) / sigma);
        }

        private struct PeakMeasurement
        {
            internal double Height;
            internal double Area;
            internal double Position;
            internal double Width;
        }

        /// <summary>
        /// 测量Peak相关的内部处理。
        /// </summary>
        private static PeakMeasurement MeasurePeak(
            double[] shifts,
            double[] intensities,
            double center,
            double halfWidth)
        {
            List<double> leftSide = new List<double>();
            List<double> rightSide = new List<double>();
            for (int index = 0; index < shifts.Length; index++)
            {
                double shift = shifts[index];
                if (shift >= center - 2.5 * halfWidth && shift <= center - 1.5 * halfWidth)
                    leftSide.Add(intensities[index]);
                else if (shift >= center + 1.5 * halfWidth && shift <= center + 2.5 * halfWidth)
                    rightSide.Add(intensities[index]);
            }
            double leftBaseline = Median(leftSide);
            double rightBaseline = Median(rightSide);

            double area = 0.0;
            double previousShift = double.NaN;
            double previousValue = 0.0;
            List<int> peakIndexes = new List<int>();
            List<double> peakValues = new List<double>();
            for (int index = 0; index < shifts.Length; index++)
            {
                double shift = shifts[index];
                if (shift < center - halfWidth || shift > center + halfWidth)
                    continue;
                double fraction = (shift - (center - halfWidth)) / (2.0 * halfWidth);
                double baseline = leftBaseline + (rightBaseline - leftBaseline) * Clamp01(fraction);
                double value = Math.Max(0.0, intensities[index] - baseline);
                peakIndexes.Add(index);
                peakValues.Add(value);
                if (!double.IsNaN(previousShift))
                    area += (previousValue + value) * 0.5 * Math.Abs(shift - previousShift);
                previousShift = shift;
                previousValue = value;
            }
            if (peakValues.Count < 3)
                return new PeakMeasurement { Height = double.NaN, Area = double.NaN,
                    Position = double.NaN, Width = double.NaN };

            int maximumIndex = 0;
            for (int index = 1; index < peakValues.Count; index++)
                if (peakValues[index] > peakValues[maximumIndex]) maximumIndex = index;
            double height = peakValues[maximumIndex];
            if (height <= 1e-12)
                return new PeakMeasurement { Height = 0.0, Area = 0.0,
                    Position = double.NaN, Width = double.NaN };

            List<double> sideSamples = new List<double>(leftSide.Count + rightSide.Count);
            sideSamples.AddRange(leftSide);
            sideSamples.AddRange(rightSide);
            double sideCenter = Median(new List<double>(sideSamples));
            List<double> sideDeviations = new List<double>(sideSamples.Count);
            for (int index = 0; index < sideSamples.Count; index++)
                sideDeviations.Add(Math.Abs(sideSamples[index] - sideCenter));
            double localNoise = 1.4826 * Median(sideDeviations);
            bool hasReliablePeak = sideSamples.Count < 4
                || height >= Math.Max(1e-12, 3.0 * localNoise);

            double position = shifts[peakIndexes[maximumIndex]];
            if (maximumIndex > 0 && maximumIndex < peakValues.Count - 1)
            {
                double y1 = peakValues[maximumIndex - 1];
                double y2 = peakValues[maximumIndex];
                double y3 = peakValues[maximumIndex + 1];
                double denominator = y1 - 2.0 * y2 + y3;
                if (Math.Abs(denominator) > 1e-12)
                {
                    double offset = Math.Max(-1.0, Math.Min(1.0, 0.5 * (y1 - y3) / denominator));
                    double step = (shifts[peakIndexes[maximumIndex + 1]]
                        - shifts[peakIndexes[maximumIndex - 1]]) * 0.5;
                    position += offset * step;
                }
            }

            double halfHeight = height * 0.5;
            double leftCrossing = double.NaN;
            double rightCrossing = double.NaN;
            for (int index = maximumIndex; index > 0; index--)
            {
                if (peakValues[index - 1] <= halfHeight)
                {
                    leftCrossing = InterpolateCrossing(
                        shifts[peakIndexes[index - 1]], peakValues[index - 1],
                        shifts[peakIndexes[index]], peakValues[index], halfHeight);
                    break;
                }
            }
            for (int index = maximumIndex; index < peakValues.Count - 1; index++)
            {
                if (peakValues[index + 1] <= halfHeight)
                {
                    rightCrossing = InterpolateCrossing(
                        shifts[peakIndexes[index]], peakValues[index],
                        shifts[peakIndexes[index + 1]], peakValues[index + 1], halfHeight);
                    break;
                }
            }
            double width = double.IsNaN(leftCrossing) || double.IsNaN(rightCrossing)
                ? double.NaN
                : Math.Abs(rightCrossing - leftCrossing);
            return new PeakMeasurement
            {
                Height = height,
                Area = area,
                Position = hasReliablePeak ? position : double.NaN,
                Width = hasReliablePeak ? width : double.NaN
            };
        }

        /// <summary>
        /// 选择Metric相关的内部处理。
        /// </summary>
        private static double SelectMetric(PeakMeasurement peak, RamanMappingMode mode)
        {
            switch (mode)
            {
                case RamanMappingMode.PeakHeight: return peak.Height;
                case RamanMappingMode.PeakPosition: return peak.Position;
                case RamanMappingMode.PeakWidth: return peak.Width;
                default: return peak.Area;
            }
        }

        /// <summary>
        /// 获取MetricDisplayName相关的内部处理。
        /// </summary>
        private static string GetMetricDisplayName(RamanMappingMode mode)
        {
            switch (mode)
            {
                case RamanMappingMode.PeakHeight: return "峰高";
                case RamanMappingMode.PeakPosition: return "峰位置";
                case RamanMappingMode.PeakWidth: return "半高宽 FWHM";
                default: return "峰面积";
            }
        }

        private static Dictionary<int, Color> BuildColors(
            IList<RamanMappingSpectrum> spectra,
            double[] values,
            RamanMappingMode mode)
        {
            List<double> valid = new List<double>();
            for (int index = 0; index < values.Length; index++)
                if (!double.IsNaN(values[index]) && !double.IsInfinity(values[index]))
                    valid.Add(values[index]);
            if (valid.Count < 2)
                throw new InvalidOperationException("有效峰参数不足，无法生成 Mapping。请检查目标峰中心和搜索半宽。");

            double[] validValues = valid.ToArray();
            double colorStart;
            double colorEnd;
            if (mode == RamanMappingMode.PeakHeight || mode == RamanMappingMode.PeakArea)
            {
                double background = Percentile(validValues, 0.50);
                double[] deviations = new double[validValues.Length];
                for (int index = 0; index < validValues.Length; index++)
                    deviations[index] = Math.Abs(validValues[index] - background);
                double sigma = 1.4826 * Percentile(deviations, 0.50);
                sigma = Math.Max(sigma, Math.Max(1e-12, Math.Abs(background) * 0.01));
                colorStart = background + 3.0 * sigma;
                colorEnd = Math.Max(background + 10.0 * sigma, Percentile(validValues, 0.98));
                if (colorEnd <= colorStart) colorEnd = colorStart + sigma;
            }
            else
            {
                // 峰位和峰宽表示参数本身的空间分布，使用稳健的 2%~98% 色阶。
                colorStart = Percentile(validValues, 0.02);
                colorEnd = Percentile(validValues, 0.98);
                if (colorEnd <= colorStart) colorEnd = colorStart + 1e-9;
            }

            Dictionary<int, Color> colors = new Dictionary<int, Color>();
            Color backgroundColor = GetPseudoColor(0.0);
            for (int index = 0; index < spectra.Count; index++)
            {
                double value = values[index];
                double normalized = double.IsNaN(value) || double.IsInfinity(value)
                    ? 0.0
                    : Clamp01((value - colorStart) / (colorEnd - colorStart));
                colors[spectra[index].ScanIndex] = double.IsNaN(value)
                    ? backgroundColor
                    : GetPseudoColor(normalized);
            }
            return colors;
        }

        /// <summary>
        /// 插值Crossing相关的内部处理。
        /// </summary>
        private static double InterpolateCrossing(
            double x1, double y1, double x2, double y2, double target)
        {
            double difference = y2 - y1;
            if (Math.Abs(difference) <= 1e-12) return (x1 + x2) * 0.5;
            double fraction = Clamp01((target - y1) / difference);
            return x1 + (x2 - x1) * fraction;
        }

        /// <summary>
        /// 校验Spectra相关的内部处理。
        /// </summary>
        private static void ValidateSpectra(IList<RamanMappingSpectrum> spectra)
        {
            if (spectra == null || spectra.Count < 2)
                throw new InvalidOperationException("至少需要两个完整扫描点才能生成拉曼 Mapping。");
            int length = spectra[0].RamanShifts.Length;
            if (length < 20 || spectra[0].Intensities.Length != length)
                throw new InvalidOperationException("扫描光谱数据不完整。");
            for (int index = 1; index < spectra.Count; index++)
            {
                if (spectra[index].RamanShifts.Length != length
                    || spectra[index].Intensities.Length != length)
                    throw new InvalidOperationException("各扫描点的光谱长度不一致。");
            }
        }

        /// <summary>
        /// 创建FilledArray相关的内部处理。
        /// </summary>
        private static double[] CreateFilledArray(int length, double value)
        {
            double[] result = new double[length];
            for (int index = 0; index < length; index++)
                result[index] = value;
            return result;
        }

        /// <summary>
        /// 执行 Median 相关的内部处理。
        /// </summary>
        private static double Median(List<double> values)
        {
            if (values.Count == 0)
                return 0.0;
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2.0
                : values[middle];
        }

        /// <summary>
        /// 执行 Percentile 相关的内部处理。
        /// </summary>
        private static double Percentile(double[] values, double percentile)
        {
            double[] ordered = (double[])values.Clone();
            Array.Sort(ordered);
            double position = Clamp01(percentile) * (ordered.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return ordered[lower];
            double fraction = position - lower;
            return ordered[lower] * (1.0 - fraction) + ordered[upper] * fraction;
        }

        /// <summary>
        /// 获取PseudoColor相关的内部处理。
        /// </summary>
        private static Color GetPseudoColor(double value)
        {
            Color[] anchors =
            {
                Color.FromArgb(35, 35, 150),
                Color.FromArgb(0, 145, 235),
                Color.FromArgb(65, 195, 105),
                Color.FromArgb(255, 220, 45),
                Color.FromArgb(210, 30, 30)
            };
            double scaled = Clamp01(value) * (anchors.Length - 1);
            int lower = Math.Min(anchors.Length - 2, (int)Math.Floor(scaled));
            double fraction = scaled - lower;
            Color first = anchors[lower];
            Color second = anchors[lower + 1];
            return Color.FromArgb(
                (int)Math.Round(first.R + (second.R - first.R) * fraction),
                (int)Math.Round(first.G + (second.G - first.G) * fraction),
                (int)Math.Round(first.B + (second.B - first.B) * fraction));
        }

        /// <summary>
        /// 限制01相关的内部处理。
        /// </summary>
        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}

namespace MicroRaman
{
    internal sealed class FullSpectrumDifferenceMappingResult
    {
        internal IDictionary<int, Color> Colors { get; set; }
        internal double ContrastRatio { get; set; }
        internal double QualityScore { get; set; }
    }

    /// <summary>
    /// 当数据只有宽荧光/基线形状、没有可靠窄拉曼峰时，比较每条全谱与全图中位背景谱的差异。 该图只表示光谱/荧光差异，不宣称为某一个拉曼峰的化学分布。
    /// </summary>
    internal static class FullSpectrumDifferenceMappingAnalyzer
    {
        internal static FullSpectrumDifferenceMappingResult Analyze(
            IList<RamanMappingSpectrum> spectra)
        {
            if (spectra == null || spectra.Count < 3)
                throw new InvalidOperationException("全谱差异 Mapping 至少需要 3 个扫描点。");

            int length = spectra[0].Intensities.Length;
            List<int> indexes = new List<int>();
            for (int index = 0; index < length; index++)
            {
                double shift = spectra[0].RamanShifts[index];
                if (shift >= 100.0 && shift <= 3100.0)
                    indexes.Add(index);
            }
            if (indexes.Count < 20)
                throw new InvalidOperationException("有效拉曼位移范围内的数据点不足。");

            int stride = Math.Max(1, indexes.Count / 350);
            List<int> sampled = new List<int>();
            for (int index = 0; index < indexes.Count; index += stride)
                sampled.Add(indexes[index]);

            double[][] values = new double[spectra.Count][];
            for (int row = 0; row < spectra.Count; row++)
            {
                if (spectra[row].Intensities.Length != length)
                    throw new InvalidOperationException("各扫描点的光谱长度不一致。");
                values[row] = new double[sampled.Count];
                for (int column = 0; column < sampled.Count; column++)
                {
                    double value = spectra[row].Intensities[sampled[column]];
                    values[row][column] = double.IsNaN(value) || double.IsInfinity(value)
                        ? 0.0
                        : value;
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
            if (colorEnd <= colorStart) colorEnd = colorStart + sigma;

            Dictionary<int, Color> colors = new Dictionary<int, Color>();
            for (int row = 0; row < spectra.Count; row++)
            {
                double normalized = Clamp01((scores[row] - colorStart) / (colorEnd - colorStart));
                colors[spectra[row].ScanIndex] = GetPseudoColor(normalized);
            }
            return new FullSpectrumDifferenceMappingResult
            {
                Colors = colors,
                ContrastRatio = colorEnd / Math.Max(1e-9, center),
                QualityScore = Math.Max(0.0, (Percentile(scores, 0.98) - center) / sigma)
            };
        }

        /// <summary>
        /// 执行 MedianAbsoluteDeviation 相关的内部处理。
        /// </summary>
        private static double MedianAbsoluteDeviation(double[] values, double center)
        {
            double[] deviations = new double[values.Length];
            for (int index = 0; index < values.Length; index++)
                deviations[index] = Math.Abs(values[index] - center);
            return Percentile(deviations, 0.50);
        }

        /// <summary>
        /// 执行 Percentile 相关的内部处理。
        /// </summary>
        private static double Percentile(double[] values, double percentile)
        {
            double[] ordered = (double[])values.Clone();
            Array.Sort(ordered);
            double position = Clamp01(percentile) * (ordered.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return ordered[lower];
            double fraction = position - lower;
            return ordered[lower] * (1.0 - fraction) + ordered[upper] * fraction;
        }

        /// <summary>
        /// 获取PseudoColor相关的内部处理。
        /// </summary>
        private static Color GetPseudoColor(double value)
        {
            Color[] anchors =
            {
                Color.FromArgb(35, 35, 150), Color.FromArgb(0, 145, 235),
                Color.FromArgb(65, 195, 105), Color.FromArgb(255, 220, 45),
                Color.FromArgb(210, 30, 30)
            };
            double scaled = Clamp01(value) * (anchors.Length - 1);
            int lower = Math.Min(anchors.Length - 2, (int)Math.Floor(scaled));
            double fraction = scaled - lower;
            Color first = anchors[lower];
            Color second = anchors[lower + 1];
            return Color.FromArgb(
                (int)Math.Round(first.R + (second.R - first.R) * fraction),
                (int)Math.Round(first.G + (second.G - first.G) * fraction),
                (int)Math.Round(first.B + (second.B - first.B) * fraction));
        }

        /// <summary>
        /// 限制01相关的内部处理。
        /// </summary>
        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}

namespace MicroRaman
{
    internal sealed class PcaMappingResult
    {
        internal IDictionary<int, Color> Colors { get; set; }
        internal int ComponentCount { get; set; }
        internal double QualityScore { get; set; }
    }

    /// <summary>
    /// 对基线校正、向量归一化后的全谱执行 PCA，并用前三个主成分中的稳健距离突出少量异常光谱。
    /// </summary>
    internal static class PcaMappingAnalyzer
    {
        internal static PcaMappingResult Analyze(IList<RamanMappingSpectrum> spectra)
        {
            if (spectra == null || spectra.Count < 3)
                throw new InvalidOperationException("PCA Mapping 至少需要 3 个完整扫描点。");

            List<int> featureIndexes = GetFeatureIndexes(spectra[0].RamanShifts);
            if (featureIndexes.Count < 20)
                throw new InvalidOperationException("有效拉曼位移范围内的数据点不足，无法执行 PCA。");

            // 最多取约 300 个等间隔变量，避免大矩阵拖慢界面，同时保留完整拉曼范围。
            int stride = Math.Max(1, featureIndexes.Count / 300);
            List<int> sampledIndexes = new List<int>();
            for (int index = 0; index < featureIndexes.Count; index += stride)
                sampledIndexes.Add(featureIndexes[index]);

            int rowCount = spectra.Count;
            int columnCount = sampledIndexes.Count;
            double[,] matrix = new double[rowCount, columnCount];
            for (int row = 0; row < rowCount; row++)
            {
                RamanMappingSpectrum spectrum = spectra[row];
                if (spectrum.RamanShifts.Length != spectra[0].RamanShifts.Length
                    || spectrum.Intensities.Length != spectrum.RamanShifts.Length)
                    throw new InvalidOperationException("各扫描点的光谱长度不一致，无法执行 PCA。");

                double[] corrected = RamanMappingAnalyzer.RemoveBaseline(spectrum.Intensities);
                double normSquared = 0.0;
                for (int column = 0; column < columnCount; column++)
                {
                    double value = corrected[sampledIndexes[column]];
                    if (double.IsNaN(value) || double.IsInfinity(value))
                        value = 0.0;
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
            double[,] scores = new double[rowCount, componentCount];
            for (int component = 0; component < componentCount; component++)
            {
                double[] loading = ExtractComponent(matrix, rowCount, columnCount);
                double scoreNorm = 0.0;
                for (int row = 0; row < rowCount; row++)
                {
                    double score = 0.0;
                    for (int column = 0; column < columnCount; column++)
                        score += matrix[row, column] * loading[column];
                    scores[row, component] = score;
                    scoreNorm += score * score;
                }
                if (scoreNorm <= 1e-20)
                {
                    componentCount = component;
                    break;
                }
                for (int row = 0; row < rowCount; row++)
                    for (int column = 0; column < columnCount; column++)
                        matrix[row, column] -= scores[row, component] * loading[column];
            }
            if (componentCount == 0)
                throw new InvalidOperationException("所有扫描点的波形几乎相同，PCA 无法形成有效主成分。");

            double[] distances = CalculateRobustDistances(scores, rowCount, componentCount);
            double center = Percentile(distances, 0.50);
            double sigma = 1.4826 * MedianAbsoluteDeviation(distances, center);
            sigma = Math.Max(sigma, 1e-6);
            double colorStart = center + 3.0 * sigma;
            double colorEnd = Math.Max(center + 8.0 * sigma, Percentile(distances, 0.98));
            if (colorEnd <= colorStart)
                colorEnd = colorStart + sigma;

            Dictionary<int, Color> colors = new Dictionary<int, Color>();
            for (int row = 0; row < rowCount; row++)
            {
                double value = Clamp01((distances[row] - colorStart) / (colorEnd - colorStart));
                colors[spectra[row].ScanIndex] = GetPseudoColor(value);
            }
            return new PcaMappingResult
            {
                Colors = colors,
                ComponentCount = componentCount,
                QualityScore = Math.Max(0.0, (Percentile(distances, 0.98) - center) / sigma)
            };
        }

        /// <summary>
        /// 获取FeatureIndexes相关的内部处理。
        /// </summary>
        private static List<int> GetFeatureIndexes(double[] shifts)
        {
            List<int> indexes = new List<int>();
            for (int index = 0; index < shifts.Length; index++)
            {
                double shift = shifts[index];
                if (shift >= 100.0 && shift <= 3100.0)
                    indexes.Add(index);
            }
            return indexes;
        }

        /// <summary>
        /// 执行 CenterColumns 相关的内部处理。
        /// </summary>
        private static void CenterColumns(double[,] matrix, int rows, int columns)
        {
            for (int column = 0; column < columns; column++)
            {
                double mean = 0.0;
                for (int row = 0; row < rows; row++) mean += matrix[row, column];
                mean /= rows;
                for (int row = 0; row < rows; row++) matrix[row, column] -= mean;
            }
        }

        /// <summary>
        /// 提取Component相关的内部处理。
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
                for (int column = 0; column < columns; column++) norm += next[column] * next[column];
                norm = Math.Sqrt(norm);
                if (norm <= 1e-20) break;
                for (int column = 0; column < columns; column++) next[column] /= norm;
                loading = next;
            }
            return loading;
        }

        /// <summary>
        /// 计算RobustDistances相关的内部处理。
        /// </summary>
        private static double[] CalculateRobustDistances(double[,] scores, int rows, int components)
        {
            double[] result = new double[rows];
            for (int component = 0; component < components; component++)
            {
                double[] values = new double[rows];
                for (int row = 0; row < rows; row++) values[row] = scores[row, component];
                double median = Percentile(values, 0.50);
                double scale = Math.Max(1e-9, 1.4826 * MedianAbsoluteDeviation(values, median));
                for (int row = 0; row < rows; row++)
                {
                    double standardized = (scores[row, component] - median) / scale;
                    result[row] += standardized * standardized;
                }
            }
            for (int row = 0; row < rows; row++) result[row] = Math.Sqrt(result[row]);
            return result;
        }

        /// <summary>
        /// 执行 MedianAbsoluteDeviation 相关的内部处理。
        /// </summary>
        private static double MedianAbsoluteDeviation(double[] values, double center)
        {
            double[] deviations = new double[values.Length];
            for (int index = 0; index < values.Length; index++)
                deviations[index] = Math.Abs(values[index] - center);
            return Percentile(deviations, 0.50);
        }

        /// <summary>
        /// 执行 Percentile 相关的内部处理。
        /// </summary>
        private static double Percentile(double[] values, double percentile)
        {
            double[] ordered = (double[])values.Clone();
            Array.Sort(ordered);
            double position = Clamp01(percentile) * (ordered.Length - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper) return ordered[lower];
            double fraction = position - lower;
            return ordered[lower] * (1.0 - fraction) + ordered[upper] * fraction;
        }

        /// <summary>
        /// 获取PseudoColor相关的内部处理。
        /// </summary>
        private static Color GetPseudoColor(double value)
        {
            Color[] anchors =
            {
                Color.FromArgb(35, 35, 150), Color.FromArgb(0, 145, 235),
                Color.FromArgb(65, 195, 105), Color.FromArgb(255, 220, 45),
                Color.FromArgb(210, 30, 30)
            };
            double scaled = Clamp01(value) * (anchors.Length - 1);
            int lower = Math.Min(anchors.Length - 2, (int)Math.Floor(scaled));
            double fraction = scaled - lower;
            Color first = anchors[lower];
            Color second = anchors[lower + 1];
            return Color.FromArgb(
                (int)Math.Round(first.R + (second.R - first.R) * fraction),
                (int)Math.Round(first.G + (second.G - first.G) * fraction),
                (int)Math.Round(first.B + (second.B - first.B) * fraction));
        }

        /// <summary>
        /// 限制01相关的内部处理。
        /// </summary>
        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}

#endregion

