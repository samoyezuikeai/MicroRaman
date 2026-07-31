using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroLaman
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

        private static double MedianAbsoluteDeviation(double[] values, double center)
        {
            double[] deviations = new double[values.Length];
            for (int index = 0; index < values.Length; index++)
                deviations[index] = Math.Abs(values[index] - center);
            return Percentile(deviations, 0.50);
        }

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

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
