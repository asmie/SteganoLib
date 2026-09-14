using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;

namespace SteganoLib.Steganalysis
{
    /// <summary>Outcome of RS analysis on one plane, or averaged over planes.</summary>
    public sealed class RsResult
    {
        internal RsResult(double estimatedEmbeddingRate, double regularPositive, double singularPositive, double regularNegative, double singularNegative, long groups)
        {
            EstimatedEmbeddingRate = estimatedEmbeddingRate;
            RegularPositive = regularPositive;
            SingularPositive = singularPositive;
            RegularNegative = regularNegative;
            SingularNegative = singularNegative;
            Groups = groups;
        }

        /// <summary>
        /// Estimated fraction of pixels whose least significant bit carries a message bit.
        /// About 0 for a clean image and reliable up to roughly 0.7. Noise makes small
        /// negative values possible. The estimator is undefined at full embedding, where
        /// both measurement points coincide, and returns <see cref="double.NaN"/> or a
        /// wild value there; use sample pair analysis to confirm a saturated image.
        /// </summary>
        public double EstimatedEmbeddingRate { get; }

        /// <summary>Fraction of groups that get noisier when the mask flips them.</summary>
        public double RegularPositive { get; }

        /// <summary>Fraction of groups that get smoother when the mask flips them.</summary>
        public double SingularPositive { get; }

        public double RegularNegative { get; }

        public double SingularNegative { get; }

        public long Groups { get; }
    }

    /// <summary>
    /// Fridrich, Goljan and Du's RS analysis. Pixel groups are classified as regular or
    /// singular by whether flipping least significant bits under a mask raises or lowers
    /// their local noise. In a clean image the counts for the mask and its negative agree;
    /// LSB replacement drives them apart in a predictable way, which yields an estimate of
    /// the embedding rate, reliable up to about 70 percent of the pixels. Detects LSB
    /// replacement; LSB matching leaves it near zero.
    /// </summary>
    public sealed class RsAnalysis
    {
        private ColorChannels _channels = ColorChannels.All;
        private int[] _mask = { 0, 1, 1, 0 };

        /// <summary>Channels analysed; the estimates are averaged. Default all three.</summary>
        public ColorChannels Channels
        {
            get => _channels;
            set
            {
                if ((value & ColorChannels.All) == ColorChannels.None)
                    throw new ArgumentException("At least one channel is required.", nameof(value));
                _channels = value & ColorChannels.All;
            }
        }

        /// <summary>Flip mask over consecutive horizontal pixels, entries 0 or 1. Default {0, 1, 1, 0}.</summary>
        public int[] Mask
        {
            get => (int[])_mask.Clone();
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (value.Length < 2) throw new ArgumentException("Mask needs at least two entries.", nameof(value));
                bool any = false;
                foreach (int m in value)
                {
                    if (m != 0 && m != 1) throw new ArgumentException("Mask entries must be 0 or 1.", nameof(value));
                    any |= m == 1;
                }
                if (!any) throw new ArgumentException("Mask must flip at least one pixel.", nameof(value));
                _mask = (int[])value.Clone();
            }
        }

        /// <summary>Analyse every selected channel and average the results.</summary>
        public RsResult Analyze(Image<Rgba32> image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var channels = ChannelPlane.Split(_channels);
            double rate = 0, rp = 0, sp = 0, rn = 0, sn = 0;
            long groups = 0;
            foreach (var channel in channels)
            {
                var r = AnalyzeChannel(image, channel);
                rate += r.EstimatedEmbeddingRate;
                rp += r.RegularPositive;
                sp += r.SingularPositive;
                rn += r.RegularNegative;
                sn += r.SingularNegative;
                groups += r.Groups;
            }
            int n = channels.Count;
            return new RsResult(rate / n, rp / n, sp / n, rn / n, sn / n, groups);
        }

        public RsResult AnalyzeChannel(Image<Rgba32> image, ColorChannels channel)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            return AnalyzePlane(ChannelPlane.Extract(image, channel), image.Width, image.Height);
        }

        /// <summary>RS analysis of a row-major plane of <paramref name="width"/> by <paramref name="height"/> values.</summary>
        public RsResult AnalyzePlane(byte[] plane, int width, int height)
        {
            if (plane == null) throw new ArgumentNullException(nameof(plane));
            if (width < 1 || height < 1 || plane.Length != width * height)
                throw new ArgumentException("Plane size does not match the dimensions.", nameof(plane));

            var (rp, sp, rn, sn, groups) = Classify(plane, width, height, flipAll: false);
            var (rp1, sp1, rn1, sn1, _) = Classify(plane, width, height, flipAll: true);

            if (groups == 0)
                return new RsResult(double.NaN, 0, 0, 0, 0, 0);

            double d0 = rp - sp, d1 = rp1 - sp1, dn0 = rn - sn, dn1 = rn1 - sn1;
            double rate = SolveRate(d0, d1, dn0, dn1);
            return new RsResult(rate, rp, sp, rn, sn, groups);
        }

        /// <summary>Solves the RS quadratic for the point x = p / 2 and returns p.</summary>
        internal static double SolveRate(double d0, double d1, double dn0, double dn1)
        {
            double a = 2 * (d1 + d0);
            double b = dn0 - dn1 - d1 - 3 * d0;
            double c = d0 - dn0;

            double x;
            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) < 1e-12)
                    return 0;
                x = -c / b;
            }
            else
            {
                double discriminant = b * b - 4 * a * c;
                if (discriminant < 0)
                {
                    if (-discriminant > 0.05 * b * b)
                        return double.NaN;
                    discriminant = 0; // noise around a double root
                }
                double root = Math.Sqrt(discriminant);
                double x1 = (-b + root) / (2 * a), x2 = (-b - root) / (2 * a);
                x = Math.Abs(x1) <= Math.Abs(x2) ? x1 : x2;
            }

            return x / (x - 0.5);
        }

        private (double Rp, double Sp, double Rn, double Sn, long Groups) Classify(byte[] plane, int width, int height, bool flipAll)
        {
            int n = _mask.Length;
            long regularPositive = 0, singularPositive = 0, regularNegative = 0, singularNegative = 0, groups = 0;
            var group = new int[n];
            var flipped = new int[n];

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x + n <= width; x += n)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int v = plane[row + x + i];
                        group[i] = flipAll ? v ^ 1 : v;
                    }
                    int baseline = Noise(group);

                    for (int i = 0; i < n; i++)
                        flipped[i] = _mask[i] == 1 ? group[i] ^ 1 : group[i];
                    int positive = Noise(flipped);

                    for (int i = 0; i < n; i++)
                        flipped[i] = _mask[i] == 1 ? FlipNegative(group[i]) : group[i];
                    int negative = Noise(flipped);

                    if (positive > baseline) regularPositive++;
                    else if (positive < baseline) singularPositive++;
                    if (negative > baseline) regularNegative++;
                    else if (negative < baseline) singularNegative++;
                    groups++;
                }
            }

            if (groups == 0)
                return (0, 0, 0, 0, 0);
            return ((double)regularPositive / groups, (double)singularPositive / groups, (double)regularNegative / groups, (double)singularNegative / groups, groups);
        }

        /// <summary>F-1: swaps 2k-1 and 2k, so 0 goes to -1 and 255 to 256; the noise function only needs differences.</summary>
        private static int FlipNegative(int v) => ((v + 1) ^ 1) - 1;

        private static int Noise(int[] group)
        {
            int sum = 0;
            for (int i = 1; i < group.Length; i++)
                sum += Math.Abs(group[i] - group[i - 1]);
            return sum;
        }
    }
}
