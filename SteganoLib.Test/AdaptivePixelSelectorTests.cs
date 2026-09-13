using System;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class AdaptivePixelSelectorTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 77 });

        private static AdaptivePixelSelector CreateSelector(double minVariance = 1.0)
        {
            return new AdaptivePixelSelector(new KeyedPermutationSelector(Key)) { MinVariance = minVariance };
        }

        // Left half flat grey, right half random noise.
        private static Image<Rgba32> HalfFlatHalfNoise(int width, int height, int seed)
        {
            var image = new Image<Rgba32>(width, height);
            var random = new Random(seed);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    image[x, y] = x < width / 2
                        ? new Rgba32(120, 120, 120, 255)
                        : new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                }
            }
            return image;
        }

        [Fact]
        public void FlatImage_SelectsNothing()
        {
            using var image = new Image<Rgba32>(20, 20);
            Assert.Empty(CreateSelector().Pixels(image));
        }

        [Fact]
        public void NoisyImage_SelectsEverything()
        {
            using var image = HalfFlatHalfNoise(40, 20, 1);
            using var noisy = image.Clone(c => c.Crop(new Rectangle(20, 0, 20, 20)));

            Assert.Equal(400, CreateSelector().Pixels(noisy).Count());
        }

        [Fact]
        public void MixedImage_SelectsOnlyTexturedSide()
        {
            using var image = HalfFlatHalfNoise(40, 20, 2);

            var selected = CreateSelector().Pixels(image).ToList();

            Assert.NotEmpty(selected);
            // Pixels at x = 19 border the noise and may score above the threshold.
            Assert.All(selected, p => Assert.True(p.X >= 19, $"flat pixel {p} was selected"));
        }

        [Fact]
        public void Threshold_ZeroSelectsEverything()
        {
            using var image = new Image<Rgba32>(10, 10);
            Assert.Equal(100, CreateSelector(0).Pixels(image).Count());
        }

        [Fact]
        public void Selection_IsUnchangedAfterEmbedding()
        {
            using var image = HalfFlatHalfNoise(60, 40, 3);
            var selector = CreateSelector();
            var before = selector.Pixels(image).ToList();

            var lsb = new LSB(selector);
            var data = new byte[lsb.Capacity(image)];
            new Random(4).NextBytes(data);
            lsb.EmbedBytes(data, image);

            Assert.Equal(before, selector.Pixels(image));
        }

        [Fact]
        public void Embedding_KeepsHighSixBitsAndFlatRegion()
        {
            using var cover = HalfFlatHalfNoise(60, 40, 5);
            using var stego = cover.Clone();

            var lsb = new LSB(CreateSelector());
            var data = new byte[lsb.Capacity(stego)];
            new Random(6).NextBytes(data);
            lsb.EmbedBytes(data, stego);

            for (int y = 0; y < cover.Height; y++)
            {
                for (int x = 0; x < cover.Width; x++)
                {
                    var c = cover[x, y];
                    var s = stego[x, y];
                    Assert.Equal(c.R >> 2, s.R >> 2);
                    Assert.Equal(c.G >> 2, s.G >> 2);
                    Assert.Equal(c.B >> 2, s.B >> 2);
                    Assert.Equal(c.A, s.A);
                    if (x < 19)
                        Assert.Equal(c, s);
                }
            }
        }

        [Fact]
        public void Embedding_StillMovesInBothDirections()
        {
            // Values of the form 4k+1 and 4k+2 can move either way without changing the top six bits.
            // Mixing two blocks (25 and 26 after >> 2) gives the selector something to score.
            byte[] choices = { 101, 102, 105, 106 };
            using var image = new Image<Rgba32>(60, 60);
            var random = new Random(7);
            for (int y = 0; y < 60; y++)
                for (int x = 0; x < 60; x++)
                    image[x, y] = new Rgba32(choices[random.Next(4)], choices[random.Next(4)], choices[random.Next(4)], 255);
            using var cover = image.Clone();

            var lsb = new LSB(CreateSelector(0.05));
            var data = new byte[lsb.Capacity(image)];
            random.NextBytes(data);
            lsb.EmbedBytes(data, image);

            int up = 0, down = 0;
            for (int y = 0; y < 60; y++)
                for (int x = 0; x < 60; x++)
                {
                    if (image[x, y].R > cover[x, y].R) up++;
                    if (image[x, y].R < cover[x, y].R) down++;
                }

            Assert.True(up > 50, $"up={up}");
            Assert.True(down > 50, $"down={down}");
            Assert.Equal(data, lsb.ExtractBytes(image));
        }

        [Fact]
        public void RoundTrip_ThroughPipeline()
        {
            using var image = HalfFlatHalfNoise(80, 60, 8);
            var pipeline = new StegoPipeline<Image<Rgba32>>(new LSB(CreateSelector()));
            var data = new byte[pipeline.Capacity(image)];
            new Random(9).NextBytes(data);

            pipeline.Embed(data, image, Key);
            var result = pipeline.Extract(image, Key);

            Assert.True(result.IsSuccess);
            Assert.Equal(data, result.Data);
        }

        [Fact]
        public void Capacity_ReflectsSelectedPixelCount()
        {
            using var image = HalfFlatHalfNoise(40, 20, 10);
            var selector = CreateSelector();
            var lsb = new LSB(selector) { BitsPerPixel = 3 };

            long selected = selector.Pixels(image).LongCount();
            Assert.Equal(selected * 3 / 8 - 6, lsb.Capacity(image));

            Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[lsb.Capacity(image) + 1], image));
        }

        [Fact]
        public void Score_FlatIsZero_NoisyIsPositive()
        {
            using var image = HalfFlatHalfNoise(40, 20, 11);
            var selector = CreateSelector();

            Assert.Equal(0, selector.Score(image, 5, 5));
            Assert.True(selector.Score(image, 30, 10) > 1.0);
        }

        [Fact]
        public void Pixels_WithoutImage_NotSupported()
        {
            Assert.Throws<NotSupportedException>(() => CreateSelector().Pixels(10, 10));
        }

        [Fact]
        public void Validation()
        {
            Assert.Throws<ArgumentNullException>(() => new AdaptivePixelSelector(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateSelector().MinVariance = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateSelector().MinVariance = double.NaN);
            Assert.Throws<ArgumentNullException>(() => CreateSelector().Pixels(null));
        }
    }
}
