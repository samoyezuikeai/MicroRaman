using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroLaman
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
    /// 仿照 LabSpec Instant Image 的单变量分析：对用户指定拉曼峰做局部基线扣除和峰面积积分。
    /// 颜色阈值以全图背景的中位数和 MAD 计算，使正常背景保持蓝色，仅突出少量显著区域。
    /// </summary>
    internal static class LabSpecPeakMappingAnalyzer
    {
        internal static double DetectTargetPeak(IList<RamanMappingSpectrum> spectra)
        {
            return DetectTargetPeakParameters(spectra).Center;
        }

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

        private static double CalculateValidFraction(double[] values)
        {
            int validCount = 0;
            for (int index = 0; index < values.Length; index++)
                if (!double.IsNaN(values[index]) && !double.IsInfinity(values[index]))
                    validCount++;
            return values.Length == 0 ? 0.0 : (double)validCount / values.Length;
        }

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

        private static double InterpolateCrossing(
            double x1, double y1, double x2, double y2, double target)
        {
            double difference = y2 - y1;
            if (Math.Abs(difference) <= 1e-12) return (x1 + x2) * 0.5;
            double fraction = Clamp01((target - y1) / difference);
            return x1 + (x2 - x1) * fraction;
        }

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

        private static double[] CreateFilledArray(int length, double value)
        {
            double[] result = new double[length];
            for (int index = 0; index < length; index++)
                result[index] = value;
            return result;
        }

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

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
