using System;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class TrellisConstraintTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 31, 47 });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Lsb_InfeasiblePayloadLeavesHeaderAndCarrierUnchanged(bool audioCarrier)
        {
            var data = new byte[] { 0xFF };
            if (audioCarrier)
            {
                var audio = new PcmAudio(8000, 1, 16, new int[128]);
                var original = (int[])audio.Samples.Clone();
                var algorithm = new AudioLsb(new KeyedSampleSelector(Key))
                {
                    TrellisCoder = new SyndromeTrellisCoder(3),
                    CostModel = new WetSamples(),
                };
                Assert.True(((IStegAlgorithm<PcmAudio>)algorithm).IsPossibleToEmbed(data.Length, audio));
                Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(data, audio));
                Assert.Equal(original, audio.Samples);
            }
            else
            {
                using var image = new Image<Rgba32>(128, 1);
                var original = new byte[128 * 4];
                image.CopyPixelDataTo(original);
                var algorithm = new LSB(new KeyedPermutationSelector(Key))
                {
                    TrellisCoder = new SyndromeTrellisCoder(3),
                    CostModel = new WetPixels(),
                };
                Assert.True(((IStegAlgorithm<Image<Rgba32>>)algorithm).IsPossibleToEmbed(data.Length, image));
                Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(data, image));
                var after = new byte[original.Length];
                image.CopyPixelDataTo(after);
                Assert.Equal(original, after);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(-2)]
        public void F5_NoMagnitudeOnes_CanUseEntireBudgetWithTrellis(short coefficient)
        {
            var image = Coefficients(coefficient);
            var algorithm = new F5(Key) { TrellisCoder = new SyndromeTrellisCoder(3) };
            var data = new byte[algorithm.Capacity(image)];
            Assert.NotEmpty(data);
            new Random(8).NextBytes(data);
            algorithm.EmbedBytes(data, image);
            Assert.Equal(data, new F5(Key).ExtractBytes(JpegImage.Load(image.ToArray())));
        }

        [Fact]
        public void F5_InfeasiblePayloadDoesNotCommitHeaderChanges()
        {
            var image = Coefficients(2);
            var original = image.Components.Select(c => (short[])c.Coefficients.Clone()).ToArray();
            var algorithm = new F5(Key)
            {
                TrellisCoder = new SyndromeTrellisCoder(3),
                CostModel = new ConstantCoefficients(double.PositiveInfinity),
            };
            var data = new byte[] { 0xFF };
            Assert.True(((IStegAlgorithm<JpegImage>)algorithm).IsPossibleToEmbed(data.Length, image));
            var error = Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(data, image));
            Assert.True(error.Required <= error.Available);
            Assert.Contains("constraints", error.Message);
            for (int c = 0; c < original.Length; c++)
                Assert.Equal(original[c], image.Components[c].Coefficients);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void F5_CustomFiniteCostsCannotAllowPayloadShrinkage(short one)
        {
            var image = Coefficients(2, size: 64);
            foreach (var component in image.Components)
                for (int i = 1; i < component.Coefficients.Length; i++)
                    if (i % 64 != 0 && i % 3 == 0)
                        component.Coefficients[i] = one;
            var algorithm = new F5(Key)
            {
                TrellisCoder = new SyndromeTrellisCoder(3),
                CostModel = new ConstantCoefficients(0),
            };
            var data = new byte[20];
            new Random(9).NextBytes(data);
            algorithm.EmbedBytes(data, image);
            Assert.Equal(data, new F5(Key).ExtractBytes(JpegImage.Load(image.ToArray())));
        }

        [Fact]
        public void SmallTrellis_MatchesExhaustiveMinimumWithForbiddenChanges()
        {
            // Height 2 and width 2 have columns [3, 3]. Each message bit is the
            // parity of its pair XOR the previous pair's parity (initially zero).
            var coder = new SyndromeTrellisCoder(2);
            double[] costs = { 0, double.PositiveInfinity, 2, 1, double.PositiveInfinity, 3 };
            for (int coverWord = 0; coverWord < 64; coverWord++)
            {
                var cover = Bits(coverWord, 6);
                for (int messageWord = 0; messageWord < 8; messageWord++)
                {
                    double expected = double.PositiveInfinity;
                    for (int candidate = 0; candidate < 64; candidate++)
                    {
                        if (Syndrome(candidate) != messageWord)
                            continue;
                        double cost = 0;
                        for (int bit = 0; bit < 6; bit++)
                            if (((candidate ^ coverWord) & (1 << bit)) != 0)
                                cost += costs[bit];
                        expected = Math.Min(expected, cost);
                    }

                    var stego = Enumerable.Repeat(true, 6).ToArray();
                    double actual = coder.Embed(cover, costs, Bits(messageWord, 3), stego);
                    Assert.Equal(expected, actual);
                    if (double.IsPositiveInfinity(actual))
                        Assert.All(stego, bit => Assert.True(bit));
                    else
                    {
                        int word = 0;
                        double cost = 0;
                        for (int bit = 0; bit < 6; bit++)
                        {
                            if (stego[bit]) word |= 1 << bit;
                            if (stego[bit] != cover[bit]) cost += costs[bit];
                        }
                        Assert.Equal(messageWord, Syndrome(word));
                        Assert.Equal(expected, cost);
                    }
                }
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.NegativeInfinity)]
        public void EmptyMessage_StillRejectsInvalidCostsWithoutWriting(double cost)
        {
            var stego = new[] { true };
            Assert.Throws<ArgumentException>(() => new SyndromeTrellisCoder(2).Embed(new[] { false }, new[] { cost }, Array.Empty<bool>(), stego));
            Assert.True(stego[0]);
        }

        [Fact]
        public void EmptyMessage_StillRequiresMatchingBuffers()
        {
            var coder = new SyndromeTrellisCoder(2);
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[1], Array.Empty<double>(), Array.Empty<bool>(), new bool[1]));
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[1], new double[1], Array.Empty<bool>(), new bool[2]));
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[1], new double[1], Array.Empty<bool>(), Array.Empty<bool>()));
        }

        private static bool[] Bits(int word, int count) => Enumerable.Range(0, count).Select(bit => (word & (1 << bit)) != 0).ToArray();

        private static int Syndrome(int word)
        {
            int pairs = ((word ^ (word >> 1)) & 1)
                | (((word >> 2) ^ (word >> 3)) & 1) << 1
                | (((word >> 4) ^ (word >> 5)) & 1) << 2;
            return (pairs ^ (pairs << 1)) & 7;
        }

        private static JpegImage Coefficients(short value, int size = 8)
        {
            var image = JpegImage.Load(JpegImageTests.SampleJpeg(size, size));
            foreach (var component in image.Components)
                for (int i = 0; i < component.Coefficients.Length; i++)
                    if (i % 64 != 0)
                        component.Coefficients[i] = value;
            return image;
        }

        private sealed class WetPixels : IPixelCostModel
        {
            public double Cost(Image<Rgba32> image, int x, int y, int channel) => double.PositiveInfinity;
        }

        private sealed class WetSamples : ISampleCostModel
        {
            public double Cost(PcmAudio audio, long index) => double.PositiveInfinity;
        }

        private sealed class ConstantCoefficients(double cost) : ICoefficientCostModel
        {
            public double Cost(short coefficient, int zigzagIndex) => cost;
        }
    }
}
