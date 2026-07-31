using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroLaman
{
    /// <summary>一条已按扫描顺序保存的拉曼光谱。</summary>
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

    /// <summary>每个扫描点的光谱差异得分及其伪彩色。</summary>
    internal sealed class RamanMappingResult
    {
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
    /// 自动比较整条拉曼谱并为每个实测点分配伪彩色；不进行空间插值。
    /// 蓝色表示接近全图中位参考谱，红色表示光谱形状差异较大。
    /// </summary>
    internal static class RamanMappingAnalyzer
    {
        private const double MinimumRamanShift = 100.0;
        private const double MaximumRamanShift = 3100.0;
        private const double CommonPeakMaskMinimum = 480.0;
        private const double CommonPeakMaskMaximum = 560.0;

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

        /// <summary>低包络基线校正后做向量归一化，比较谱形而不是总亮度。</summary>
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

        /// <summary>使用低包络二次拟合去除缓慢变化的基线，不改变窄拉曼峰的位置。</summary>
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

        /// <summary>谱角距离：0 表示波形完全一致，整体强度缩放不会改变结果。</summary>
        private static double CalculateWaveformDistance(double[] spectrum, double[] reference)
        {
            double dotProduct = 0.0;
            for (int index = 0; index < spectrum.Length; index++)
                dotProduct += spectrum[index] * reference[index];
            dotProduct = Math.Max(-1.0, Math.Min(1.0, dotProduct));
            return Math.Acos(dotProduct) / Math.PI;
        }

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

        private static int Interpolate(int first, int second, double fraction)
        {
            return (int)Math.Round(first + (second - first) * fraction);
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
