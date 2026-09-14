using System;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;

namespace SteganoLib.Steganalysis
{
    /// <summary>Outcome of sample pair analysis on one plane, or averaged over planes.</summary>
    public sealed class SamplePairResult
    {
        internal SamplePairResult(double estimatedEmbeddingRate, long pairs, long x, long y, long w, long z)
        {
            EstimatedEmbeddingRate = estimatedEmbeddingRate;
            Pairs = pairs;
            X = x;
            Y = y;
            W = w;
            Z = z;
        }

        /// <summary>
        /// Estimated fraction of pixels whose least significant bit carries a message bit.
        /// About 0 for a clean image, about 1 when every pixel is used; <see cref="double.NaN"/>
        /// when the estimator has no solution.
        /// </summary>
        public double EstimatedEmbeddingRate { get; }

        public long Pairs { get; }

        /// <summary>Pairs where the second value is even and larger, or odd and smaller, than the first.</summary>
        public long X { get; }

        /// <summary>Pairs where the second value is even and smaller, or odd and larger, than the first.</summary>
        public long Y { get; }

        /// <summary>Pairs that share a value pair (2k, 2k+1) but differ.</summary>
        public long W { get; }

        /// <summary>Pairs of equal values.</summary>
        public long Z { get; }
    }

    /// <summary>
    /// Dumitrescu, Wu and Wang's sample pair analysis. Adjacent pixel pairs are sorted
    /// into trace multisets by the parity and order of their values; LSB replacement
    /// moves pairs between the sets in a way that depends only on the embedding rate,
    /// which a quadratic recovers. Detects LSB replacement; LSB matching leaves it near zero.
    /// </summary>
    public sealed class SamplePairAnalysis
    {
        private ColorChannels _channels = ColorChannels.All;

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

        /// <summary>Analyse every selected channel and average the results.</summary>
        public SamplePairResult Analyze(Image<Rgba32> image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var channels = ChannelPlane.Split(_channels);
            double rate = 0;
            long pairs = 0, x = 0, y = 0, w = 0, z = 0;
            foreach (var channel in channels)
            {
                var r = AnalyzeChannel(image, channel);
                rate += r.EstimatedEmbeddingRate;
                pairs += r.Pairs;
                x += r.X;
                y += r.Y;
                w += r.W;
                z += r.Z;
            }
            return new SamplePairResult(rate / channels.Count, pairs, x, y, w, z);
        }

        public SamplePairResult AnalyzeChannel(Image<Rgba32> image, ColorChannels channel)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            return AnalyzePlane(ChannelPlane.Extract(image, channel), image.Width, image.Height);
        }

        /// <summary>Sample pair analysis over horizontal and vertical neighbours of a row-major plane.</summary>
        public SamplePairResult AnalyzePlane(byte[] plane, int width, int height)
        {
            if (plane == null) throw new ArgumentNullException(nameof(plane));
            if (width < 1 || height < 1 || plane.Length != width * height)
                throw new ArgumentException("Plane size does not match the dimensions.", nameof(plane));

            long pairs = 0, x = 0, y = 0, w = 0, z = 0;

            void Count(int u, int v)
            {
                pairs++;
                if (u == v)
                    z++;
                else if ((u >> 1) == (v >> 1))
                    w++;

                bool vEven = (v & 1) == 0;
                if ((vEven && u < v) || (!vEven && u > v))
                    x++;
                else if ((vEven && u > v) || (!vEven && u < v))
                    y++;
            }

            for (int row = 0; row < height; row++)
            {
                int offset = row * width;
                for (int col = 0; col + 1 < width; col++)
                    Count(plane[offset + col], plane[offset + col + 1]);
                if (row + 1 < height)
                {
                    for (int col = 0; col < width; col++)
                        Count(plane[offset + col], plane[offset + width + col]);
                }
            }

            return new SamplePairResult(SolveRate(pairs, x, y, w, z), pairs, x, y, w, z);
        }

        /// <summary>Solves 0.5 (W + Z) p^2 + (2X - P) p + (Y - X) = 0 for the embedding rate p.</summary>
        internal static double SolveRate(long pairs, long x, long y, long w, long z)
        {
            if (pairs == 0)
                return double.NaN;

            double a = 0.5 * (w + z);
            double b = 2.0 * x - pairs;
            double c = (double)y - x;

            if (Math.Abs(a) < 1e-12)
                return Math.Abs(b) < 1e-12 ? 0 : -c / b;

            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0)
            {
                // At full embedding the two roots coincide at 1, so sampling noise can push
                // the discriminant just below zero; treat that as the double root.
                if (-discriminant <= 0.05 * b * b)
                    return -b / (2 * a);
                return double.NaN;
            }

            double root = Math.Sqrt(discriminant);
            double p1 = (-b + root) / (2 * a), p2 = (-b - root) / (2 * a);
            return Math.Abs(p1) <= Math.Abs(p2) ? p1 : p2;
        }
    }
}
