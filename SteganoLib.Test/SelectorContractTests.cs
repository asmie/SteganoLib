using System;
using System.Collections.Generic;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class SelectorContractTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 17, 31 });

        [Theory]
        [InlineData(ColorChannels.All, 1, 26, false)]
        [InlineData(ColorChannels.All, 2, 42, false)]
        [InlineData(ColorChannels.All, 3, 90, false)]
        [InlineData(ColorChannels.Red | ColorChannels.Blue, 3, 58, false)]
        [InlineData(ColorChannels.All, 1, 26, true)]
        [InlineData(ColorChannels.All, 2, 42, true)]
        public void PixelSubset_ExactCapacityRoundTripsWithoutTouchingOtherPixels(ColorChannels channels, int bits, int capacity, bool trellis)
        {
            // 257 selected pixels exercise a partial channel cycle.
            using var image = new Image<Rgba32>(514, 1, new Rgba32(100, 100, 100));
            var algorithm = new LSB(new EvenPixels())
            {
                Channels = channels,
                BitsPerPixel = bits,
                TrellisCoder = trellis ? new SyndromeTrellisCoder(3) : null,
            };
            Assert.Equal(capacity, algorithm.Capacity(image));
            var original = Pixels(image);
            Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(new byte[capacity + 1], image));
            Assert.Equal(original, Pixels(image));

            var data = new byte[capacity];
            new Random(4).NextBytes(data);
            algorithm.EmbedBytes(data, image);
            Assert.Equal(data, algorithm.ExtractBytes(image));
            for (int x = 1; x < image.Width; x += 2)
                Assert.Equal(new Rgba32(100, 100, 100), image[x, 0]);
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(4, true)]
        public void SampleSubset_ExactCapacityRoundTripsWithoutTouchingOtherSamples(int bits, bool trellis)
        {
            var audio = new PcmAudio(8000, 1, 16, Enumerable.Repeat(100, 514).ToArray());
            var original = (int[])audio.Samples.Clone();
            var algorithm = new AudioLsb(new EvenSamples())
            {
                BitsPerSample = bits,
                TrellisCoder = trellis ? new SyndromeTrellisCoder(3) : null,
            };
            long capacity = 257 * bits / 8 - 6;
            Assert.Equal(capacity, algorithm.Capacity(audio));
            Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(new byte[capacity + 1], audio));
            Assert.Equal(original, audio.Samples);

            var data = new byte[capacity];
            new Random(5).NextBytes(data);
            algorithm.EmbedBytes(data, audio);
            Assert.Equal(data, algorithm.ExtractBytes(PcmAudio.Load(audio.ToArray())));
            for (int i = 1; i < audio.Samples.Length; i += 2)
                Assert.Equal(original[i], audio.Samples[i]);
        }

        [Fact]
        public void SubsetsTooSmallForHeader_EmptyEmbeddingIsANoOp()
        {
            using var image = new Image<Rgba32>(80, 1, new Rgba32(100, 100, 100));
            var pixels = Pixels(image);
            var lsb = new LSB(new EvenPixels());
            Assert.Equal(0, lsb.Capacity(image));
            lsb.EmbedBytes(Array.Empty<byte>(), image);
            Assert.Empty(lsb.ExtractBytes(image));
            Assert.Equal(pixels, Pixels(image));

            var audio = new PcmAudio(8000, 1, 16, Enumerable.Repeat(100, 80).ToArray());
            var samples = (int[])audio.Samples.Clone();
            var audioLsb = new AudioLsb(new EvenSamples());
            Assert.Equal(0, audioLsb.Capacity(audio));
            audioLsb.EmbedBytes(Array.Empty<byte>(), audio);
            Assert.Empty(audioLsb.ExtractBytes(audio));
            Assert.Equal(samples, audio.Samples);
        }

        [Fact]
        public void NestedAdaptiveSelectors_IntersectFiltersAndPreserveOrder()
        {
            using var image = JpegImageTests.TestPicture(60, 40, 17);
            var inner = new AdaptivePixelSelector(new EvenPixels()) { MinVariance = 1 };
            var outer = new AdaptivePixelSelector(inner) { MinVariance = 5 };
            var expected = inner.Pixels(image).Where(p => outer.Score(image, p.X, p.Y) >= 5).ToArray();
            Assert.NotEmpty(expected);
            Assert.Equal(expected, outer.Pixels(image));
            Assert.Equal(6, outer.StableHighBits);

            var lsb = new LSB(outer);
            var data = new byte[lsb.Capacity(image)];
            Assert.NotEmpty(data);
            new Random(6).NextBytes(data);
            lsb.EmbedBytes(data, image);
            Assert.Equal(expected, outer.Pixels(image));
            Assert.Equal(data, lsb.ExtractBytes(image));
        }

        [Theory]
        [InlineData(LsbEmbeddingMode.Match, false)]
        [InlineData(LsbEmbeddingMode.Replace, false)]
        [InlineData(LsbEmbeddingMode.Match, true)]
        public void AdaptiveWrapper_PreservesSevenBitInnerSelection(LsbEmbeddingMode mode, bool trellis)
        {
            using var image = new Image<Rgba32>(64, 32);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                    image[x, y] = x % 2 == 0 ? new Rgba32(101, 102, 105) : new Rgba32(104, 107, 108);
            using var cover = image.Clone();
            var outer = new AdaptivePixelSelector(new SevenBitSelector()) { MinVariance = 0 };
            Assert.Equal(7, outer.StableHighBits);
            // Its own score must retain the six-bit scale despite a stricter inner selector.
            Assert.Equal(new AdaptivePixelSelector(new EvenPixels()).Score(image, 10, 10), outer.Score(image, 10, 10));
            var positions = outer.Pixels(image).ToArray();
            var algorithm = new LSB(outer)
            {
                BitsPerPixel = 3,
                EmbeddingMode = mode,
                TrellisCoder = trellis ? new SyndromeTrellisCoder(3) : null,
            };
            var data = new byte[algorithm.Capacity(image)];
            Assert.NotEmpty(data);
            new Random(7).NextBytes(data);
            algorithm.EmbedBytes(data, image);
            Assert.Equal(data, algorithm.ExtractBytes(image));
            Assert.Equal(positions, outer.Pixels(image));
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    Assert.Equal(cover[x, y].R >> 1, image[x, y].R >> 1);
                    Assert.Equal(cover[x, y].G >> 1, image[x, y].G >> 1);
                    Assert.Equal(cover[x, y].B >> 1, image[x, y].B >> 1);
                }
        }

        [Fact]
        public void BuiltInCounts_DoNotNeedToEnumerateLargeDomains()
        {
            Assert.Equal((long)int.MaxValue * int.MaxValue, new KeyedPermutationSelector(Key).Count(int.MaxValue, int.MaxValue));
            Assert.Equal(long.MaxValue, new KeyedSampleSelector(Key).Count(long.MaxValue));
            Assert.Equal(64, new PrngPixelSelector(1, 1).Count(8, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new KeyedPermutationSelector(Key).Count(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new KeyedSampleSelector(Key).Count(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PrngPixelSelector(1, 2).Count(1, 0));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(515)]
        public void InvalidDeclaredCounts_AreRejectedBeforeEmbedding(long count)
        {
            var selector = new DeclaredCount(count);
            using var image = new Image<Rgba32>(514, 1);
            var pixels = Pixels(image);
            var lsb = new LSB(selector);
            Assert.Throws<InvalidOperationException>(() => lsb.Capacity(image));
            Assert.Throws<InvalidOperationException>(() => lsb.EmbedBytes(Array.Empty<byte>(), image));
            Assert.Equal(pixels, Pixels(image));

            var audio = new PcmAudio(8000, 1, 16, new int[514]);
            var samples = (int[])audio.Samples.Clone();
            var audioLsb = new AudioLsb(selector);
            Assert.Throws<InvalidOperationException>(() => audioLsb.Capacity(audio));
            Assert.Throws<InvalidOperationException>(() => audioLsb.EmbedBytes(Array.Empty<byte>(), audio));
            Assert.Equal(samples, audio.Samples);
        }

        private static byte[] Pixels(Image<Rgba32> image)
        {
            var result = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(result);
            return result;
        }

        private sealed class EvenPixels : IPixelSelector
        {
            public IEnumerable<Point> Pixels(int width, int height)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x += 2)
                        yield return new Point(x, y);
            }
        }

        private sealed class EvenSamples : ISampleSelector
        {
            public IEnumerable<long> Indices(long count)
            {
                for (long i = 0; i < count; i += 2)
                    yield return i;
            }
        }

        private sealed class SevenBitSelector : IContentAwarePixelSelector
        {
            public int StableHighBits => 7;
            public IEnumerable<Point> Pixels(int width, int height) => throw new NotSupportedException();
            public IEnumerable<Point> Pixels(Image<Rgba32> image)
            {
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        if ((image[x, y].R >> 1) % 2 == 0)
                            yield return new Point(x, y);
            }
        }

        private sealed class DeclaredCount(long count) : IPixelSelector, ISampleSelector
        {
            public long Count(int width, int height) => count;
            public long Count(long sampleCount) => count;
            public IEnumerable<Point> Pixels(int width, int height) => throw new NotSupportedException("Count must be validated before enumeration.");
            public IEnumerable<long> Indices(long sampleCount) => throw new NotSupportedException("Count must be validated before enumeration.");
        }
    }
}
