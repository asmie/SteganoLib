using System;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Payload;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class TrellisEmbeddingTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x5C });

        private static LSB TrellisLsb(int height = 7) => new(new KeyedPermutationSelector(Key)) { TrellisCoder = new SyndromeTrellisCoder(height) };

        private static Image<Rgba32> HalfFlatHalfNoise(int width, int height, int seed)
        {
            var image = new Image<Rgba32>(width, height);
            var random = new Random(seed);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    image[x, y] = x < width / 2
                        ? new Rgba32(120, 120, 120, 255)
                        : new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            return image;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(500)]
        public void Lsb_RoundTrip(int length)
        {
            var data = new byte[length];
            new Random(length).NextBytes(data);
            using var image = HalfFlatHalfNoise(80, 60, 1);

            TrellisLsb().EmbedBytes(data, image);

            Assert.Equal(data, TrellisLsb().ExtractBytes(image));
            // A reader without a coder configured still recovers the payload: parameters travel in the header.
            Assert.Equal(data, new LSB(new KeyedPermutationSelector(Key)).ExtractBytes(image));
        }

        [Fact]
        public void Lsb_AvoidsFlatRegion()
        {
            using var cover = HalfFlatHalfNoise(120, 80, 2);
            using var stego = cover.Clone();
            var data = new byte[200];
            new Random(3).NextBytes(data);

            TrellisLsb(8).EmbedBytes(data, stego);

            int flatChanges = 0, texturedChanges = 0;
            for (int y = 0; y < cover.Height; y++)
                for (int x = 0; x < cover.Width; x++)
                    if (cover[x, y] != stego[x, y])
                    {
                        if (x < 60) flatChanges++; else texturedChanges++;
                    }

            // Only the plainly written header can land in the flat half.
            Assert.True(flatChanges <= 48, $"flat={flatChanges}");
            Assert.True(texturedChanges > 100, $"textured={texturedChanges}");
        }

        [Fact]
        public void Lsb_ChangesFewerPixelsThanDirectEmbedding()
        {
            var data = new byte[300];
            new Random(4).NextBytes(data);

            int direct = CountChanges(new LSB(new KeyedPermutationSelector(Key)), data);
            int trellis = CountChanges(TrellisLsb(8), data);

            Assert.True(trellis < direct * 0.75, $"trellis={trellis} direct={direct}");
        }

        private static int CountChanges(LSB lsb, byte[] data)
        {
            using var cover = HalfFlatHalfNoise(120, 80, 5);
            using var stego = cover.Clone();
            lsb.EmbedBytes(data, stego);

            int changes = 0;
            for (int y = 0; y < cover.Height; y++)
                for (int x = 0; x < cover.Width; x++)
                    if (cover[x, y] != stego[x, y]) changes++;
            return changes;
        }

        [Fact]
        public void Lsb_UsesFullCapacityWithWidthOne()
        {
            using var image = HalfFlatHalfNoise(40, 40, 6);
            var lsb = TrellisLsb(6);
            var data = new byte[lsb.Capacity(image)];
            new Random(7).NextBytes(data);

            lsb.EmbedBytes(data, image);

            Assert.Equal(data, lsb.ExtractBytes(image));
            Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[data.Length + 1], image));
        }

        [Fact]
        public void Lsb_RespectsMaxTrellisWidth()
        {
            using var image = HalfFlatHalfNoise(100, 100, 8);
            var lsb = TrellisLsb(6);
            lsb.MaxTrellisWidth = 3;
            var data = new byte[10];

            lsb.EmbedBytes(data, image);

            Assert.Equal(data, lsb.ExtractBytes(image));
        }

        [Fact]
        public void Lsb_Pipeline_RoundTrip()
        {
            using var image = HalfFlatHalfNoise(100, 80, 9);
            var pipeline = new StegoPipeline<Image<Rgba32>>(TrellisLsb());
            var data = new byte[150];
            new Random(10).NextBytes(data);

            pipeline.Embed(data, image, Key);

            var result = new StegoPipeline<Image<Rgba32>>(TrellisLsb()).Extract(image, Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);
        }

        [Fact]
        public void Lsb_Validation()
        {
            var lsb = TrellisLsb();
            Assert.Throws<ArgumentNullException>(() => lsb.CostModel = null);
            Assert.Throws<ArgumentOutOfRangeException>(() => lsb.MaxTrellisWidth = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => lsb.MaxTrellisWidth = 256);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextureCostModel().Smoothing = 0);
        }

        private static F5 TrellisF5(int height = 7) => new(Key) { TrellisCoder = new SyndromeTrellisCoder(height) };

        private static JpegImage Cover() => JpegImage.Load(JpegImageTests.SampleJpeg(256, 192, quality: 85, seed: 33));

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(200)]
        public void F5_RoundTrip(int length)
        {
            var data = new byte[length];
            new Random(length).NextBytes(data);
            var cover = Cover();

            TrellisF5().EmbedBytes(data, cover);
            var stego = JpegImage.Load(cover.ToArray());

            Assert.Equal(data, TrellisF5().ExtractBytes(stego));
            Assert.Equal(data, new F5(Key).ExtractBytes(stego));
        }

        [Fact]
        public void F5_NeverZeroesPayloadCoefficients()
        {
            var cover = Cover();
            var stego = cover.Clone();
            var data = new byte[300];
            new Random(11).NextBytes(data);

            TrellisF5().EmbedBytes(data, stego);

            long zeroed = 0, changed = 0;
            for (int c = 0; c < cover.Components.Count; c++)
            {
                var before = cover.Components[c].Coefficients;
                var after = stego.Components[c].Coefficients;
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] == after[i]) continue;
                    changed++;
                    Assert.Equal(Math.Abs(before[i]) - 1, Math.Abs(after[i]));
                    if (after[i] == 0) zeroed++;
                }
            }

            Assert.True(changed > 0);
            Assert.True(zeroed <= 56, $"zeroed={zeroed}"); // only the plainly written header may shrink coefficients
        }

        [Fact]
        public void F5_ChangesFewerCoefficientsThanMatrixEncoding()
        {
            var data = new byte[300];
            new Random(12).NextBytes(data);

            int matrix = Changes(new F5(Key), data);
            int trellis = Changes(TrellisF5(8), data);

            Assert.True(trellis < matrix, $"trellis={trellis} matrix={matrix}");
        }

        private static int Changes(F5 f5, byte[] data)
        {
            var cover = Cover();
            var stego = cover.Clone();
            f5.EmbedBytes(data, stego);
            int changes = 0;
            for (int c = 0; c < cover.Components.Count; c++)
                changes += cover.Components[c].Coefficients.Zip(stego.Components[c].Coefficients).Count(p => p.First != p.Second);
            return changes;
        }

        [Fact]
        public void F5_Pipeline_RoundTrip()
        {
            var cover = Cover();
            var pipeline = new StegoPipeline<JpegImage>(TrellisF5());
            var data = new byte[100];
            new Random(13).NextBytes(data);

            pipeline.Embed(data, cover, Key);

            var result = new StegoPipeline<JpegImage>(TrellisF5()).Extract(JpegImage.Load(cover.ToArray()), Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);
        }

        [Fact]
        public void F5_Validation()
        {
            var f5 = TrellisF5();
            Assert.Throws<ArgumentNullException>(() => f5.CostModel = null);
            Assert.Throws<ArgumentOutOfRangeException>(() => f5.MaxTrellisWidth = 0);
            Assert.Equal(double.PositiveInfinity, new MagnitudeCostModel().Cost(1, 5));
            Assert.Equal(double.PositiveInfinity, new MagnitudeCostModel().Cost(-1, 5));
            Assert.Equal(1.0, new MagnitudeCostModel().Cost(2, 5));
            Assert.Equal(0.25, new MagnitudeCostModel().Cost(-5, 5));
        }
    }
}
