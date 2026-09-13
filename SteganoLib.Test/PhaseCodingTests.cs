using System;
using System.Linq;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using Xunit;

namespace SteganoLib.Test
{
    public class PhaseCodingTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0xF0 });

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(500)]
        [InlineData(4000)]
        public void RoundTrip_ThroughWavBytes(int length)
        {
            var audio = PcmAudioTests.Synthetic(70000, 2, 16);
            var data = new byte[length];
            new Random(length).NextBytes(data);

            new PhaseCoding(Key).EmbedBytes(data, audio);
            var loaded = PcmAudio.Load(audio.ToArray());

            Assert.Equal(data, new PhaseCoding(Key).ExtractBytes(loaded));
        }

        [Theory]
        [InlineData(8)]
        [InlineData(24)]
        public void RoundTrip_OtherDepths(int depth)
        {
            var audio = PcmAudioTests.Synthetic(40000, 1, depth);
            var data = new byte[300];
            new Random(depth).NextBytes(data);

            new PhaseCoding(Key).EmbedBytes(data, audio);

            Assert.Equal(data, new PhaseCoding(Key).ExtractBytes(PcmAudio.Load(audio.ToArray())));
        }

        [Fact]
        public void Capacity_UsesLargestPowerOfTwo()
        {
            var coder = new PhaseCoding(Key);
            Assert.Equal((65536 / 2 - 1) / 8 - 4, coder.Capacity(PcmAudioTests.Synthetic(70000)));
            Assert.Equal((1024 / 2 - 1) / 8 - 4, coder.Capacity(PcmAudioTests.Synthetic(1500)));
            Assert.Equal(0, coder.Capacity(PcmAudioTests.Synthetic(50)));
        }

        [Fact]
        public void Capacity_IsReachable()
        {
            var audio = PcmAudioTests.Synthetic(40000, 2, 16);
            var coder = new PhaseCoding(Key);
            var data = new byte[coder.Capacity(audio)];
            new Random(9).NextBytes(data);

            coder.EmbedBytes(data, audio);

            Assert.Equal(data, coder.ExtractBytes(audio));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[data.Length + 1], audio));
        }

        [Fact]
        public void MultipleSegments_RoundTripAndSecondChannelUntouched()
        {
            var cover = PcmAudioTests.Synthetic(30000, 2, 16);
            var stego = cover.Clone();
            var coder = new PhaseCoding(Key) { SegmentLength = 4096 };
            var data = new byte[coder.Capacity(stego)];
            new Random(10).NextBytes(data);

            coder.EmbedBytes(data, stego);

            Assert.Equal(data, new PhaseCoding(Key) { SegmentLength = 4096 }.ExtractBytes(stego));
            for (int f = 0; f < cover.FrameCount; f++)
                Assert.Equal(cover.Sample(f, 1), stego.Sample(f, 1));
            // Samples beyond the last full segment are untouched.
            for (int f = 7 * 4096; f < cover.FrameCount; f++)
                Assert.Equal(cover.Sample(f, 0), stego.Sample(f, 0));
        }

        [Fact]
        public void MagnitudeSpectrum_IsPreserved()
        {
            // Phase coding changes the waveform but not what the ear hears: the magnitude of every bin.
            var cover = PcmAudioTests.Synthetic(65536, 1, 16);
            var stego = cover.Clone();
            var data = new byte[1000];
            new Random(11).NextBytes(data);

            new PhaseCoding(Key).EmbedBytes(data, stego);

            var before = cover.Samples.Select(v => new System.Numerics.Complex(v, 0)).ToArray();
            var after = stego.Samples.Select(v => new System.Numerics.Complex(v, 0)).ToArray();
            Fft.Forward(before);
            Fft.Forward(after);

            double roundingNoise = Math.Sqrt(65536 / 12.0);
            int compared = 0;
            for (int bin = 1; bin < 32768; bin++)
            {
                if (before[bin].Magnitude < 200 * roundingNoise)
                    continue;
                compared++;
                double relative = Math.Abs(after[bin].Magnitude - before[bin].Magnitude) / before[bin].Magnitude;
                Assert.True(relative < 0.02, $"bin {bin}: {before[bin].Magnitude:F0} -> {after[bin].Magnitude:F0}");
            }
            Assert.True(compared > 100, $"compared {compared} bins");
        }

        [Fact]
        public void SurvivesGainChange()
        {
            var audio = PcmAudioTests.Synthetic(40000, 1, 16);
            var data = new byte[200];
            new Random(12).NextBytes(data);
            new PhaseCoding(Key).EmbedBytes(data, audio);

            for (int i = 0; i < audio.Samples.Length; i++)
                audio.Samples[i] = (int)Math.Round(audio.Samples[i] * 0.7);

            Assert.Equal(data, new PhaseCoding(Key).ExtractBytes(audio));
        }

        [Fact]
        public void WrongKey_DoesNotRecover()
        {
            var audio = PcmAudioTests.Synthetic(40000, 1, 16);
            var data = new byte[100];
            new Random(13).NextBytes(data);
            new PhaseCoding(Key).EmbedBytes(data, audio);

            Assert.NotEqual(data, new PhaseCoding(StegoKey.FromBytes(new byte[] { 1 })).ExtractBytes(audio));
        }

        [Fact]
        public void Pipeline_RoundTrip()
        {
            var audio = PcmAudioTests.Synthetic(40000, 2, 16);
            var pipeline = new StegoPipeline<PcmAudio>(new PhaseCoding(Key));
            var data = new byte[150];
            new Random(14).NextBytes(data);

            pipeline.Embed(data, audio, Key);

            var result = pipeline.Extract(PcmAudio.Load(audio.ToArray()), Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);
            Assert.Equal(ExtractionStatus.NotFound, pipeline.Extract(PcmAudioTests.Synthetic(40000, 2, 16), Key).Status);
        }

        [Fact]
        public void Validation()
        {
            Assert.Throws<ArgumentNullException>(() => new PhaseCoding(null));
            var coder = new PhaseCoding(Key);
            Assert.Throws<ArgumentOutOfRangeException>(() => coder.SegmentLength = 1000);
            Assert.Throws<ArgumentOutOfRangeException>(() => coder.SegmentLength = 32);
            Assert.Throws<ArgumentOutOfRangeException>(() => coder.MagnitudeFloorFactor = 0.5);
            coder.SegmentLength = 256;
            Assert.Equal(256, coder.SegmentLength);
            Assert.Empty(coder.ExtractBytes(PcmAudioTests.Synthetic(50)));
        }
    }
}
