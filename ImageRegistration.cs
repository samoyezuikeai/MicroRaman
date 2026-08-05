using System;
using System.Numerics;

namespace MicroRaman
{
    /// <summary>
    /// 用于图像配准的降采样灰度帧。
    /// </summary>
    internal sealed class GrayFrameSnapshot
    {
        /// <summary>
        /// 创建灰度帧并保留其与原始相机像素之间的比例。
        /// </summary>
        internal GrayFrameSnapshot(int width, int height, int originalWidth, int originalHeight, int samplingStep, byte[] pixels)
        {
            Width = width;
            Height = height;
            OriginalWidth = originalWidth;
            OriginalHeight = originalHeight;
            SamplingStep = samplingStep;
            Pixels = pixels;
        }

        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal int OriginalWidth { get; private set; }
        internal int OriginalHeight { get; private set; }
        internal int SamplingStep { get; private set; }
        internal byte[] Pixels { get; private set; }
    }

    /// <summary>
    /// 图像相对参考帧的 X/Y 位移及相关峰可信度。
    /// </summary>
    internal struct ImageTranslation
    {
        internal double X;
        internal double Y;
        internal double Confidence;
    }

    /// <summary>
    /// 缓存参考帧频谱和可复用工作区，避免逐帧大量分配内存。
    /// </summary>
    internal sealed class PreparedImageRegistration
    {
        internal GrayFrameSnapshot Reference;
        internal int FftWidth;
        internal int FftHeight;
        internal Complex[] ReferenceSpectrum;
        internal Complex[] WorkSpectrum;
        internal Complex[] Correlation;
    }

    /// <summary>
    /// 使用相位相关计算两帧图像之间的亚像素平移。
    /// </summary>
    internal static class ImageRegistration
    {
        /// <summary>
        /// 一次性准备参考帧并测量目标帧位移。
        /// </summary>
        internal static ImageTranslation MeasureTranslation(GrayFrameSnapshot reference, GrayFrameSnapshot moved)
        {
            return MeasureTranslation(Prepare(reference), moved);
        }

        /// <summary>
        /// 预计算参考帧频谱及后续测量所需工作缓冲。
        /// </summary>
        private static PreparedImageRegistration Prepare(GrayFrameSnapshot reference)
        {
            if (reference == null)
                throw new ArgumentNullException("reference");

            int fftWidth = NextPowerOfTwo(reference.Width);
            int fftHeight = NextPowerOfTwo(reference.Height);
            Complex[] spectrum = CreateWindowedImage(reference, fftWidth, fftHeight);
            Transform2D(spectrum, fftWidth, fftHeight, false);
            return new PreparedImageRegistration
            {
                Reference = reference,
                FftWidth = fftWidth,
                FftHeight = fftHeight,
                ReferenceSpectrum = spectrum,
                WorkSpectrum = new Complex[spectrum.Length],
                Correlation = new Complex[spectrum.Length]
            };
        }

        /// <summary>
        /// 执行相位相关、峰值搜索和抛物线亚像素拟合。
        /// </summary>
        private static ImageTranslation MeasureTranslation(
            PreparedImageRegistration prepared,
            GrayFrameSnapshot moved)
        {
            if (prepared == null || moved == null)
                throw new ArgumentNullException("校准图像不能为空。");
            GrayFrameSnapshot reference = prepared.Reference;
            if (reference.Width != moved.Width || reference.Height != moved.Height || reference.SamplingStep != moved.SamplingStep)
                throw new InvalidOperationException("校准前后的相机分辨率不一致。");

            int fftWidth = prepared.FftWidth;
            int fftHeight = prepared.FftHeight;
            Complex[] second = prepared.WorkSpectrum;
            FillWindowedImage(moved, fftWidth, fftHeight, second);
            Transform2D(second, fftWidth, fftHeight, false);

            Complex[] correlation = prepared.Correlation;
            for (int i = 0; i < correlation.Length; i++)
            {
                Complex value = Complex.Conjugate(prepared.ReferenceSpectrum[i]) * second[i];
                double magnitude = value.Magnitude;
                correlation[i] = magnitude > 1e-12 ? value / magnitude : Complex.Zero;
            }

            Transform2D(correlation, fftWidth, fftHeight, true);
            int peakX = 0;
            int peakY = 0;
            double peak = double.MinValue;
            double absoluteSum = 0;
            for (int y = 0; y < fftHeight; y++)
            {
                for (int x = 0; x < fftWidth; x++)
                {
                    double value = correlation[y * fftWidth + x].Real;
                    absoluteSum += Math.Abs(value);
                    if (value > peak)
                    {
                        peak = value;
                        peakX = x;
                        peakY = y;
                    }
                }
            }

            if (peak == double.MinValue)
                throw new InvalidOperationException("没有找到有效的图像位移峰值。");

            double subpixelX = peakX + ParabolicOffset(
                GetWrapped(correlation, fftWidth, fftHeight, peakX - 1, peakY),
                peak,
                GetWrapped(correlation, fftWidth, fftHeight, peakX + 1, peakY));
            double subpixelY = peakY + ParabolicOffset(
                GetWrapped(correlation, fftWidth, fftHeight, peakX, peakY - 1),
                peak,
                GetWrapped(correlation, fftWidth, fftHeight, peakX, peakY + 1));

            if (subpixelX > fftWidth / 2.0)
                subpixelX -= fftWidth;
            if (subpixelY > fftHeight / 2.0)
                subpixelY -= fftHeight;

            double meanAbsolute = absoluteSum / (fftWidth * fftHeight);
            return new ImageTranslation
            {
                X = subpixelX * reference.SamplingStep,
                Y = subpixelY * reference.SamplingStep,
                Confidence = meanAbsolute > 1e-12 ? peak / meanAbsolute : 0
            };
        }

        /// <summary>
        /// 创建经过均值去除和汉宁窗处理的复数图像。
        /// </summary>
        private static Complex[] CreateWindowedImage(GrayFrameSnapshot frame, int fftWidth, int fftHeight)
        {
            Complex[] result = new Complex[fftWidth * fftHeight];
            FillWindowedImage(frame, fftWidth, fftHeight, result);
            return result;
        }

        /// <summary>
        /// 将灰度帧填入可复用 FFT 缓冲区并应用二维汉宁窗。
        /// </summary>
        private static void FillWindowedImage(
            GrayFrameSnapshot frame,
            int fftWidth,
            int fftHeight,
            Complex[] result)
        {
            Array.Clear(result, 0, result.Length);
            double mean = 0;
            for (int i = 0; i < frame.Pixels.Length; i++)
                mean += frame.Pixels[i];
            mean /= Math.Max(1, frame.Pixels.Length);

            for (int y = 0; y < frame.Height; y++)
            {
                double windowY = frame.Height > 1
                    ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * y / (frame.Height - 1))
                    : 1;
                for (int x = 0; x < frame.Width; x++)
                {
                    double windowX = frame.Width > 1
                        ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * x / (frame.Width - 1))
                        : 1;
                    result[y * fftWidth + x] = (frame.Pixels[y * frame.Width + x] - mean) * windowX * windowY;
                }
            }
        }

        /// <summary>
        /// 以环绕坐标读取相关平面数值。
        /// </summary>
        private static double GetWrapped(Complex[] values, int width, int height, int x, int y)
        {
            x = (x + width) % width;
            y = (y + height) % height;
            return values[y * width + x].Real;
        }

        /// <summary>
        /// 使用峰值左右样本计算限制在半像素内的亚像素偏移。
        /// </summary>
        private static double ParabolicOffset(double before, double center, double after)
        {
            double denominator = before - 2 * center + after;
            if (Math.Abs(denominator) < 1e-12)
                return 0;
            return Math.Max(-0.5, Math.Min(0.5, 0.5 * (before - after) / denominator));
        }

        /// <summary>
        /// 返回不小于输入值的最小 2 次幂。
        /// </summary>
        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result <<= 1;
            return result;
        }

        /// <summary>
        /// 分别对所有行和列执行一维 FFT，完成二维变换。
        /// </summary>
        private static void Transform2D(Complex[] values, int width, int height, bool inverse)
        {
            Complex[] row = new Complex[width];
            for (int y = 0; y < height; y++)
            {
                Array.Copy(values, y * width, row, 0, width);
                Transform(row, inverse);
                Array.Copy(row, 0, values, y * width, width);
            }

            Complex[] column = new Complex[height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                    column[y] = values[y * width + x];
                Transform(column, inverse);
                for (int y = 0; y < height; y++)
                    values[y * width + x] = column[y];
            }
        }

        /// <summary>
        /// 原地执行 radix-2 Cooley-Tukey 快速傅里叶变换。
        /// </summary>
        private static void Transform(Complex[] values, bool inverse)
        {
            int length = values.Length;
            for (int i = 1, j = 0; i < length; i++)
            {
                int bit = length >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    Complex temporary = values[i];
                    values[i] = values[j];
                    values[j] = temporary;
                }
            }

            for (int block = 2; block <= length; block <<= 1)
            {
                double angle = 2 * Math.PI / block * (inverse ? 1 : -1);
                Complex root = new Complex(Math.Cos(angle), Math.Sin(angle));
                for (int start = 0; start < length; start += block)
                {
                    Complex factor = Complex.One;
                    int half = block >> 1;
                    for (int offset = 0; offset < half; offset++)
                    {
                        Complex even = values[start + offset];
                        Complex odd = values[start + offset + half] * factor;
                        values[start + offset] = even + odd;
                        values[start + offset + half] = even - odd;
                        factor *= root;
                    }
                }
            }

            if (inverse)
            {
                for (int i = 0; i < length; i++)
                    values[i] /= length;
            }
        }
    }
}

