using System;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Payload;
using Xunit;

namespace SteganoLib.Test
{
    public class F5Tests
    {
        private static readonly StegoKey Key = StegoKey.FromPassphrase("f5", iterations: 10);
        private static readonly StegoKey OtherKey = StegoKey.FromPassphrase("f6", iterations: 10);

        private static JpegImage Cover(int width = 256, int height = 192, JpegEncodingColor color = JpegEncodingColor.YCbCrRatio420, int quality = 85)
        {
            return JpegImage.Load(JpegImageTests.SampleJpeg(width, height, color, quality, seed: 21));
        }

        private static byte[] RandomBytes(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(17)]
        [InlineData(300)]
        [InlineData(1500)]
        public void RoundTrip_ThroughFileBytes(int length)
        {
            var cover = Cover();
            var data = RandomBytes(length, length);

            new F5(Key).EmbedBytes(data, cover);
            var stego = JpegImage.Load(cover.ToArray());

            Assert.Equal(data, new F5(Key).ExtractBytes(stego));
        }

        [Theory]
        [InlineData(JpegEncodingColor.YCbCrRatio444)]
        [InlineData(JpegEncodingColor.Luminance)]
        [InlineData(JpegEncodingColor.YCbCrRatio422)]
        public void RoundTrip_OtherLayouts(JpegEncodingColor color)
        {
            var cover = Cover(120, 90, color);
            var data = RandomBytes(100, 7);

            new F5(Key).EmbedBytes(data, cover);

            Assert.Equal(data, new F5(Key).ExtractBytes(JpegImage.Load(cover.ToArray())));
        }

        [Fact]
        public void Stego_StillDecodesWithImageSharp()
        {
            var cover = Cover();
            new F5(Key).EmbedBytes(RandomBytes(500, 1), cover);

            using var image = Image.Load<Rgba32>(cover.ToArray());
            Assert.Equal(256, image.Width);
        }

        [Fact]
        public void Capacity_IsReachableAndOneMoreThrows()
        {
            var cover = Cover();
            var f5 = new F5(Key);
            int capacity = (int)f5.Capacity(cover);
            Assert.True(capacity > 100);

            var data = RandomBytes(capacity, 2);
            f5.EmbedBytes(data, cover);
            Assert.Equal(data, f5.ExtractBytes(cover));

            Assert.Throws<CapacityExceededException>(() => new F5(Key).EmbedBytes(new byte[capacity + 1], Cover()));
        }

        [Fact]
        public void Embedding_OnlyMovesAcCoefficientsTowardZero()
        {
            var cover = Cover();
            var stego = cover.Clone();
            new F5(Key).EmbedBytes(RandomBytes(400, 3), stego);

            int changed = 0;
            for (int c = 0; c < cover.Components.Count; c++)
            {
                var before = cover.Components[c].Coefficients;
                var after = stego.Components[c].Coefficients;
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] == after[i])
                        continue;

                    changed++;
                    Assert.NotEqual(0, i % 64);
                    Assert.Equal(Math.Abs(before[i]) - 1, Math.Abs(after[i]));
                    Assert.True(after[i] == 0 || Math.Sign(before[i]) == Math.Sign(after[i]));
                }
            }
            Assert.True(changed > 0);
        }

        [Fact]
        public void MatrixEncoding_ChangesFewerCoefficientsThanPlain()
        {
            var data = RandomBytes(60, 4);

            int matrix = CountChanges(new F5(Key), data);
            int plain = CountChanges(new F5(Key) { MaxK = 1 }, data);

            Assert.True(matrix < plain / 2, $"matrix={matrix} plain={plain}");
        }

        private static int CountChanges(F5 f5, byte[] data)
        {
            var cover = Cover();
            var stego = cover.Clone();
            f5.EmbedBytes(data, stego);

            int changed = 0;
            for (int c = 0; c < cover.Components.Count; c++)
                changed += cover.Components[c].Coefficients.Zip(stego.Components[c].Coefficients).Count(p => p.First != p.Second);
            return changed;
        }

        [Fact]
        public void ChooseK_LargeForSmallPayload_OneForLarge()
        {
            var cover = Cover();
            var f5 = new F5(Key);

            Assert.Equal(7, f5.ChooseK(cover, 1));
            // The guaranteed capacity is below the estimate, so k may still be 2 here; EmbedBytes falls back if needed.
            Assert.InRange(f5.ChooseK(cover, (int)f5.Capacity(cover)), 1, 2);
            Assert.Equal(1, f5.ChooseK(cover, (int)(f5.Capacity(cover) * 1.5)));
            Assert.InRange(f5.ChooseK(cover, 600), 3, 5);
        }

        [Fact]
        public void WrongKey_DoesNotRecoverData()
        {
            var cover = Cover();
            var data = RandomBytes(50, 5);
            new F5(Key).EmbedBytes(data, cover);

            Assert.NotEqual(data, new F5(OtherKey).ExtractBytes(cover));
        }

        [Fact]
        public void Extract_FromUntouchedCover_DoesNotThrow()
        {
            var result = new F5(Key).ExtractBytes(Cover());
            Assert.NotNull(result);
        }

        [Fact]
        public void EmptyCoefficients_HaveZeroCapacity()
        {
            var cover = Cover(16, 16, JpegEncodingColor.YCbCrRatio444, 100);
            foreach (var component in cover.Components)
                for (int i = 0; i < component.Coefficients.Length; i++)
                    if (i % 64 != 0) component.Coefficients[i] = 0;

            var f5 = new F5(Key);
            Assert.Equal(0, f5.Capacity(cover));
            Assert.Empty(f5.ExtractBytes(cover));
            Assert.Throws<CapacityExceededException>(() => f5.EmbedBytes(new byte[] { 1 }, cover));
        }

        [Fact]
        public void Pipeline_RoundTrip_AndWrongKeyNotFound()
        {
            var cover = Cover();
            var pipeline = new StegoPipeline<JpegImage>(new F5(Key));
            var data = RandomBytes(200, 6);

            pipeline.Embed(data, cover, Key);
            var stego = JpegImage.Load(cover.ToArray());

            var result = pipeline.Extract(stego, Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);
            Assert.Equal(ExtractionStatus.NotFound, new StegoPipeline<JpegImage>(new F5(OtherKey)).Extract(stego, OtherKey).Status);
        }

        [Fact]
        public void Pipeline_FileAndStreamHelpers()
        {
            var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jpg");
            var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jpg");
            var data = RandomBytes(40, 8);
            try
            {
                File.WriteAllBytes(input, JpegImageTests.SampleJpeg(128, 96));

                var pipeline = new StegoPipeline<JpegImage>(new F5(Key));
                pipeline.Embed(data, input, output, Key);
                Assert.Equal(data, pipeline.Extract(output, Key).Data);

                using var inStream = File.OpenRead(input);
                using var outStream = new MemoryStream();
                pipeline.Embed(data, inStream, outStream, Key);
                outStream.Position = 0;
                Assert.Equal(data, pipeline.Extract(outStream, Key).Data);

                new F5(Key).EmbedBytes(data, input, output);
                Assert.Equal(data, new F5(Key).ExtractBytes(output));
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        [Fact]
        public void Validation()
        {
            Assert.Throws<ArgumentNullException>(() => new F5(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new F5(Key).MaxK = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => new F5(Key).MaxK = 16);
            Assert.Throws<ArgumentNullException>(() => new F5(Key).EmbedBytes(null, Cover(16, 16)));
            Assert.Throws<ArgumentNullException>(() => new F5(Key).EmbedBytes(new byte[1], null));
            Assert.Throws<ArgumentNullException>(() => new F5(Key).ExtractBytes(null));
        }
    }
}
