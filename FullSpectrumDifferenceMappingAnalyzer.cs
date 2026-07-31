using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroLaman
{
    internal sealed class FullSpectrumDifferenceMappingResult
    {
        internal IDictionary<int, Color> Colors { get; set; }
        internal double ContrastRatio { get; set; }
    }

    /// <summary>
    /// 当数据只有宽荧光/基线形状、没有可靠窄拉曼峰时，比较每条全谱与全图中位背景谱的差异。
    /// 该图只表示光谱/荧光差异，不宣称为某一个拉曼峰的化学分布。
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
                ContrastRatio = colorEnd / Math.Max(1e-9, center)
            };
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
