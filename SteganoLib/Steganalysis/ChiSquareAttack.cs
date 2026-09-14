using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;

namespace SteganoLib.Steganalysis
{
    /// <summary>Outcome of a chi-square test on one histogram.</summary>
    public sealed class ChiSquareResult
    {
        internal ChiSquareResult(double statistic, int degreesOfFreedom, double embeddingProbability, long samples)
        {
            Statistic = statistic;
            DegreesOfFreedom = degreesOfFreedom;
            EmbeddingProbability = embeddingProbability;
            Samples = samples;
        }

        public double Statistic { get; }

        public int DegreesOfFreedom { get; }

        /// <summary>
        /// Probability that the pairs of values are as even as LSB replacement would make
        /// them: near 1 means "embedded", near 0 means the histogram still has its natural
        /// unevenness. Meaningless when <see cref="DegreesOfFreedom"/> is 0.
        /// </summary>
        public double EmbeddingProbability { get; }

        /// <summary>Values that went into the histogram.</summary>
        public long Samples { get; }
    }

    /// <summary>
    /// Westfeld and Pfitzmann's chi-square attack. Overwriting least significant bits
    /// with message bits makes the two values of each pair (2k, 2k+1) equally frequent;
    /// the test measures how close the histogram is to that state. Detects sequential
    /// and dense LSB replacement, not LSB matching or sparse embedding.
    /// </summary>
    public sealed class ChiSquareAttack
    {
        private ColorChannels _channels = ColorChannels.All;
        private int _minimumExpected = 4;

        /// <summary>Channels whose values are pooled into one histogram. Default all three.</summary>
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

        /// <summary>Pairs whose expected frequency is below this are left out of the statistic. Default 4.</summary>
        public int MinimumExpected
        {
            get => _minimumExpected;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _minimumExpected = value;
            }
        }

        /// <summary>Test the whole image.</summary>
        public ChiSquareResult Analyze(Image<Rgba32> image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            var histogram = new long[256];
            foreach (var channel in ChannelPlane.Split(_channels))
            {
                foreach (byte v in ChannelPlane.Extract(image, channel))
                    histogram[v]++;
            }
            return FromHistogram(histogram, _minimumExpected);
        }

        /// <summary>
        /// Test growing prefixes of the image in row order: entry i covers the first
        /// (i + 1) / <paramref name="steps"/> of the pixels. A sequential embedding shows
        /// as a probability near 1 that drops to 0 where the message ends.
        /// </summary>
        public IReadOnlyList<ChiSquareResult> Profile(Image<Rgba32> image, int steps = 10)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (steps < 1) throw new ArgumentOutOfRangeException(nameof(steps));

            var planes = new List<byte[]>();
            foreach (var channel in ChannelPlane.Split(_channels))
                planes.Add(ChannelPlane.Extract(image, channel));

            int pixels = image.Width * image.Height;
            var results = new List<ChiSquareResult>(steps);
            var histogram = new long[256];
            int done = 0;
            for (int step = 1; step <= steps; step++)
            {
                int upTo = (int)((long)pixels * step / steps);
                for (; done < upTo; done++)
                {
                    foreach (var plane in planes)
                        histogram[plane[done]]++;
                }
                results.Add(FromHistogram(histogram, _minimumExpected));
            }
            return results;
        }

        /// <summary>Chi-square over value pairs (2k, 2k+1) of a 256-bin histogram.</summary>
        public static ChiSquareResult FromHistogram(long[] histogram, int minimumExpected = 4)
        {
            if (histogram == null) throw new ArgumentNullException(nameof(histogram));
            if (histogram.Length != 256) throw new ArgumentException("A 256-bin histogram is required.", nameof(histogram));
            if (minimumExpected < 1) throw new ArgumentOutOfRangeException(nameof(minimumExpected));

            double statistic = 0;
            int categories = 0;
            long samples = 0;
            for (int k = 0; k < 128; k++)
            {
                long even = histogram[2 * k], odd = histogram[2 * k + 1];
                samples += even + odd;
                double expected = (even + odd) / 2.0;
                if (expected < minimumExpected)
                    continue;

                double diff = even - expected;
                statistic += diff * diff / expected;
                categories++;
            }

            int degreesOfFreedom = Math.Max(0, categories - 1);
            double probability = degreesOfFreedom == 0 ? 0 : 1 - SpecialFunctions.ChiSquareCdf(statistic, degreesOfFreedom);
            return new ChiSquareResult(statistic, degreesOfFreedom, probability, samples);
        }
    }
}
