using System;
using System.IO;
using System.Linq;
using System.Text;
using SteganoLib.Audio;
using Xunit;

namespace SteganoLib.Test
{
    public class PcmAudioTests
    {
        internal static PcmAudio Synthetic(int frames = 20000, int channels = 2, int bits = 16, int sampleRate = 44100, int seed = 1)
        {
            var random = new Random(seed);
            var samples = new int[frames * channels];
            double scale = (1 << (bits - 1)) * 0.6;
            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / sampleRate;
                double tone = Math.Sin(2 * Math.PI * 440 * t) * 0.5 + Math.Sin(2 * Math.PI * 1234.5 * t) * 0.3;
                for (int c = 0; c < channels; c++)
                {
                    double v = (tone + (random.NextDouble() - 0.5) * 0.2) * scale;
                    samples[f * channels + c] = (int)Math.Round(v);
                }
            }
            return new PcmAudio(sampleRate, channels, bits, samples);
        }

        [Theory]
        [InlineData(8, 1)]
        [InlineData(16, 2)]
        [InlineData(24, 2)]
        [InlineData(32, 1)]
        [InlineData(16, 6)]
        public void SaveAndLoad_RoundTrip(int bits, int channels)
        {
            var audio = Synthetic(1001, channels, bits);
            if (bits == 32)
            {
                audio.Samples[0] = int.MinValue;
                audio.Samples[1] = int.MaxValue;
            }
            else
            {
                audio.Samples[0] = audio.MinValue;
                audio.Samples[1] = audio.MaxValue;
            }

            var loaded = PcmAudio.Load(audio.ToArray());

            Assert.Equal(audio.SampleRate, loaded.SampleRate);
            Assert.Equal(audio.Channels, loaded.Channels);
            Assert.Equal(audio.BitsPerSample, loaded.BitsPerSample);
            Assert.Equal(audio.Samples, loaded.Samples);
        }

        [Fact]
        public void Wav_HeaderFieldsAreCorrect()
        {
            var bytes = Synthetic(100, 2, 16, 8000).ToArray();

            Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(bytes.Length - 8, BitConverter.ToInt32(bytes, 4));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));
            Assert.Equal(1, BitConverter.ToInt16(bytes, 20));      // PCM
            Assert.Equal(2, BitConverter.ToInt16(bytes, 22));      // channels
            Assert.Equal(8000, BitConverter.ToInt32(bytes, 24));   // sample rate
            Assert.Equal(32000, BitConverter.ToInt32(bytes, 28));  // byte rate
            Assert.Equal(4, BitConverter.ToInt16(bytes, 32));      // block align
            Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
            Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
            Assert.Equal(400, BitConverter.ToInt32(bytes, 40));
        }

        [Fact]
        public void ExtraChunks_ArePreserved()
        {
            var original = Synthetic(50, 1, 16).ToArray();
            var list = Encoding.ASCII.GetBytes("INFOISFT\x05\0\0\0test\0\0"); // odd-sized nested payload, padded
            using var stream = new MemoryStream();
            stream.Write(original, 0, 36);
            stream.Write(Encoding.ASCII.GetBytes("LIST"));
            stream.Write(BitConverter.GetBytes(list.Length));
            stream.Write(list);
            stream.Write(original, 36, original.Length - 36);

            var audio = PcmAudio.Load(stream.ToArray());
            Assert.Single(audio.ExtraChunks);
            Assert.Equal("LIST", audio.ExtraChunks[0].Id);

            var reloaded = PcmAudio.Load(audio.ToArray());
            Assert.Equal("LIST", reloaded.ExtraChunks.Single().Id);
            Assert.Equal(list, reloaded.ExtraChunks[0].Payload);
            Assert.Equal(audio.Samples, reloaded.Samples);
        }

        [Fact]
        public void Extensible_PcmIsAccepted()
        {
            var original = Synthetic(50, 2, 24).ToArray();
            using var stream = new MemoryStream();
            stream.Write(original, 0, 12);
            stream.Write(Encoding.ASCII.GetBytes("fmt "));
            stream.Write(BitConverter.GetBytes(40));
            stream.Write(BitConverter.GetBytes((ushort)0xFFFE));
            stream.Write(original, 22, 14); // channels .. bits
            stream.Write(BitConverter.GetBytes((ushort)22)); // cbSize
            stream.Write(BitConverter.GetBytes((ushort)24)); // valid bits
            stream.Write(BitConverter.GetBytes(3));          // channel mask
            stream.Write(BitConverter.GetBytes((ushort)1));  // sub format: PCM
            stream.Write(new byte[14]);
            stream.Write(original, 36, original.Length - 36);

            var audio = PcmAudio.Load(stream.ToArray());
            Assert.Equal(24, audio.BitsPerSample);
            Assert.Equal(PcmAudio.Load(original).Samples, audio.Samples);
        }

        [Fact]
        public void FloatFormat_IsRejected()
        {
            var bytes = Synthetic(50, 1, 32).ToArray();
            bytes[20] = 3; // IEEE float
            Assert.Throws<NotSupportedException>(() => PcmAudio.Load(bytes));
        }

        [Fact]
        public void NotWave_IsRejected()
        {
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(new byte[] { 1, 2, 3 }));
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(Encoding.ASCII.GetBytes("RIFF\0\0\0\0AVI LIST")));
        }

        [Fact]
        public void Constructor_Validation()
        {
            Assert.Throws<NotSupportedException>(() => new PcmAudio(44100, 1, 12, new int[4]));
            Assert.Throws<ArgumentException>(() => new PcmAudio(44100, 2, 16, new int[3]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PcmAudio(0, 2, 16, new int[4]));
            Assert.Throws<ArgumentNullException>(() => new PcmAudio(44100, 2, 16, null));
        }

        [Fact]
        public void Properties()
        {
            var audio = Synthetic(44100, 2, 16);
            Assert.Equal(44100, audio.FrameCount);
            Assert.Equal(TimeSpan.FromSeconds(1), audio.Duration);
            Assert.Equal(-32768, audio.MinValue);
            Assert.Equal(32767, audio.MaxValue);
            Assert.Equal(audio.Samples[3], audio.Sample(1, 1));

            var clone = audio.Clone();
            clone.Samples[0] = 42;
            Assert.NotEqual(42, audio.Samples[0]);
        }

        [Fact]
        public void FileRoundTrip()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
            try
            {
                var audio = Synthetic(300, 2, 16);
                audio.Save(path);
                Assert.Equal(audio.Samples, PcmAudio.Load(path).Samples);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
