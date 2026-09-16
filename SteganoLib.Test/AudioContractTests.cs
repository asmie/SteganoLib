#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class AudioContractTests
    {
        [Theory]
        [InlineData(-1, 2, "frame")]
        [InlineData(int.MinValue, 0, "frame")]
        [InlineData(2, -1, "frame")]
        [InlineData(int.MaxValue, 2, "frame")]
        [InlineData(0, -1, "channel")]
        [InlineData(1, -1, "channel")]
        [InlineData(0, 2, "channel")]
        [InlineData(0, int.MinValue, "channel")]
        [InlineData(0, int.MaxValue, "channel")]
        public void Sample_RejectsInvalidCoordinatesWithoutAliasing(int frame, int channel, string parameter)
        {
            var audio = new PcmAudio(8000, 2, 16, new[] { 10, 20, 30, 40 });

            var error = Assert.Throws<ArgumentOutOfRangeException>(() => audio.Sample(frame, channel));

            Assert.Equal(parameter, error.ParamName);
        }

        [Fact]
        public void Sample_RejectsAccessToEmptyAudio()
        {
            var audio = new PcmAudio(8000, 1, 16, Array.Empty<int>());
            Assert.Equal("frame", Assert.Throws<ArgumentOutOfRangeException>(() => audio.Sample(0, 0)).ParamName);
        }

        [Fact]
        public void Sample_ReadsValidCoordinatesAndRetainsConstructorBufferSharing()
        {
            int[] samples = { 10, 20, 30, 40 };
            var audio = new PcmAudio(8000, 2, 16, samples);
            Assert.Equal(10, audio.Sample(0, 0));
            Assert.Equal(20, audio.Sample(0, 1));
            Assert.Equal(30, audio.Sample(1, 0));
            Assert.Equal(40, audio.Sample(1, 1));
            samples[3] = 99;
            Assert.Equal(99, audio.Sample(1, 1));
        }

        [Theory]
        [InlineData(8, -129)]
        [InlineData(8, 128)]
        [InlineData(16, -32769)]
        [InlineData(16, 32768)]
        [InlineData(24, -8388609)]
        [InlineData(24, 8388608)]
        public void Save_RejectsSamplesMutatedBeyondBitDepthAndPreservesOutput(int bits, int sample)
        {
            var audio = new PcmAudio(8000, 1, bits, new[] { 0, 0 });
            audio.Samples[1] = sample;
            using var output = new MemoryStream(new byte[] { 1, 2, 3, 4 });
            output.Position = 2;

            Assert.Throws<InvalidOperationException>(() => audio.ToArray());
            Assert.Throws<InvalidOperationException>(() => audio.Save(output));

            Assert.Equal(2, output.Position);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.ToArray());
            Assert.Equal(sample, audio.Samples[1]);
            audio.Samples[1] = audio.MaxValue;
            Assert.Equal(audio.Samples, PcmAudio.Load(audio.ToArray()).Samples);
        }

        [Theory]
        [InlineData(1, 65536, 8)]
        [InlineData(1, 32768, 16)]
        [InlineData(1, 21846, 24)]
        [InlineData(1, 16384, 32)]
        [InlineData(int.MaxValue, 2, 16)]
        [InlineData(int.MaxValue, int.MaxValue, 32)]
        public void Save_RejectsUnrepresentableFormatWithoutWriting(int sampleRate, int channels, int bits)
        {
            var audio = new PcmAudio(sampleRate, channels, bits, Array.Empty<int>());
            using var output = new MemoryStream();
            output.WriteByte(42);

            Assert.Throws<InvalidOperationException>(() => audio.ToArray());
            Assert.Throws<InvalidOperationException>(() => audio.Save(output));

            Assert.Equal(1, output.Position);
            Assert.Equal(new byte[] { 42 }, output.ToArray());
        }

        [Theory]
        [InlineData(int.MaxValue, 2, 8)]
        [InlineData(1, 65535, 8)]
        [InlineData(1, 32767, 16)]
        [InlineData(1, 21845, 24)]
        [InlineData(1, 16383, 32)]
        public void Save_ValidBoundaryFormatsRoundTrip(int sampleRate, int channels, int bits)
        {
            var audio = new PcmAudio(sampleRate, channels, bits, Array.Empty<int>());

            var bytes = audio.ToArray();
            var loaded = PcmAudio.Load(bytes);

            Assert.Equal((long)sampleRate * channels * (bits / 8), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)));
            Assert.Equal(sampleRate, loaded.SampleRate);
            Assert.Equal(channels, loaded.Channels);
            Assert.Equal(bits, loaded.BitsPerSample);
            Assert.Empty(loaded.Samples);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Save_InvalidStateDoesNotOverwriteExistingFile(bool invalidFormat)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".wav");
            byte[] original = { 1, 2, 3, 4 };
            var audio = invalidFormat
                ? new PcmAudio(1, 65536, 8, Array.Empty<int>())
                : new PcmAudio(8000, 1, 8, new[] { 128 });
            try
            {
                File.WriteAllBytes(path, original);

                Assert.Throws<InvalidOperationException>(() => audio.Save(path));

                Assert.Equal(original, File.ReadAllBytes(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData(0, "data")]
        [InlineData(1, "data")]
        [InlineData(2, "key")]
        [InlineData(3, "key")]
        public void StreamHelpers_RejectNullPayloadOrKeyWithoutConsumingInput(int operation, string parameter)
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var algorithm = new AudioLsb(new KeyedSampleSelector(key));
            var pipeline = new StegoPipeline<PcmAudio>(algorithm);
            using var input = new MemoryStream(new PcmAudio(8000, 1, 16, new int[1024]).ToArray());
            using var output = new MemoryStream(new byte[] { 1, 2, 3 });
            input.Position = 5;
            output.Position = 1;

            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                switch (operation)
                {
                    case 0: algorithm.EmbedBytes(null!, input, output); break;
                    case 1: pipeline.Embed(null!, input, output, key); break;
                    case 2: pipeline.Embed(Array.Empty<byte>(), input, output, null!); break;
                    case 3: pipeline.Extract(input, null!); break;
                }
            });

            Assert.Equal(parameter, error.ParamName);
            Assert.Equal(5, input.Position);
            Assert.Equal(1, output.Position);
            Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
        }

        [Theory]
        [InlineData(0, "data")]
        [InlineData(1, "data")]
        [InlineData(2, "key")]
        [InlineData(3, "key")]
        public void FileHelpers_RejectNullPayloadOrKeyBeforeOpeningInput(int operation, string parameter)
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var algorithm = new AudioLsb(new KeyedSampleSelector(key));
            var pipeline = new StegoPipeline<PcmAudio>(algorithm);
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string input = Path.Combine(directory, "missing.wav");
            string output = Path.Combine(directory, "output.wav");

            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                switch (operation)
                {
                    case 0: algorithm.EmbedBytes(null!, input, output); break;
                    case 1: pipeline.Embed(null!, input, output, key); break;
                    case 2: pipeline.Embed(Array.Empty<byte>(), input, output, null!); break;
                    case 3: pipeline.Extract(input, null!); break;
                }
            });

            Assert.Equal(parameter, error.ParamName);
            Assert.False(Directory.Exists(directory));
        }
    }
}
