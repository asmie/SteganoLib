using System;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using SteganoLib.Steganalysis;
using Xunit;

namespace SteganoLib.Test
{
    public class SteganalysisTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x33 });

        /// <summary>
        /// A photo-like cover: smooth low-frequency content, mild sensor noise and a tone
        /// curve applied after quantisation, which gives the uneven histogram that real
        /// captures have and the chi-square attack relies on.
        /// </summary>
        internal static Image<Rgba32> NaturalCover(int width = 256, int height = 192, int seed = 1)
        {
            var random = new Random(seed);
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
                lut[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, 1.3));

            double Gaussian()
            {
                double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            }
            byte Tone(double v) => lut[(int)Math.Clamp(Math.Round(v), 0, 255)];

            var image = new Image<Rgba32>(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double b = 128 + 55 * Math.Sin(x / 13.0 + seed) * Math.Cos(y / 17.0) + 35 * Math.Sin((x + 2 * y) / 29.0);
                    image[x, y] = new Rgba32(
                        Tone(b + Gaussian() * 2.5),
                        Tone(0.9 * b + 10 + Gaussian() * 2.5),
                        Tone(0.8 * b + 20 + Gaussian() * 2.5),
                        255);
                }
            }
            return image;
        }

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static Algorithms.LSB RedLsb(LsbEmbeddingMode mode) => new(new KeyedPermutationSelector(Key))
        {
            Channels = ColorChannels.Red,
            EmbeddingMode = mode,
        };

        /// <summary>Embed into the red channel at <paramref name="fraction"/> of its capacity.</summary>
        private static Image<Rgba32> Stego(Image<Rgba32> cover, LsbEmbeddingMode mode, double fraction)
        {
            var image = cover.Clone();
            var lsb = RedLsb(mode);
            int length = (int)(lsb.Capacity(image) * fraction);
            lsb.EmbedBytes(Random(length, length), image);
            return image;
        }

        /// <summary>Overwrite the red LSB of the first <paramref name="fraction"/> of pixels, in row order, with random bits.</summary>
        private static Image<Rgba32> SequentialReplace(Image<Rgba32> cover, double fraction, int seed = 5)
        {
            var image = cover.Clone();
            var random = new Random(seed);
            int limit = (int)(image.Width * image.Height * fraction);
            for (int i = 0; i < limit; i++)
            {
                int x = i % image.Width, y = i / image.Width;
                var p = image[x, y];
                p.R = (byte)((p.R & 0xFE) | random.Next(2));
                image[x, y] = p;
            }
            return image;
        }

        // ---------- special functions ----------

        [Fact]
        public void LogGamma_MatchesFactorials()
        {
            Assert.Equal(Math.Log(24), SpecialFunctions.LogGamma(5), 10);
            Assert.Equal(Math.Log(3628800), SpecialFunctions.LogGamma(11), 8);
            Assert.Equal(0.5 * Math.Log(Math.PI), SpecialFunctions.LogGamma(0.5), 10);
        }

        [Theory]
        [InlineData(3.841, 1, 0.95)]
        [InlineData(18.307, 10, 0.95)]
        [InlineData(124.342, 100, 0.95)]
        [InlineData(0.0158, 1, 0.10)]
        [InlineData(140.169, 100, 0.995)]
        public void ChiSquareCdf_MatchesTables(double statistic, int df, double expected)
        {
            Assert.Equal(expected, SpecialFunctions.ChiSquareCdf(statistic, df), 3);
        }

        // ---------- chi-square ----------

        [Fact]
        public void ChiSquare_FromHistogram_Extremes()
        {
            var even = new long[256];
            for (int k = 0; k < 128; k++) { even[2 * k] = 100; even[2 * k + 1] = 100; }
            var equal = ChiSquareAttack.FromHistogram(even);
            Assert.Equal(0, equal.Statistic);
            Assert.Equal(127, equal.DegreesOfFreedom);
            Assert.Equal(1.0, equal.EmbeddingProbability, 6);
            Assert.Equal(25600, equal.Samples);

            var skewed = new long[256];
            for (int k = 0; k < 128; k++) { skewed[2 * k] = 150; skewed[2 * k + 1] = 50; }
            Assert.Equal(0.0, ChiSquareAttack.FromHistogram(skewed).EmbeddingProbability, 9);

            var sparse = new long[256];
            sparse[10] = 3; sparse[11] = 2;
            var tiny = ChiSquareAttack.FromHistogram(sparse);
            Assert.Equal(0, tiny.DegreesOfFreedom);
            Assert.Equal(0, tiny.EmbeddingProbability);

            Assert.Throws<ArgumentException>(() => ChiSquareAttack.FromHistogram(new long[255]));
        }

        [Fact]
        public void ChiSquare_DetectsReplacement_NotMatching()
        {
            using var cover = NaturalCover();
            using var replaced = Stego(cover, LsbEmbeddingMode.Replace, 1.0);
            using var matched = Stego(cover, LsbEmbeddingMode.Match, 1.0);
            var attack = new ChiSquareAttack { Channels = ColorChannels.Red };

            double clean = attack.Analyze(cover).EmbeddingProbability;
            double replace = attack.Analyze(replaced).EmbeddingProbability;
            double match = attack.Analyze(matched).EmbeddingProbability;

            Assert.True(clean < 0.05, $"clean cover scored {clean}");
            Assert.True(replace > 0.95, $"full LSB replacement scored {replace}");
            Assert.True(match < 0.05, $"LSB matching scored {match}");
        }

        [Fact]
        public void ChiSquare_Profile_ShowsWhereSequentialMessageEnds()
        {
            using var cover = NaturalCover();
            using var stego = SequentialReplace(cover, 0.4);
            var attack = new ChiSquareAttack { Channels = ColorChannels.Red };

            var profile = attack.Profile(stego, 10);

            Assert.Equal(10, profile.Count);
            for (int i = 0; i < 4; i++)
                Assert.True(profile[i].EmbeddingProbability > 0.9, $"step {i + 1}: {profile[i].EmbeddingProbability}");
            Assert.True(profile[9].EmbeddingProbability < 0.05, $"whole image: {profile[9].EmbeddingProbability}");
            Assert.True(attack.Profile(cover, 10).All(r => r.EmbeddingProbability < 0.05));
        }

        [Fact]
        public void ChiSquare_ChannelSelection()
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Replace, 1.0);

            Assert.True(new ChiSquareAttack { Channels = ColorChannels.Red }.Analyze(stego).EmbeddingProbability > 0.95);
            Assert.True(new ChiSquareAttack { Channels = ColorChannels.Green | ColorChannels.Blue }.Analyze(stego).EmbeddingProbability < 0.05);
            Assert.Throws<ArgumentException>(() => new ChiSquareAttack { Channels = ColorChannels.None });
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChiSquareAttack { MinimumExpected = 0 });
        }

        // ---------- RS ----------

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.3)]
        [InlineData(0.6)]
        public void Rs_EstimatesReplacementRate(double rate)
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Replace, rate);

            var result = new RsAnalysis { Channels = ColorChannels.Red }.Analyze(stego);

            Assert.InRange(result.EstimatedEmbeddingRate, rate - 0.12, rate + 0.12);
            Assert.True(result.Groups > 10000);
            Assert.InRange(result.RegularPositive + result.SingularPositive, 0.5, 1.0);
        }

        [Fact]
        public void Rs_MatchingStaysLow()
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Match, 1.0);

            double estimate = new RsAnalysis { Channels = ColorChannels.Red }.Analyze(stego).EstimatedEmbeddingRate;

            Assert.True(Math.Abs(estimate) < 0.25, $"LSB matching estimated at {estimate}");
        }

        [Fact]
        public void Rs_AveragesChannels_AndValidates()
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Replace, 0.6);
            var rs = new RsAnalysis();

            var all = rs.Analyze(stego);
            double mean = new[] { ColorChannels.Red, ColorChannels.Green, ColorChannels.Blue }
                .Average(c => rs.AnalyzeChannel(stego, c).EstimatedEmbeddingRate);
            Assert.Equal(mean, all.EstimatedEmbeddingRate, 9);
            Assert.InRange(all.EstimatedEmbeddingRate, 0.1, 0.3); // one of three channels carries 60 percent

            Assert.Equal(new[] { 0, 1, 1, 0 }, rs.Mask);
            rs.Mask = new[] { 1, 0, 0, 1 };
            Assert.InRange(rs.AnalyzeChannel(stego, ColorChannels.Red).EstimatedEmbeddingRate, 0.48, 0.72);
            Assert.Throws<ArgumentException>(() => rs.Mask = new[] { 0, 0 });
            Assert.Throws<ArgumentException>(() => rs.Mask = new[] { 1, 2 });
            Assert.Throws<ArgumentException>(() => rs.Mask = new[] { 1 });
            Assert.Throws<ArgumentException>(() => rs.AnalyzePlane(new byte[10], 3, 3));
            Assert.Throws<ArgumentException>(() => rs.Channels = ColorChannels.None);
        }

        [Fact]
        public void Rs_SolveRate_KnownPoints()
        {
            // A clean image has d0 = dn0 and d1 = dn1, and the smaller root is x = 0.
            Assert.Equal(0, RsAnalysis.SolveRate(0.3, -0.3, 0.3, -0.3), 9);
            // Degenerate with no curvature falls back to the linear solution.
            Assert.Equal(0, RsAnalysis.SolveRate(0, 0, 0, 0));
        }

        // ---------- sample pairs ----------

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void SamplePairs_EstimatesReplacementRate(double rate)
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Replace, rate);

            var result = new SamplePairAnalysis { Channels = ColorChannels.Red }.Analyze(stego);

            Assert.InRange(result.EstimatedEmbeddingRate, rate - 0.12, rate + 0.12);
            Assert.Equal(2L * 256 * 192 - 256 - 192, result.Pairs);
            Assert.Equal(result.Pairs, result.X + result.Y + result.Z + (result.Pairs - result.X - result.Y - result.Z));
        }

        [Fact]
        public void SamplePairs_MatchingStaysLow()
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Match, 1.0);

            double estimate = new SamplePairAnalysis { Channels = ColorChannels.Red }.Analyze(stego).EstimatedEmbeddingRate;

            Assert.True(Math.Abs(estimate) < 0.25, $"LSB matching estimated at {estimate}");
        }

        [Fact]
        public void SamplePairs_SolveRate_KnownPoints()
        {
            Assert.Equal(0, SamplePairAnalysis.SolveRate(1000, 400, 400, 100, 100), 9);
            Assert.True(double.IsNaN(SamplePairAnalysis.SolveRate(0, 0, 0, 0, 0)));
            Assert.Throws<ArgumentException>(() => new SamplePairAnalysis().AnalyzePlane(new byte[10], 3, 3));
            Assert.Throws<ArgumentNullException>(() => new SamplePairAnalysis().Analyze(null));
        }

        [Fact]
        public void Rs_UndefinedAtSaturation_SamplePairsStillWorks()
        {
            using var cover = NaturalCover();
            using var stego = Stego(cover, LsbEmbeddingMode.Replace, 1.0);

            double rs = new RsAnalysis { Channels = ColorChannels.Red }.Analyze(stego).EstimatedEmbeddingRate;
            double spa = new SamplePairAnalysis { Channels = ColorChannels.Red }.Analyze(stego).EstimatedEmbeddingRate;

            Assert.True(double.IsNaN(rs) || rs > 0.7, $"RS at saturation: {rs}");
            Assert.InRange(spa, 0.88, 1.12);
        }

        // ---------- as a test oracle for the library's own algorithms ----------

        [Fact]
        public void Oracle_DefaultLsbIsInvisibleToAllThreeDetectors()
        {
            using var cover = NaturalCover(256, 192, 7);
            using var stego = cover.Clone();
            var lsb = new Algorithms.LSB(new KeyedPermutationSelector(Key)); // default: matching, all channels
            lsb.EmbedBytes(Random((int)lsb.Capacity(stego), 3), stego);

            Assert.True(new ChiSquareAttack().Analyze(stego).EmbeddingProbability < 0.05);
            Assert.True(Math.Abs(new RsAnalysis().Analyze(stego).EstimatedEmbeddingRate) < 0.25);
            Assert.True(Math.Abs(new SamplePairAnalysis().Analyze(stego).EstimatedEmbeddingRate) < 0.25);
        }
    }
}
