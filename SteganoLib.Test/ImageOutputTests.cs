using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using SteganoLib.Sharing;
using SteganoLib.Video;
using Xunit;

namespace SteganoLib.Test
{
    public class ImageOutputTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "stegano-output-" + Path.GetRandomFileName());
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 31, 41, 59 });
        private static readonly byte[] Payload = Enumerable.Range(0, 60).Select(i => (byte)i).ToArray();

        public ImageOutputTests() => Directory.CreateDirectory(_directory);

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private string PathFor(string name) => Path.Combine(_directory, name);

        private static LSB Algorithm() => new(new KeyedPermutationSelector(Key)) { BitsPerPixel = 3 };

        private string Cover(PngColorType colorType = PngColorType.Grayscale)
        {
            string path = PathFor("cover.png");
            using var image = new Image<Rgba32>(64, 64);
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    byte value = (byte)(x * 3 + y);
                    image[x, y] = new Rgba32(value, value, value);
                }
            image.SaveAsPng(path, new PngEncoder { ColorType = colorType, BitDepth = PngBitDepth.Bit8 });
            return path;
        }

        [Theory]
        [InlineData(".png")]
        [InlineData(".bmp")]
        [InlineData(".tif")]
        [InlineData(".tiff")]
        [InlineData(".PNG")]
        public void FileHelpers_RoundTripFromGrayscaleAndPaletteCovers(string extension)
        {
            foreach (var colorType in new[] { PngColorType.Grayscale, PngColorType.Palette })
            {
                string input = Cover(colorType);
                string output = PathFor("stego" + extension);
                var algorithm = Algorithm();
                algorithm.EmbedBytes(Payload, input, output);
                Assert.Equal(Payload, algorithm.ExtractBytes(output));

                var pipeline = new StegoPipeline<Image<Rgba32>>(algorithm);
                pipeline.Embed(Payload, input, output, Key);
                var recovered = pipeline.Extract(output, Key);
                Assert.True(recovered.IsSuccess);
                Assert.Equal(Payload, recovered.Data);
            }
        }

        [Theory]
        [InlineData(".jpg")]
        [InlineData(".jpeg")]
        [InlineData(".jfif")]
        [InlineData(".gif")]
        [InlineData(".webp")]
        [InlineData(".unknown")]
        [InlineData("")]
        public void FileHelpers_RejectUnsupportedOutputsWithoutOverwriting(string extension)
        {
            string input = Cover();
            string output = PathFor("existing" + extension);
            byte[] original = { 9, 8, 7 };
            File.WriteAllBytes(output, original);
            var algorithm = Algorithm();
            Assert.Throws<NotSupportedException>(() => algorithm.EmbedBytes(Payload, input, output));
            Assert.Equal(original, File.ReadAllBytes(output));

            var pipeline = new StegoPipeline<Image<Rgba32>>(algorithm);
            Assert.Throws<NotSupportedException>(() => pipeline.Embed(Payload, input, output, Key));
            Assert.Equal(original, File.ReadAllBytes(output));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Sharing_ValidatesEveryOutputBeforeWriting(bool authenticated)
        {
            string input = Cover();
            var inputs = new[] { input, input };
            string first = PathFor("first.png");
            var outputs = new[] { first, PathFor("last.jpg") };
            byte[] original = { 1, 2, 3 };
            File.WriteAllBytes(first, original);
            var algorithm = new SharedCoding<Image<Rgba32>>(Algorithm(), 2);

            if (authenticated)
            {
                var pipeline = new StegoPipeline<IReadOnlyList<Image<Rgba32>>>(algorithm);
                Assert.Throws<NotSupportedException>(() => pipeline.Embed(Payload, inputs, outputs, Key));
            }
            else
                Assert.Throws<NotSupportedException>(() => algorithm.EmbedBytes(Payload, inputs, outputs));

            Assert.Equal(original, File.ReadAllBytes(first));
            Assert.False(File.Exists(outputs[1]));
        }

        [Fact]
        public void Sharing_MixedLosslessFormatsRoundTrip()
        {
            string input = Cover(PngColorType.Palette);
            var inputs = new[] { input, input, input };
            var outputs = new[] { PathFor("one.png"), PathFor("two.bmp"), PathFor("three.tiff") };
            var algorithm = new SharedCoding<Image<Rgba32>>(Algorithm(), 2);
            var pipeline = new StegoPipeline<IReadOnlyList<Image<Rgba32>>>(algorithm);
            pipeline.Embed(Payload, inputs, outputs, Key);
            Assert.Equal(Payload, pipeline.Extract(new[] { outputs[1], outputs[2] }, Key).Data);
        }

        [Fact]
        public void Sharing_NullOutputsAreRejectedBeforeWriting()
        {
            string input = Cover();
            var inputs = new[] { input, input };
            var algorithm = new SharedCoding<Image<Rgba32>>(Algorithm(), 2);
            Assert.Throws<ArgumentNullException>(() => algorithm.EmbedBytes(Payload, inputs, (IReadOnlyList<string>)null));
            string first = PathFor("first.png");
            Assert.Throws<ArgumentException>(() => algorithm.EmbedBytes(Payload, inputs, new[] { first, null }));
            Assert.False(File.Exists(first));
        }

        [Fact]
        public void StreamHelpers_PreservePayloadInFullyTransparentPixels()
        {
            using var input = new MemoryStream();
            using (var image = new Image<Rgba32>(64, 64, new Rgba32(100, 100, 100, 0)))
                image.SaveAsPng(input, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, TransparentColorMode = PngTransparentColorMode.Preserve });
            var algorithm = Algorithm();
            using var output = new MemoryStream();
            input.Position = 0;
            algorithm.EmbedBytes(Payload, input, output);
            output.Position = 0;
            Assert.Equal(Payload, algorithm.ExtractBytes(output));

            var pipeline = new StegoPipeline<Image<Rgba32>>(algorithm);
            using var sealedOutput = new MemoryStream();
            input.Position = 0;
            pipeline.Embed(Payload, input, sealedOutput, Key);
            sealedOutput.Position = 0;
            Assert.Equal(Payload, pipeline.Extract(sealedOutput, Key).Data);
        }

        [Theory]
        [InlineData(".png")]
        [InlineData(".bmp")]
        [InlineData(".tiff")]
        public void FileHelpers_PreserveRgbInFullyTransparentPixels(string extension)
        {
            string input = PathFor("transparent.png");
            using (var image = new Image<Rgba32>(64, 64, new Rgba32(100, 100, 100, 0)))
                image.SaveAsPng(input, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, TransparentColorMode = PngTransparentColorMode.Preserve });
            string output = PathFor("stego" + extension);
            var pipeline = new StegoPipeline<Image<Rgba32>>(Algorithm());
            pipeline.Embed(Payload, input, output, Key);
            Assert.Equal(Payload, pipeline.Extract(output, Key).Data);
            using var saved = Image.Load<Rgba32>(output);
            for (int y = 0; y < saved.Height; y++)
                for (int x = 0; x < saved.Width; x++)
                {
                    var pixel = saved[x, y];
                    Assert.InRange(pixel.R, 99, 101);
                    Assert.InRange(pixel.G, 99, 101);
                    Assert.InRange(pixel.B, 99, 101);
                    if (extension != ".tiff")
                        Assert.Equal(0, pixel.A);
                }
        }

        [Fact]
        public void ImageSequence_PreservesGrayscaleCoverPayload()
        {
            string path = Cover();
            var sequence = new ImageSequence(new[] { path });
            var algorithm = Algorithm();
            sequence.Modify(0, image => algorithm.EmbedBytes(Payload, image));
            Assert.Equal(Payload, sequence.Read(0, image => algorithm.ExtractBytes(image)));
            Assert.Throws<NotSupportedException>(() => new ImageSequence(new[] { PathFor("frame.unknown") }));
        }
    }
}
