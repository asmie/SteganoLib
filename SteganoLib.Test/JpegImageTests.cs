using System;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Jpeg;
using Xunit;

namespace SteganoLib.Test
{
    public class JpegImageTests
    {
        internal static Image<Rgba32> TestPicture(int width, int height, int seed)
        {
            var image = new Image<Rgba32>(width, height);
            var random = new Random(seed);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r = (byte)((x * 255 / Math.Max(1, width - 1) + random.Next(-20, 20)) & 0xFF);
                    byte g = (byte)((y * 255 / Math.Max(1, height - 1) + random.Next(-20, 20)) & 0xFF);
                    byte b = (byte)(((x + y) * 3 + random.Next(-40, 40)) & 0xFF);
                    image[x, y] = new Rgba32(r, g, b, 255);
                }
            }
            return image;
        }

        internal static byte[] EncodeWithImageSharp(Image<Rgba32> image, JpegEncodingColor color, int quality = 85, bool interleaved = true)
        {
            using var stream = new MemoryStream();
            image.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { ColorType = color, Quality = quality, Interleaved = interleaved });
            return stream.ToArray();
        }

        internal static byte[] SampleJpeg(int width = 64, int height = 48, JpegEncodingColor color = JpegEncodingColor.YCbCrRatio420, int quality = 85, int seed = 1)
        {
            using var picture = TestPicture(width, height, seed);
            return EncodeWithImageSharp(picture, color, quality);
        }

        private static void AssertPixelsEqual(byte[] expectedJpeg, byte[] actualJpeg)
        {
            using var expected = Image.Load<Rgba32>(expectedJpeg);
            using var actual = Image.Load<Rgba32>(actualJpeg);

            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert.True(expected[x, y] == actual[x, y], $"Pixel ({x},{y}) differs: {expected[x, y]} vs {actual[x, y]}");
        }

        public static TheoryData<int, int, JpegEncodingColor, bool> Layouts => new()
        {
            { 64, 48, JpegEncodingColor.YCbCrRatio444, true },
            { 64, 48, JpegEncodingColor.YCbCrRatio420, true },
            { 37, 23, JpegEncodingColor.YCbCrRatio420, true },
            { 37, 23, JpegEncodingColor.YCbCrRatio422, true },
            { 33, 17, JpegEncodingColor.YCbCrRatio411, true },
            { 33, 17, JpegEncodingColor.YCbCrRatio410, true },
            { 37, 23, JpegEncodingColor.Luminance, true },
            { 8, 8, JpegEncodingColor.YCbCrRatio444, true },
            { 1, 1, JpegEncodingColor.YCbCrRatio420, true },
            { 100, 75, JpegEncodingColor.Rgb, true },
            { 50, 40, JpegEncodingColor.Cmyk, true },
            { 64, 48, JpegEncodingColor.YCbCrRatio420, false },
            { 37, 23, JpegEncodingColor.YCbCrRatio444, false },
            { 37, 23, JpegEncodingColor.YCbCrRatio422, false },
        };

        [Theory]
        [MemberData(nameof(Layouts))]
        public void Reencode_IsPixelIdentical(int width, int height, JpegEncodingColor color, bool interleaved)
        {
            using var picture = TestPicture(width, height, 3);
            var original = EncodeWithImageSharp(picture, color, 80, interleaved);

            var reencoded = JpegImage.Load(original).ToArray();

            AssertPixelsEqual(original, reencoded);
        }

        [Theory]
        [MemberData(nameof(Layouts))]
        public void Reencode_PreservesCoefficients(int width, int height, JpegEncodingColor color, bool interleaved)
        {
            using var picture = TestPicture(width, height, 4);
            var first = JpegImage.Load(EncodeWithImageSharp(picture, color, 90, interleaved));

            var second = JpegImage.Load(first.ToArray());

            Assert.Equal(first.Width, second.Width);
            Assert.Equal(first.Height, second.Height);
            Assert.Equal(first.Components.Count, second.Components.Count);
            for (int i = 0; i < first.Components.Count; i++)
            {
                Assert.Equal(first.Components[i].Id, second.Components[i].Id);
                Assert.Equal(first.Components[i].HorizontalSampling, second.Components[i].HorizontalSampling);
                Assert.Equal(first.Components[i].VerticalSampling, second.Components[i].VerticalSampling);
                Assert.Equal(first.Components[i].Coefficients, second.Components[i].Coefficients);
            }
            for (int i = 0; i < 4; i++)
                Assert.Equal(first.QuantizationTables[i], second.QuantizationTables[i]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(1000)]
        public void RestartInterval_RoundTrips(int interval)
        {
            var original = SampleJpeg(70, 50);
            var image = JpegImage.Load(original);
            image.RestartInterval = interval;

            var withRestarts = image.ToArray();
            var reloaded = JpegImage.Load(withRestarts);

            Assert.Equal(interval, reloaded.RestartInterval);
            Assert.Equal(image.Components[0].Coefficients, reloaded.Components[0].Coefficients);
            AssertPixelsEqual(original, withRestarts);
        }

        [Fact]
        public void RestartInterval_GrayscaleRoundTrips()
        {
            var original = SampleJpeg(45, 31, JpegEncodingColor.Luminance);
            var image = JpegImage.Load(original);
            image.RestartInterval = 5;

            var withRestarts = image.ToArray();

            Assert.Equal(image.Components[0].Coefficients, JpegImage.Load(withRestarts).Components[0].Coefficients);
            AssertPixelsEqual(original, withRestarts);
        }

        [Fact]
        public void ModifiedCoefficient_SurvivesSaveAndLoad()
        {
            var image = JpegImage.Load(SampleJpeg());
            var block = image.Components[0].Block(0, 0);
            block[5] = 17;
            block[63] = -3;
            image.Components[1].Block(1, 1)[20] = -40;

            var reloaded = JpegImage.Load(image.ToArray());

            Assert.Equal(17, reloaded.Components[0].Block(0, 0)[5]);
            Assert.Equal(-3, reloaded.Components[0].Block(0, 0)[63]);
            Assert.Equal(-40, reloaded.Components[1].Block(1, 1)[20]);
            using var decodable = Image.Load<Rgba32>(reloaded.ToArray());
        }

        [Fact]
        public void LargeRunsAndValues_Encode()
        {
            var image = JpegImage.Load(SampleJpeg(16, 16, JpegEncodingColor.YCbCrRatio444, 100));
            foreach (var component in image.Components)
            {
                for (int b = 0; b < component.BlockCount; b++)
                {
                    var block = component.Block(b);
                    block.Clear();
                    block[0] = (short)(b % 2 == 0 ? 1000 : -1000);
                    block[63] = (short)(b % 2 == 0 ? -1023 : 1023); // run of 62 zeros then a 10-bit value
                    if (b % 3 == 0) block[17] = 1;
                }
            }

            var reloaded = JpegImage.Load(image.ToArray());

            for (int i = 0; i < image.Components.Count; i++)
                Assert.Equal(image.Components[i].Coefficients, reloaded.Components[i].Coefficients);
        }

        [Fact]
        public void Segments_ArePreserved()
        {
            var original = SampleJpeg();
            var image = JpegImage.Load(original);

            Assert.Contains(image.Segments, s => s.Marker == 0xE0); // JFIF APP0
            var reloaded = JpegImage.Load(image.ToArray());

            Assert.Equal(image.Segments.Select(s => s.Marker), reloaded.Segments.Select(s => s.Marker));
            Assert.Equal(image.Segments.Select(s => s.Payload), reloaded.Segments.Select(s => s.Payload));
        }

        [Fact]
        public void Clone_IsIndependent()
        {
            var image = JpegImage.Load(SampleJpeg());
            var clone = image.Clone();
            clone.Components[0].Block(0)[1] = 99;

            Assert.NotEqual(99, image.Components[0].Block(0)[1]);
            Assert.Equal(image.Components[0].Coefficients.Length, clone.Components[0].Coefficients.Length);
        }

        [Fact]
        public void Progressive_IsRejected()
        {
            var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xC2, 0x00, 0x0B, 0x08, 0x00, 0x08, 0x00, 0x08, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xD9 };
            Assert.Throws<NotSupportedException>(() => JpegImage.Load(data));
        }

        [Fact]
        public void NotAJpeg_IsRejected()
        {
            Assert.Throws<InvalidDataException>(() => JpegImage.Load(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
            Assert.Throws<InvalidDataException>(() => JpegImage.Load(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }));
        }

        [Fact]
        public void Truncated_IsRejected()
        {
            var data = SampleJpeg();
            Assert.ThrowsAny<Exception>(() => JpegImage.Load(data.AsSpan(0, 40).ToArray()));
        }

        [Fact]
        public void FileAndStream_RoundTrip()
        {
            var original = SampleJpeg();
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jpg");
            try
            {
                File.WriteAllBytes(path, original);
                var image = JpegImage.Load(path);
                image.Save(path);
                AssertPixelsEqual(original, File.ReadAllBytes(path));

                using var stream = new MemoryStream(original);
                using var output = new MemoryStream();
                JpegImage.Load(stream).Save(output);
                AssertPixelsEqual(original, output.ToArray());
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void HuffmanTable_FromFrequencies_RoundTripsSymbols()
        {
            var frequencies = new long[256];
            var random = new Random(5);
            for (int i = 0; i < 256; i++)
                frequencies[i] = i % 7 == 0 ? random.Next(1, 100000) : 0;
            frequencies[0xF0] = 1;
            frequencies[0x00] = 500000;

            var table = HuffmanTable.FromFrequencies(frequencies);
            var symbols = Enumerable.Range(0, 256).Where(i => frequencies[i] > 0).Select(i => (byte)i).ToArray();

            using var stream = new MemoryStream();
            var writer = new BitWriter(stream);
            foreach (var s in symbols)
                table.Encode(writer, s);
            writer.Flush();

            var reader = new BitReader(stream.ToArray(), 0);
            foreach (var s in symbols)
                Assert.Equal(s, table.Decode(reader));
        }

        [Fact]
        public void HuffmanTable_SingleSymbol_IsValid()
        {
            var frequencies = new long[256];
            frequencies[0x03] = 10;

            var table = HuffmanTable.FromFrequencies(frequencies);

            Assert.Equal(1, table.Counts.Sum(c => c));
            using var stream = new MemoryStream();
            var writer = new BitWriter(stream);
            table.Encode(writer, 0x03);
            table.Encode(writer, 0x03);
            writer.Flush();
            var reader = new BitReader(stream.ToArray(), 0);
            Assert.Equal(0x03, table.Decode(reader));
            Assert.Equal(0x03, table.Decode(reader));
        }

        [Fact]
        public void HuffmanTable_AllSymbols_StayWithin16Bits()
        {
            var frequencies = new long[256];
            long f = 1;
            for (int i = 0; i < 256; i++)
            {
                frequencies[i] = f;
                if (i % 8 == 7) f *= 2; // wide spread forces long codes that must be folded
            }

            var table = HuffmanTable.FromFrequencies(frequencies);

            Assert.Equal(256, table.Symbols.Length);
            Assert.Equal(16, table.Counts.Length);
        }
    }
}
