using System;
using System.IO;
using System.Linq;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class AudioLsbTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0xA1 });

        private static AudioLsb Create(int bits = 1) => new(new KeyedSampleSelector(Key)) { BitsPerSample = bits };

        [Theory]
        [InlineData(1, 0)]
        [InlineData(1, 1)]
        [InlineData(1, 900)]
        [InlineData(2, 900)]
        [InlineData(4, 3000)]
        public void RoundTrip_ThroughWavBytes(int bits, int length)
        {
            var audio = PcmAudioTests.Synthetic(8000, 2, 16);
            var data = new byte[length];
            new Random(length).NextBytes(data);

            Create(bits).EmbedBytes(data, audio);
            var loaded = PcmAudio.Load(audio.ToArray());

            Assert.Equal(data, Create(bits).ExtractBytes(loaded));
        }

        [Theory]
        [InlineData(8)]
        [InlineData(24)]
        [InlineData(32)]
        public void RoundTrip_OtherDepths(int depth)
        {
            var audio = PcmAudioTests.Synthetic(4000, 1, depth);
            var data = new byte[200];
            new Random(depth).NextBytes(data);

            Create().EmbedBytes(data, audio);

            Assert.Equal(data, Create().ExtractBytes(PcmAudio.Load(audio.ToArray())));
        }

        [Fact]
        public void OneBit_ChangesSamplesByAtMostOne_InBothDirections()
        {
            var cover = PcmAudioTests.Synthetic(8000, 2, 16);
            var stego = cover.Clone();
            var data = new byte[1500];
            new Random(2).NextBytes(data);

            Create().EmbedBytes(data, stego);

            int up = 0, down = 0;
            for (int i = 0; i < cover.Samples.Length; i++)
            {
                int diff = stego.Samples[i] - cover.Samples[i];
                Assert.InRange(diff, -1, 1);
                if (diff > 0) up++;
                if (diff < 0) down++;
            }
            Assert.True(up > 100 && down > 100, $"up={up} down={down}");
        }

        [Fact]
        public void OneBit_StaysInsideRangeAtExtremes()
        {
            var audio = PcmAudioTests.Synthetic(4000, 1, 8);
            for (int i = 0; i < audio.Samples.Length; i++)
                audio.Samples[i] = i % 2 == 0 ? audio.MinValue : audio.MaxValue;
            var data = new byte[300];
            new Random(3).NextBytes(data);

            Create().EmbedBytes(data, audio);

            Assert.All(audio.Samples, s => Assert.InRange(s, audio.MinValue, audio.MaxValue));
            Assert.Equal(data, Create().ExtractBytes(audio));
        }

        [Fact]
        public void Capacity_IsExact()
        {
            var audio = PcmAudioTests.Synthetic(1000, 2, 16);
            var lsb = Create(2);

            Assert.Equal(2000 * 2 / 8 - 6, lsb.Capacity(audio));
            var data = new byte[lsb.Capacity(audio)];
            new Random(4).NextBytes(data);
            lsb.EmbedBytes(data, audio);
            Assert.Equal(data, lsb.ExtractBytes(audio));
            Assert.Throws<CapacityExceededException>(() => lsb.EmbedBytes(new byte[data.Length + 1], audio));
        }

        [Fact]
        public void Trellis_RoundTripAndAvoidsSilence()
        {
            var cover = PcmAudioTests.Synthetic(16000, 1, 16);
            for (int i = 0; i < 8000; i++)
                cover.Samples[i] = 0; // first half silent
            var stego = cover.Clone();
            var lsb = Create();
            lsb.TrellisCoder = new SyndromeTrellisCoder(7);
            var data = new byte[200];
            new Random(5).NextBytes(data);

            lsb.EmbedBytes(data, stego);

            Assert.Equal(data, Create().ExtractBytes(stego));
            int silentChanges = Enumerable.Range(0, 8000).Count(i => cover.Samples[i] != stego.Samples[i]);
            int loudChanges = Enumerable.Range(8000, 8000).Count(i => cover.Samples[i] != stego.Samples[i]);
            Assert.True(silentChanges <= 48, $"silent={silentChanges}");
            Assert.True(loudChanges > 100, $"loud={loudChanges}");
        }

        [Fact]
        public void Pipeline_AndFileHelpers()
        {
            var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
            var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
            try
            {
                PcmAudioTests.Synthetic(6000, 2, 16).Save(input);
                var data = new byte[120];
                new Random(6).NextBytes(data);

                var pipeline = new StegoPipeline<PcmAudio>(Create());
                pipeline.Embed(data, input, output, Key);
                var result = pipeline.Extract(output, Key);
                Assert.Equal(ExtractionStatus.Success, result.Status);
                Assert.Equal(data, result.Data);

                using var inStream = File.OpenRead(input);
                using var outStream = new MemoryStream();
                pipeline.Embed(data, inStream, outStream, Key);
                outStream.Position = 0;
                Assert.Equal(data, pipeline.Extract(outStream, Key).Data);

                Create().EmbedBytes(data, input, output);
                Assert.Equal(data, Create().ExtractBytes(output));
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        [Fact]
        public void KeyedSampleSelector_IsABijection()
        {
            var selector = new KeyedSampleSelector(Key);
            var indices = selector.Indices(1234).ToList();

            Assert.Equal(1234, indices.Count);
            Assert.Equal(1234, indices.Distinct().Count());
            Assert.All(indices, i => Assert.InRange(i, 0, 1233));
            Assert.Empty(selector.Indices(0));
            Assert.Equal(indices, new KeyedSampleSelector(Key).Indices(1234));
        }

        [Fact]
        public void Validation()
        {
            Assert.Throws<ArgumentNullException>(() => new AudioLsb(null));
            Assert.Throws<ArgumentNullException>(() => new KeyedSampleSelector(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Create().BitsPerSample = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => Create().BitsPerSample = 5);
            Assert.Throws<ArgumentNullException>(() => Create().CostModel = null);
            Assert.Throws<ArgumentOutOfRangeException>(() => new AmplitudeCostModel().Smoothing = 0);
            Assert.Throws<ArgumentNullException>(() => Create().EmbedBytes(null, PcmAudioTests.Synthetic(10)));
            Assert.Throws<ArgumentNullException>(() => Create().ExtractBytes(null));
        }
    }
}
