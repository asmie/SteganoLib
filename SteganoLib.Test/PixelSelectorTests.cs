using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class PixelSelectorTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(7, 3)]
        [InlineData(100, 37)]
        public void Keyed_VisitsEveryPixelOnce(int width, int height)
        {
            var selector = new KeyedPermutationSelector(StegoKey.FromBytes(new byte[] { 42 }));
            var pixels = selector.Pixels(width, height).ToList();

            Assert.Equal(width * height, pixels.Count);
            Assert.Equal(width * height, pixels.Distinct().Count());
            Assert.All(pixels, p =>
            {
                Assert.InRange(p.X, 0, width - 1);
                Assert.InRange(p.Y, 0, height - 1);
            });
        }

        [Fact]
        public void Keyed_SameKey_SameOrder()
        {
            var a = new KeyedPermutationSelector(StegoKey.FromPassphrase("pw", iterations: 10));
            var b = new KeyedPermutationSelector(StegoKey.FromPassphrase("pw", iterations: 10));

            Assert.Equal(a.Pixels(50, 50), b.Pixels(50, 50));
        }

        [Fact]
        public void Keyed_DifferentKey_DifferentOrder()
        {
            var a = new KeyedPermutationSelector(StegoKey.FromPassphrase("pw1", iterations: 10));
            var b = new KeyedPermutationSelector(StegoKey.FromPassphrase("pw2", iterations: 10));

            Assert.NotEqual(a.Pixels(50, 50).Take(100), b.Pixels(50, 50).Take(100));
        }

        [Fact]
        public void Keyed_NullKey_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new KeyedPermutationSelector(null));
        }

        [Fact]
        public void Prng_MatchesLegacyDrawOrder()
        {
            var rows = new PRNG();
            rows.Initialize(11);
            var columns = new PRNG();
            columns.Initialize(12);

            var expected = new List<Point>();
            var used = new HashSet<Point>();
            while (expected.Count < 200)
            {
                var p = new Point(columns.Next(30), rows.Next(20));
                if (used.Add(p))
                    expected.Add(p);
            }

            var selector = new PrngPixelSelector(rowSeed: 11, columnSeed: 12);
            Assert.Equal(expected, selector.Pixels(30, 20).Take(200));
        }

        [Fact]
        public void Prng_VisitsEveryPixelOnce()
        {
            var selector = new PrngPixelSelector(1, 2);
            var pixels = selector.Pixels(8, 8).ToList();

            Assert.Equal(64, pixels.Count);
            Assert.Equal(64, pixels.Distinct().Count());
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, -1)]
        [InlineData(0, 0)]
        public async Task Prng_CorrelatedSeedsPreservePrefixAndThenFail(int rowSeed, int columnSeed)
        {
            await Task.Run(() =>
            {
                var selector = new PrngPixelSelector(rowSeed, columnSeed);
                var prefix = selector.Pixels(8, 8).Take(8).ToArray();
                Assert.Equal(8, prefix.Distinct().Count());
                Assert.All(prefix, p => Assert.Equal(p.X, p.Y));
                using var pixels = selector.Pixels(8, 8).GetEnumerator();
                foreach (var expected in prefix)
                {
                    Assert.True(pixels.MoveNext());
                    Assert.Equal(expected, pixels.Current);
                }
                var error = Assert.Throws<InvalidOperationException>(() => pixels.MoveNext());
                Assert.Contains("progress", error.Message);
            }).WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 8)]
        [InlineData(8, 1)]
        public void Prng_EqualSeedsVisitDegenerateDimensions(int width, int height)
        {
            var pixels = new PrngPixelSelector(1, 1).Pixels(width, height).ToList();
            Assert.Equal(width * height, pixels.Count);
            Assert.Equal(pixels.Count, pixels.Distinct().Count());
        }

        [Fact]
        public void Prng_StuckCustomGeneratorFailsWithinBoundedDraws()
        {
            string name = nameof(PixelSelectorTests) + nameof(StuckRandom);
            Assert.True(PRNG.RegisterPRNG(name, typeof(StuckRandom)));
            using var pixels = new PrngPixelSelector(1, 2, name).Pixels(2, 2).GetEnumerator();
            Assert.True(pixels.MoveNext());
            Assert.Equal(new Point(0, 0), pixels.Current);
            Assert.Throws<InvalidOperationException>(() => pixels.MoveNext());
        }

        [Fact]
        public void Prng_DuplicateBudgetResetsWhenNewPixelsAreFound()
        {
            string name = nameof(PixelSelectorTests) + nameof(BurstyRandom);
            Assert.True(PRNG.RegisterPRNG(name, typeof(BurstyRandom)));
            var pixels = new PrngPixelSelector(1, 2, name).Pixels(2, 2).ToArray();
            Assert.Equal(new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1) }, pixels);
        }

        public sealed class BurstyRandom : Random
        {
            private readonly bool _row;
            private int _calls;

            public BurstyRandom(int seed) : base(seed) => _row = seed == 1;

            public override int Next(int maxValue)
            {
                int pixel = _calls++ / 800;
                return _row ? pixel / 2 : pixel % 2;
            }
        }

        public sealed class StuckRandom : Random
        {
            private int _calls;

            public StuckRandom(int seed) : base(seed) { }

            public override int Next(int maxValue)
            {
                // Fail the test instead of hanging if the selector's progress guard regresses.
                if (++_calls > 10_000)
                    throw new TimeoutException("The selector did not stop a stuck generator.");
                return 0;
            }
        }

        [Fact]
        public void Prng_IsReEnumerable()
        {
            var selector = new PrngPixelSelector(5, 6);
            Assert.Equal(selector.Pixels(10, 10).Take(20), selector.Pixels(10, 10).Take(20));
        }
    }
}
