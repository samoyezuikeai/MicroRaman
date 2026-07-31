using System;
using System.Collections.Generic;
using System.Drawing;

namespace MicroLaman
{
    internal sealed class AutomaticRamanMappingResult
    {
        internal IDictionary<int, Color> Colors { get; set; }
        internal string Description { get; set; }
    }

    /// <summary>
    /// 自动比较所有已提供的 Mapping 标准。它只根据数据可靠性选择呈现指标，
    /// 不能替代“用户想研究浓度、应力还是结晶度”的科学判断。
    /// </summary>
    internal static class AutomaticRamanMappingAnalyzer
    {
        internal static AutomaticRamanMappingResult Analyze(
            IList<RamanMappingSpectrum> spectra,
            AutoDetectedRamanPeak detectedPeak)
        {
            if (detectedPeak == null)
                throw new ArgumentNullException(nameof(detectedPeak));

            FullSpectrumDifferenceMappingResult fullSpectrum =
                FullSpectrumDifferenceMappingAnalyzer.Analyze(spectra);
            PcaMappingResult pca = TryAnalyzePca(spectra);

            // 宽于约 110 cm⁻¹ 的“峰”通常是荧光包络或残余基线，而不是可稳定拟合的拉曼带。
            if (detectedPeak.HalfWidth >= 55.0)
            {
                if (pca != null && pca.QualityScore > fullSpectrum.QualityScore * 1.25)
                {
                    return new AutomaticRamanMappingResult
                    {
                        Colors = pca.Colors,
                        Description = string.Format(
                            "自动选择：PCA 全谱异常（全谱组分差异优于宽荧光对比，{0} 个主成分）",
                            pca.ComponentCount)
                    };
                }
                return new AutomaticRamanMappingResult
                {
                    Colors = fullSpectrum.Colors,
                    Description = "自动选择：荧光/全谱差异（未发现可靠窄拉曼峰）"
                };
            }

            LabSpecPeakMappingResult peakArea = LabSpecPeakMappingAnalyzer.Analyze(
                spectra, detectedPeak.Center, detectedPeak.HalfWidth,
                detectedPeak.ReferenceCenter, detectedPeak.ReferenceHalfWidth,
                RamanMappingMode.PeakArea);
            LabSpecPeakMappingResult peakHeight = LabSpecPeakMappingAnalyzer.Analyze(
                spectra, detectedPeak.Center, detectedPeak.HalfWidth,
                detectedPeak.ReferenceCenter, detectedPeak.ReferenceHalfWidth,
                RamanMappingMode.PeakHeight);
            LabSpecPeakMappingResult peakPosition = TryAnalyzePeakMetric(
                spectra, detectedPeak, RamanMappingMode.PeakPosition);
            LabSpecPeakMappingResult peakWidth = TryAnalyzePeakMetric(
                spectra, detectedPeak, RamanMappingMode.PeakWidth);

            LabSpecPeakMappingResult selected = peakArea;
            string reason = "峰面积对已识别窄峰更稳健";
            if (peakHeight.QualityScore > peakArea.QualityScore * 1.25)
            {
                selected = peakHeight;
                reason = "峰高的空间分离度明显高于峰面积";
            }

            // 峰位和峰宽只有在峰足够窄且绝大多数点都能稳定定位时才允许自动胜出。
            bool precisePeak = detectedPeak.HalfWidth <= 30.0;
            if (precisePeak && peakPosition != null
                && peakPosition.ValidFraction >= 0.75
                && peakPosition.QualityScore > selected.QualityScore * 1.5
                && peakPosition.QualityScore >= 8.0)
            {
                selected = peakPosition;
                reason = "峰位变化明显且峰定位可靠";
            }
            if (precisePeak && peakWidth != null
                && peakWidth.ValidFraction >= 0.75
                && peakWidth.QualityScore > selected.QualityScore * 1.5
                && peakWidth.QualityScore >= 8.0)
            {
                selected = peakWidth;
                reason = "峰宽变化明显且半高宽拟合可靠";
            }

            // 当整谱或 PCA 的可分离度远高于单峰时，优先显示对样品更有区分力的全谱信息。
            if (fullSpectrum.QualityScore > selected.QualityScore * 1.8
                && fullSpectrum.QualityScore >= 8.0)
            {
                return new AutomaticRamanMappingResult
                {
                    Colors = fullSpectrum.Colors,
                    Description = "自动选择：荧光/全谱差异（整谱差异显著高于单峰差异）"
                };
            }
            if (pca != null && pca.QualityScore > selected.QualityScore * 1.8
                && pca.QualityScore > fullSpectrum.QualityScore * 1.1
                && pca.QualityScore >= 8.0)
            {
                return new AutomaticRamanMappingResult
                {
                    Colors = pca.Colors,
                    Description = string.Format(
                        "自动选择：PCA 全谱异常（{0} 个主成分对波形差异分离更好）",
                        pca.ComponentCount)
                };
            }

            return new AutomaticRamanMappingResult
            {
                Colors = selected.Colors,
                Description = string.Format(
                    "自动选择：{0}（{1:F1}±{2:F1} cm⁻¹，{3}{4}）",
                    selected.MetricDisplayName,
                    selected.TargetShift,
                    selected.HalfWidth,
                    reason,
                    selected.UsedReferenceNormalization
                        ? string.Format("；{0:F1} cm⁻¹ 自动参考峰归一化", selected.ReferenceShift)
                        : string.Empty)
            };
        }

        private static PcaMappingResult TryAnalyzePca(IList<RamanMappingSpectrum> spectra)
        {
            try { return PcaMappingAnalyzer.Analyze(spectra); }
            catch { return null; }
        }

        private static LabSpecPeakMappingResult TryAnalyzePeakMetric(
            IList<RamanMappingSpectrum> spectra,
            AutoDetectedRamanPeak detectedPeak,
            RamanMappingMode mode)
        {
            try
            {
                return LabSpecPeakMappingAnalyzer.Analyze(
                    spectra, detectedPeak.Center, detectedPeak.HalfWidth,
                    detectedPeak.ReferenceCenter, detectedPeak.ReferenceHalfWidth, mode);
            }
            catch { return null; }
        }
    }
}
