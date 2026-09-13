using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class StegoPipelineTests
    {
        private static StegoPipeline<Image<Rgba32>> CreatePipeline(StegoKey key)
        {
            return new StegoPipeline<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(key)));
        }

        [Fact]
        public void EmbedAndExtract_RoundTrip()
        {
            var key = StegoKey.FromPassphrase("hunter2", iterations: 10);
            var data = new byte[300];
            new Random(3).NextBytes(data);
            using var image = new Image<Rgba32>(80, 80);

            CreatePipeline(key).Embed(data, image, key);
            var result = CreatePipeline(key).Extract(image, key);

            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);
        }

        [Fact]
        public void Extract_UntouchedImage_NotFound()
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            using var image = new Image<Rgba32>(80, 80);

            Assert.Equal(ExtractionStatus.NotFound, CreatePipeline(key).Extract(image, key).Status);
        }

        [Fact]
        public void Extract_WrongKey_NotFound()
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var wrong = StegoKey.FromBytes(new byte[] { 2 });
            using var image = new Image<Rgba32>(80, 80);
            CreatePipeline(key).Embed(new byte[] { 1, 2, 3 }, image, key);

            // The pixel order is keyed too, so a wrong key reads noise rather than an envelope.
            Assert.Equal(ExtractionStatus.NotFound, CreatePipeline(wrong).Extract(image, wrong).Status);
        }

        [Fact]
        public void Extract_WrongEnvelopeKey_AuthenticationFailed()
        {
            var positions = StegoKey.FromBytes(new byte[] { 1 });
            var key = StegoKey.FromBytes(new byte[] { 2 });
            var wrong = StegoKey.FromBytes(new byte[] { 3 });
            using var image = new Image<Rgba32>(80, 80);

            var pipeline = CreatePipeline(positions);
            pipeline.Embed(new byte[] { 1, 2, 3 }, image, key);

            Assert.Equal(ExtractionStatus.AuthenticationFailed, pipeline.Extract(image, wrong).Status);
        }

        [Fact]
        public void IsPossibleToEmbed_AccountsForEnvelopeOverhead()
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var pipeline = CreatePipeline(key);
            // 8x8 = 64 pixels = 64 bits = 8 bytes; header alone needs 4 + 5 + 28.
            using var image = new Image<Rgba32>(8, 8);

            Assert.Equal(0, pipeline.Capacity(image));
            Assert.False(pipeline.IsPossibleToEmbed(1, image));
            Assert.Throws<CapacityExceededException>(() => pipeline.Embed(new byte[] { 1 }, image, key));
        }

        [Fact]
        public void EmbedAndExtract_ThroughPngFile_RoundTrip()
        {
            var key = StegoKey.FromBytes(new byte[] { 7 });
            var data = new byte[] { 0xCA, 0xFE };
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            try
            {
                using (var image = new Image<Rgba32>(32, 32))
                {
                    CreatePipeline(key).Embed(data, image, key);
                    image.SaveAsPng(path);
                }

                using var loaded = Image.Load<Rgba32>(path);
                Assert.Equal(data, CreatePipeline(key).Extract(loaded, key).Data);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Capacity_IsAlgorithmCapacityMinusOverhead()
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var pipeline = CreatePipeline(key);
            using var image = new Image<Rgba32>(100, 100);

            Assert.Equal(pipeline.Algorithm.Capacity(image) - pipeline.Envelope.Overhead, pipeline.Capacity(image));

            var data = new byte[pipeline.Capacity(image)];
            new Random(1).NextBytes(data);
            pipeline.Embed(data, image, key);
            Assert.Equal(data, pipeline.Extract(image, key).Data);
        }

        [Fact]
        public void FileHelpers_RoundTrip()
        {
            var key = StegoKey.FromBytes(new byte[] { 3 });
            var data = new byte[] { 1, 2, 3, 4 };
            var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");
            var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".png");

            try
            {
                using (var image = new Image<Rgba32>(32, 32))
                    image.SaveAsPng(input);

                CreatePipeline(key).Embed(data, input, output, key);
                var result = CreatePipeline(key).Extract(output, key);

                Assert.Equal(ExtractionStatus.Success, result.Status);
                Assert.Equal(data, result.Data);
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        [Fact]
        public void StreamHelpers_RoundTrip()
        {
            var key = StegoKey.FromBytes(new byte[] { 4 });
            var data = new byte[] { 9, 8, 7 };

            using var input = new MemoryStream();
            using (var image = new Image<Rgba32>(32, 32))
                image.SaveAsPng(input);
            input.Position = 0;

            using var output = new MemoryStream();
            CreatePipeline(key).Embed(data, input, output, key);
            output.Position = 0;

            Assert.Equal(data, CreatePipeline(key).Extract(output, key).Data);
        }
    }
}
