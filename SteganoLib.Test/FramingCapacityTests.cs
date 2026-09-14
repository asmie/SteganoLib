using System;
using System.Collections.Generic;
using System.IO;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using SteganoLib.Selection;
using SteganoLib.Sharing;
using SteganoLib.Video;
using Xunit;

namespace SteganoLib.Test
{
    public class FramingCapacityTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 17, 31, 47 });

        private static StegoPipeline<Image<Rgba32>> Pipeline(int parity, bool hmac, bool compress = false)
        {
            IStegAlgorithm<Image<Rgba32>> algorithm = new LSB(new KeyedPermutationSelector(Key));
            if (parity > 0)
                algorithm = new ErrorCorrectedAlgorithm<Image<Rgba32>>(algorithm, new ReedSolomonCode(parity));
            IPayloadCodec codec = hmac ? new HmacPayloadCodec() : new AesGcmPayloadCodec();
            return new StegoPipeline<Image<Rgba32>>(algorithm, new PayloadEnvelope(codec) { Compress = compress });
        }

        private static byte[] Pixels(Image<Rgba32> image)
        {
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            return pixels;
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(32, false)]
        [InlineData(0, true)]
        [InlineData(32, true)]
        public void Compression_EmbedsPayloadLargerThanUncompressedBudget(int parity, bool hmac)
        {
            using var image = new Image<Rgba32>(64, 64, new Rgba32(100, 100, 100));
            var pipeline = Pipeline(parity, hmac, compress: true);
            var payload = new byte[1000];
            Assert.True(payload.Length > pipeline.Capacity(image));
            Assert.False(pipeline.IsPossibleToEmbed(payload.Length, image)); // length-only, without compression
            Assert.True(pipeline.Envelope.Seal(payload, Key).Length <= pipeline.Algorithm.Capacity(image));

            pipeline.Embed(payload, image, Key);
            using var saved = new MemoryStream();
            image.SaveAsPng(saved);
            saved.Position = 0;
            using var loaded = Image.Load<Rgba32>(saved);
            var recovered = pipeline.Extract(loaded, Key);
            Assert.True(recovered.IsSuccess);
            Assert.Equal(payload, recovered.Data);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(32)]
        public void OversizedSealedPayload_IsRejectedWithoutChangingCarrier(int parity)
        {
            using var image = new Image<Rgba32>(64, 64, new Rgba32(100, 100, 100));
            var original = Pixels(image);
            var pipeline = Pipeline(parity, hmac: false, compress: true);
            var payload = new byte[2000];
            new Random(123).NextBytes(payload);
            int sealedLength = pipeline.Envelope.Seal(payload, Key).Length;
            Assert.True(sealedLength > pipeline.Algorithm.Capacity(image));
            var error = Assert.Throws<CapacityExceededException>(() => pipeline.Embed(payload, image, Key));
            Assert.Equal(sealedLength, error.Required);
            Assert.Equal(pipeline.Algorithm.Capacity(image), error.Available);
            Assert.Equal(original, Pixels(image));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(32, false)]
        [InlineData(0, true)]
        [InlineData(32, true)]
        public void EmptyEnvelope_RequiresItsEntireFraming(int parity, bool hmac)
        {
            var pipeline = Pipeline(parity, hmac);
            long envelopeLength = pipeline.Envelope.Overhead;
            long encodedLength = parity == 0 ? envelopeLength : new ReedSolomonCode(parity).EncodedLength(envelopeLength);
            int width = (int)(6 + encodedLength) * 8;
            using var exact = new Image<Rgba32>(width, 1);
            using var shortImage = new Image<Rgba32>(width - 1, 1);
            var original = Pixels(shortImage);

            Assert.Equal(0, pipeline.Capacity(exact));
            Assert.Equal(0, pipeline.Capacity(shortImage));
            Assert.True(pipeline.IsPossibleToEmbed(0, exact));
            Assert.False(pipeline.IsPossibleToEmbed(1, exact));
            Assert.False(pipeline.IsPossibleToEmbed(0, shortImage));
            Assert.Throws<CapacityExceededException>(() => pipeline.Embed(Array.Empty<byte>(), shortImage, Key));
            Assert.Equal(original, Pixels(shortImage));

            pipeline.Embed(Array.Empty<byte>(), exact, Key);
            var result = pipeline.Extract(exact, Key);
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Data);
        }

        [Fact]
        public void Pipeline_LengthBoundsAndNullKeyDoNotModifyCarrier()
        {
            using var image = new Image<Rgba32>(64, 64);
            var original = Pixels(image);
            var pipeline = Pipeline(32, hmac: false);
            Assert.False(pipeline.IsPossibleToEmbed(-1, image));
            Assert.False(pipeline.IsPossibleToEmbed(long.MaxValue, image));
            Assert.False(pipeline.IsPossibleToEmbed(long.MaxValue - pipeline.Envelope.Overhead, image));
            Assert.Throws<ArgumentNullException>(() => pipeline.IsPossibleToEmbed(0, null));
            Assert.Throws<ArgumentNullException>(() => pipeline.Embed(new byte[1000], image, null));
            Assert.Equal(original, Pixels(image));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SharedEmptyPayload_ValidatesEveryShareHeaderBeforeMutation(bool ecc)
        {
            using var large = new Image<Rgba32>(32, 32);
            using var tiny = new Image<Rgba32>(1, 1);
            var original = Pixels(large);
            IStegAlgorithm<IReadOnlyList<Image<Rgba32>>> algorithm = new SharedCoding<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(Key)), 2);
            if (ecc)
                algorithm = new ErrorCorrectedAlgorithm<IReadOnlyList<Image<Rgba32>>>(algorithm);
            var carriers = new[] { large, tiny };
            Assert.Equal(0, algorithm.Capacity(carriers));
            Assert.False(algorithm.IsPossibleToEmbed(0, carriers));
            Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(Array.Empty<byte>(), carriers));
            Assert.Equal(original, Pixels(large));
        }

        [Fact]
        public void SharedPayload_RequiresEnoughCarriersEvenWhenLengthFits()
        {
            using var image = new Image<Rgba32>(64, 64);
            var algorithm = new SharedCoding<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(Key)), 2);
            var carriers = new[] { image };
            Assert.Equal(0, algorithm.Capacity(carriers));
            Assert.False(algorithm.IsPossibleToEmbed(0, carriers));
            Assert.False(algorithm.IsPossibleToEmbed(1, carriers));
            Assert.False(new StegoPipeline<IReadOnlyList<Image<Rgba32>>>(algorithm).IsPossibleToEmbed(0, carriers));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void VideoEmptyPayload_RequiresLengthHeaderEvenThroughEcc(bool ecc)
        {
            using var tiny = new Image<Rgba32>(1, 1);
            var frames = new FrameList<Image<Rgba32>>(new[] { tiny });
            IStegAlgorithm<IFrameSequence<Image<Rgba32>>> algorithm = new VideoCoding<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(Key)));
            if (ecc)
                algorithm = new ErrorCorrectedAlgorithm<IFrameSequence<Image<Rgba32>>>(algorithm);
            Assert.Equal(0, algorithm.Capacity(frames));
            Assert.False(algorithm.IsPossibleToEmbed(0, frames));
            Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(Array.Empty<byte>(), frames));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void VideoEmptyPayload_ExactlyFitsLengthHeader(bool spread)
        {
            using var image = new Image<Rgba32>(80, 1); // four payload bytes after the six-byte LSB header
            var frames = new FrameList<Image<Rgba32>>(new[] { image });
            var algorithm = new VideoCoding<Image<Rgba32>>(new LSB(new KeyedPermutationSelector(Key))) { Spread = spread };
            Assert.Equal(0, algorithm.Capacity(frames));
            Assert.True(algorithm.IsPossibleToEmbed(0, frames));
            Assert.False(algorithm.IsPossibleToEmbed(1, frames));
            Assert.False(algorithm.IsPossibleToEmbed(long.MaxValue, frames));
            algorithm.EmbedBytes(Array.Empty<byte>(), frames);
            Assert.Empty(algorithm.ExtractBytes(frames));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Video_PreflightsFrameConstraintsBeforeAnyMutation(bool spread)
        {
            var first = new TestFrame();
            var last = new TestFrame { Reject = true };
            var frames = new FrameList<TestFrame>(new[] { first, last });
            var algorithm = new VideoCoding<TestFrame>(new FrameAlgorithm()) { Spread = spread };
            var payload = new byte[8]; // needs both eight-byte frames, including video framing
            Assert.True(payload.Length <= algorithm.Capacity(frames));
            Assert.False(algorithm.IsPossibleToEmbed(payload.Length, frames));
            Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(payload, frames));
            Assert.False(first.Modified);
            Assert.False(last.Modified);
        }

        private sealed class TestFrame
        {
            public bool Reject;
            public bool Modified;
        }

        private sealed class FrameAlgorithm : IStegAlgorithm<TestFrame>
        {
            public long Capacity(TestFrame frame) => 8;
            public bool IsPossibleToEmbed(long length, TestFrame frame) => !frame.Reject && length >= 0 && length <= Capacity(frame);
            public void EmbedBytes(byte[] data, TestFrame frame)
            {
                if (!IsPossibleToEmbed(data.Length, frame))
                    throw new CapacityExceededException(data.Length, Capacity(frame));
                frame.Modified = true;
            }
            public byte[] ExtractBytes(TestFrame frame) => Array.Empty<byte>();
        }
    }
}
