using System;
using System.Collections.Generic;
using System.Linq;
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

        [Fact]
        public void Prng_IsReEnumerable()
        {
            var selector = new PrngPixelSelector(5, 6);
            Assert.Equal(selector.Pixels(10, 10).Take(20), selector.Pixels(10, 10).Take(20));
        }
    }
}
