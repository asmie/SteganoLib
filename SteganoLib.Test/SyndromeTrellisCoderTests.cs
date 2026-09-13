using System;
using System.Linq;
using SteganoLib.Coding;
using Xunit;

namespace SteganoLib.Test
{
    public class SyndromeTrellisCoderTests
    {
        private static (bool[] Cover, double[] Costs, bool[] Message) Random(int n, int m, int seed, bool uniformCosts = false)
        {
            var random = new Random(seed);
            var cover = new bool[n];
            var costs = new double[n];
            var message = new bool[m];
            for (int i = 0; i < n; i++)
            {
                cover[i] = random.Next(2) == 1;
                costs[i] = uniformCosts ? 1 : random.NextDouble() * 10 + 0.01;
            }
            for (int i = 0; i < m; i++)
                message[i] = random.Next(2) == 1;
            return (cover, costs, message);
        }

        [Theory]
        [InlineData(2, 1, 50)]
        [InlineData(2, 2, 50)]
        [InlineData(4, 3, 100)]
        [InlineData(7, 2, 400)]
        [InlineData(8, 5, 300)]
        [InlineData(10, 4, 200)]
        [InlineData(6, 1, 64)]
        [InlineData(6, 64, 20)]
        public void EmbedThenExtract_RecoversMessage(int height, int width, int messageLength)
        {
            var coder = new SyndromeTrellisCoder(height);
            var (cover, costs, message) = Random(messageLength * width, messageLength, height * 100 + width);
            var stego = new bool[cover.Length];

            double distortion = coder.Embed(cover, costs, message, stego);
            var extracted = new bool[messageLength];
            coder.Extract(stego, extracted);

            Assert.False(double.IsPositiveInfinity(distortion));
            Assert.Equal(message, extracted);
        }

        [Fact]
        public void Embed_ReturnsTheSumOfChangedCosts()
        {
            var coder = new SyndromeTrellisCoder(7);
            var (cover, costs, message) = Random(2000, 500, 11);
            var stego = new bool[cover.Length];

            double distortion = coder.Embed(cover, costs, message, stego);

            double actual = cover.Zip(stego, costs).Where(t => t.First != t.Second).Sum(t => t.Third);
            Assert.Equal(actual, distortion, 6);
        }

        [Theory]
        [InlineData(2, 0.22)]  // plain embedding would change 0.25 of the cover
        [InlineData(4, 0.10)]  // plain: 0.125
        [InlineData(8, 0.05)]  // plain: 0.0625
        public void Embed_ChangesFewerBitsThanPlainEmbedding(int width, double maxChangeRate)
        {
            var coder = new SyndromeTrellisCoder(9);
            const int m = 2000;
            var (cover, costs, message) = Random(m * width, m, width, uniformCosts: true);
            var stego = new bool[cover.Length];

            coder.Embed(cover, costs, message, stego);

            int changes = cover.Zip(stego).Count(p => p.First != p.Second);
            Assert.True((double)changes / cover.Length < maxChangeRate, $"changed {changes} of {cover.Length}");
        }

        [Fact]
        public void Embed_PrefersCheapElements()
        {
            var coder = new SyndromeTrellisCoder(8);
            const int m = 500, w = 4;
            var (cover, costs, message) = Random(m * w, m, 5);
            for (int i = 0; i < costs.Length; i++)
                costs[i] = i % 2 == 0 ? 1 : 100;
            var stego = new bool[cover.Length];

            coder.Embed(cover, costs, message, stego);

            int expensiveChanges = Enumerable.Range(0, cover.Length).Count(i => i % 2 == 1 && cover[i] != stego[i]);
            int cheapChanges = Enumerable.Range(0, cover.Length).Count(i => i % 2 == 0 && cover[i] != stego[i]);
            Assert.True(expensiveChanges * 10 < cheapChanges, $"expensive={expensiveChanges} cheap={cheapChanges}");
        }

        [Fact]
        public void Embed_NeverTouchesWetElements()
        {
            var coder = new SyndromeTrellisCoder(8);
            const int m = 300, w = 6;
            var (cover, costs, message) = Random(m * w, m, 6);
            var wet = new bool[cover.Length];
            var random = new Random(7);
            for (int i = 0; i < cover.Length; i++)
            {
                wet[i] = random.Next(3) == 0;
                if (wet[i]) costs[i] = double.PositiveInfinity;
            }
            var stego = new bool[cover.Length];

            double distortion = coder.Embed(cover, costs, message, stego);

            Assert.False(double.IsPositiveInfinity(distortion));
            for (int i = 0; i < cover.Length; i++)
                if (wet[i]) Assert.Equal(cover[i], stego[i]);
            var extracted = new bool[m];
            coder.Extract(stego, extracted);
            Assert.Equal(message, extracted);
        }

        [Fact]
        public void Embed_AllWet_ReturnsInfinity()
        {
            var coder = new SyndromeTrellisCoder(6);
            var (cover, costs, message) = Random(40, 20, 8);
            Array.Fill(costs, double.PositiveInfinity);
            var stego = new bool[cover.Length];

            // Unless the cover already carries the message, which is astronomically unlikely here.
            Assert.True(double.IsPositiveInfinity(coder.Embed(cover, costs, message, stego)));
        }

        [Fact]
        public void EmptyMessage_CopiesCover()
        {
            var coder = new SyndromeTrellisCoder(6);
            var cover = new[] { true, false, true };
            var stego = new bool[3];

            Assert.Equal(0, coder.Embed(cover, new double[3], Array.Empty<bool>(), stego));
            Assert.Equal(cover, stego);
        }

        [Fact]
        public void SubmatrixColumns_AreDeterministicAndHaveFixedRows()
        {
            var a = SyndromeTrellisCoder.SubmatrixColumns(8, 5);
            var b = SyndromeTrellisCoder.SubmatrixColumns(8, 5);

            Assert.Equal(a, b);
            Assert.All(a, c => Assert.Equal(1 | (1 << 7), c & (1 | (1 << 7))));
            Assert.All(a, c => Assert.InRange(c, 0, 255));
            Assert.NotEqual(a, SyndromeTrellisCoder.SubmatrixColumns(9, 5).Select(c => c & 255));
        }

        [Fact]
        public void Validation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SyndromeTrellisCoder(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SyndromeTrellisCoder(13));

            var coder = new SyndromeTrellisCoder(6);
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[7], new double[7], new bool[3], new bool[7]));
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[6], new double[5], new bool[3], new bool[6]));
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[6], new double[6], new bool[3], new bool[5]));
            Assert.Throws<ArgumentException>(() => coder.Embed(new bool[6], new[] { -1.0, 0, 0, 0, 0, 0 }, new bool[3], new bool[6]));
            Assert.Throws<ArgumentException>(() => coder.Extract(new bool[7], new bool[3]));
        }
    }
}
