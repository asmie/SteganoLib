using System;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Quality
{
    /// <summary>How far a stego image is from its cover.</summary>
    public sealed class ImageComparison
    {
        internal ImageComparison(double mse, double psnr, double ssim, long changedSamples, long totalSamples, int maxAbsoluteError)
        {
            Mse = mse;
            Psnr = psnr;
            Ssim = ssim;
            ChangedSamples = changedSamples;
            TotalSamples = totalSamples;
            MaxAbsoluteError = maxAbsoluteError;
        }

        /// <summary>Mean squared error over the R, G and B samples.</summary>
        public double Mse { get; }

        /// <summary>Peak signal-to-noise ratio in dB; infinite for identical images. Above 40 dB is generally invisible.</summary>
        public double Psnr { get; }

        /// <summary>Structural similarity of the luminance, 1 for identical images.</summary>
        public double Ssim { get; }

        /// <summary>R, G or B values that differ.</summary>
        public long ChangedSamples { get; }

        /// <summary>Three per pixel.</summary>
        public long TotalSamples { get; }

        public double ChangeRate => TotalSamples == 0 ? 0 : (double)ChangedSamples / TotalSamples;

        public int MaxAbsoluteError { get; }
    }

    /// <summary>Distortion measures between two images of the same size. Alpha is ignored.</summary>
    public static class ImageMetrics
    {
        private const double Peak = 255.0;

        public static ImageComparison Compare(Image<Rgba32> cover, Image<Rgba32> stego)
        {
            RequireSameSize(cover, stego);

            long changed = 0, total = 3L * cover.Width * cover.Height;
            double squared = 0;
            int maxError = 0;
            var lumaA = new double[cover.Width * cover.Height];
            var lumaB = new double[cover.Width * cover.Height];

            for (int y = 0; y < cover.Height; y++)
            {
                for (int x = 0; x < cover.Width; x++)
                {
                    var a = cover[x, y];
                    var b = stego[x, y];
                    Accumulate(a.R, b.R, ref changed, ref squared, ref maxError);
                    Accumulate(a.G, b.G, ref changed, ref squared, ref maxError);
                    Accumulate(a.B, b.B, ref changed, ref squared, ref maxError);
                    lumaA[y * cover.Width + x] = Luma(a);
                    lumaB[y * cover.Width + x] = Luma(b);
                }
            }

            double mse = total == 0 ? 0 : squared / total;
            return new ImageComparison(mse, PsnrFromMse(mse), Ssim(lumaA, lumaB, cover.Width, cover.Height), changed, total, maxError);
        }

        public static double Mse(Image<Rgba32> cover, Image<Rgba32> stego) => Compare(cover, stego).Mse;

        public static double Psnr(Image<Rgba32> cover, Image<Rgba32> stego) => Compare(cover, stego).Psnr;

        public static double Ssim(Image<Rgba32> cover, Image<Rgba32> stego) => Compare(cover, stego).Ssim;

        /// <summary>PSNR in dB for a mean squared error on an 8-bit scale.</summary>
        public static double PsnrFromMse(double mse)
        {
            if (mse < 0) throw new ArgumentOutOfRangeException(nameof(mse));
            return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(Peak * Peak / mse);
        }

        /// <summary>
        /// Mean SSIM (Wang et al.) over a row-major plane with an 11x11 Gaussian window of
        /// sigma 1.5, as in the reference implementation. Planes smaller than the window
        /// are compared as one window.
        /// </summary>
        public static double Ssim(double[] a, double[] b, int width, int height)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (width < 1 || height < 1 || a.Length != width * height || b.Length != width * height)
                throw new ArgumentException("Plane sizes do not match the dimensions.");

            const double k1 = 0.01, k2 = 0.03;
            double c1 = k1 * Peak * k1 * Peak, c2 = k2 * Peak * k2 * Peak;

            var aa = new double[a.Length];
            var bb = new double[a.Length];
            var ab = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                aa[i] = a[i] * a[i];
                bb[i] = b[i] * b[i];
                ab[i] = a[i] * b[i];
            }

            const int window = 11;
            double[] muA, muB, eAA, eBB, eAB;
            if (width < window || height < window)
            {
                muA = new[] { Mean(a) };
                muB = new[] { Mean(b) };
                eAA = new[] { Mean(aa) };
                eBB = new[] { Mean(bb) };
                eAB = new[] { Mean(ab) };
            }
            else
            {
                var kernel = GaussianKernel(window, 1.5);
                muA = Filter(a, width, height, kernel);
                muB = Filter(b, width, height, kernel);
                eAA = Filter(aa, width, height, kernel);
                eBB = Filter(bb, width, height, kernel);
                eAB = Filter(ab, width, height, kernel);
            }

            double sum = 0;
            for (int i = 0; i < muA.Length; i++)
            {
                double varA = eAA[i] - muA[i] * muA[i];
                double varB = eBB[i] - muB[i] * muB[i];
                double cov = eAB[i] - muA[i] * muB[i];
                sum += (2 * muA[i] * muB[i] + c1) * (2 * cov + c2) / ((muA[i] * muA[i] + muB[i] * muB[i] + c1) * (varA + varB + c2));
            }
            return sum / muA.Length;
        }

        private static void Accumulate(byte a, byte b, ref long changed, ref double squared, ref int maxError)
        {
            int diff = a - b;
            if (diff == 0)
                return;
            changed++;
            squared += (double)diff * diff;
            maxError = Math.Max(maxError, Math.Abs(diff));
        }

        private static double Luma(Rgba32 p) => 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;

        private static double Mean(double[] values)
        {
            double sum = 0;
            foreach (double v in values) sum += v;
            return sum / values.Length;
        }

        private static double[] GaussianKernel(int size, double sigma)
        {
            var kernel = new double[size];
            int radius = size / 2;
            double total = 0;
            for (int i = 0; i < size; i++)
            {
                double d = i - radius;
                kernel[i] = Math.Exp(-d * d / (2 * sigma * sigma));
                total += kernel[i];
            }
            for (int i = 0; i < size; i++)
                kernel[i] /= total;
            return kernel;
        }

        /// <summary>Separable convolution keeping only positions where the window lies fully inside the plane.</summary>
        private static double[] Filter(double[] plane, int width, int height, double[] kernel)
        {
            int k = kernel.Length;
            int outWidth = width - k + 1, outHeight = height - k + 1;

            var horizontal = new double[outWidth * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    double sum = 0;
                    for (int i = 0; i < k; i++)
                        sum += kernel[i] * plane[y * width + x + i];
                    horizontal[y * outWidth + x] = sum;
                }
            }

            var output = new double[outWidth * outHeight];
            for (int y = 0; y < outHeight; y++)
            {
                for (int x = 0; x < outWidth; x++)
                {
                    double sum = 0;
                    for (int i = 0; i < k; i++)
                        sum += kernel[i] * horizontal[(y + i) * outWidth + x];
                    output[y * outWidth + x] = sum;
                }
            }
            return output;
        }

        private static void RequireSameSize(Image<Rgba32> cover, Image<Rgba32> stego)
        {
            if (cover == null) throw new ArgumentNullException(nameof(cover));
            if (stego == null) throw new ArgumentNullException(nameof(stego));
            if (cover.Width != stego.Width || cover.Height != stego.Height)
                throw new ArgumentException($"Images differ in size: {cover.Width}x{cover.Height} versus {stego.Width}x{stego.Height}.", nameof(stego));
        }
    }
}
