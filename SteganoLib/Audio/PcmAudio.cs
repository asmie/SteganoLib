using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SteganoLib.Containers;

namespace SteganoLib.Audio
{
    /// <summary>
    /// Uncompressed PCM audio. Samples are interleaved by frame and stored as signed
    /// integers regardless of bit depth (8-bit WAV data is converted from unsigned).
    /// Reads and writes RIFF WAVE at 8, 16, 24 and 32 bits.
    /// </summary>
    public sealed class PcmAudio
    {
        private static readonly int[] SupportedDepths = { 8, 16, 24, 32 };

        public PcmAudio(int sampleRate, int channels, int bitsPerSample, int[] samples)
            : this(sampleRate, channels, bitsPerSample, samples, new List<RiffChunk>())
        {
        }

        internal PcmAudio(int sampleRate, int channels, int bitsPerSample, int[] samples, List<RiffChunk> extraChunks)
        {
            if (sampleRate < 1)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels < 1)
                throw new ArgumentOutOfRangeException(nameof(channels));
            if (Array.IndexOf(SupportedDepths, bitsPerSample) < 0)
                throw new NotSupportedException($"{bitsPerSample}-bit PCM is not supported.");
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (samples.Length % channels != 0)
                throw new ArgumentException("Sample count must be a multiple of the channel count.", nameof(samples));

            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            Samples = samples;
            ExtraChunks = extraChunks.AsReadOnly();
            MinValue = bitsPerSample == 32 ? int.MinValue : -(1 << (bitsPerSample - 1));
            MaxValue = bitsPerSample == 32 ? int.MaxValue : (1 << (bitsPerSample - 1)) - 1;
        }

        public int SampleRate { get; }

        public int Channels { get; }

        public int BitsPerSample { get; }

        /// <summary>Mutable interleaved samples, shared with the constructor's input array. Use Clone for independent samples and metadata.</summary>
        public int[] Samples { get; }

        public int FrameCount => Samples.Length / Channels;

        public int MinValue { get; }

        public int MaxValue { get; }

        public TimeSpan Duration => TimeSpan.FromSeconds((double)FrameCount / SampleRate);

        /// <summary>Read-only collection of chunks written before data. Chunk payload arrays remain editable.</summary>
        public IReadOnlyList<RiffChunk> ExtraChunks { get; }

        public int Sample(int frame, int channel) => Samples[frame * Channels + channel];

        /// <exception cref="NotSupportedException">Float, compressed or unsupported sample precision.</exception>
        /// <exception cref="InvalidDataException">Invalid RIFF framing, PCM format fields or incomplete sample frames.</exception>
        public static PcmAudio Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Load(File.ReadAllBytes(path));
        }

        public static PcmAudio Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Load(buffer.ToArray());
        }

        public static PcmAudio Load(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return WaveReader.Read(data);
        }

        public void Save(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, ToArray());
        }

        public void Save(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var bytes = ToArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        public byte[] ToArray() => WaveWriter.Write(this);

        /// <summary>Copies samples and metadata payloads so edits to the clone do not affect this audio.</summary>
        public PcmAudio Clone()
        {
            var chunks = new List<RiffChunk>(ExtraChunks.Count);
            foreach (var chunk in ExtraChunks)
                chunks.Add(new RiffChunk(chunk.Id, (byte[])chunk.Payload.Clone()));
            return new PcmAudio(SampleRate, Channels, BitsPerSample, (int[])Samples.Clone(), chunks);
        }

        private static class WaveReader
        {
            private const ushort FormatPcm = 1;
            private const ushort FormatExtensible = 0xFFFE;

            public static PcmAudio Read(byte[] data)
            {
                int end = RiffReader.ContainerEnd(data, "WAVE");
                int channels = 0, sampleRate = 0, bits = 0;
                bool formatSeen = false;
                byte[] pcm = null;
                var extra = new List<RiffChunk>();

                foreach (var (id, pos, size) in RiffReader.Chunks(data, 12, end))
                {
                    switch (id)
                    {
                        case "fmt ":
                            if (formatSeen)
                                throw new InvalidDataException("Duplicate WAVE format chunk.");
                            if (size < 16 || size == 17)
                                throw new InvalidDataException("Corrupt fmt chunk.");
                            ushort format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
                            channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2));
                            sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
                            bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 14));
                            if (size >= 18 && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 16)) > size - 18)
                                throw new InvalidDataException("WAVE format extension exceeds the chunk.");
                            if (format == FormatExtensible)
                            {
                                if (size < 40 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 16)) < 22)
                                    throw new InvalidDataException("Corrupt extensible fmt chunk.");
                                var subFormat = new Guid(data.AsSpan(pos + 24, 16));
                                if (subFormat != new Guid("00000001-0000-0010-8000-00aa00389b71"))
                                    throw new NotSupportedException("Only the integer PCM extensible subformat is supported.");
                                int validBits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 18));
                                if (validBits > bits)
                                    throw new InvalidDataException("Extensible PCM precision exceeds its sample container.");
                                if (validBits != bits)
                                    throw new NotSupportedException("PCM with fewer valid bits than its sample container is not supported.");
                                format = FormatPcm;
                            }
                            if (format != FormatPcm)
                                throw new NotSupportedException($"WAVE format tag {format} is not supported; only integer PCM is.");
                            if (channels < 1 || sampleRate < 1)
                                throw new InvalidDataException("Corrupt fmt chunk.");
                            if (Array.IndexOf(SupportedDepths, bits) < 0)
                                throw new NotSupportedException($"{bits}-bit PCM is not supported.");
                            int alignment = channels * (bits / 8);
                            uint byteRate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 8));
                            if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 12)) != alignment
                                || byteRate != (long)sampleRate * alignment)
                                throw new InvalidDataException("PCM block alignment or byte rate does not match its format.");
                            formatSeen = true;
                            break;

                        case "data":
                            if (!formatSeen || pcm != null)
                                throw new InvalidDataException("WAVE data must follow a single format chunk and must not be repeated.");
                            if (size % (channels * (bits / 8)) != 0)
                                throw new InvalidDataException("PCM data ends in an incomplete sample frame.");
                            pcm = data.AsSpan(pos, size).ToArray();
                            break;

                        default:
                            extra.Add(new RiffChunk(id, data.AsSpan(pos, size).ToArray()));
                            break;
                    }
                }

                if (!formatSeen || pcm == null)
                    throw new InvalidDataException("WAVE file is missing the fmt or data chunk.");
                int bytesPerSample = bits / 8;
                int count = pcm.Length / bytesPerSample;
                var samples = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int offset = i * bytesPerSample;
                    samples[i] = bits switch
                    {
                        8 => pcm[offset] - 128,
                        16 => BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset)),
                        24 => (pcm[offset] | (pcm[offset + 1] << 8) | (pcm[offset + 2] << 16)) << 8 >> 8,
                        _ => BinaryPrimitives.ReadInt32LittleEndian(pcm.AsSpan(offset)),
                    };
                }

                return new PcmAudio(sampleRate, channels, bits, samples, extra);
            }
        }

        private static class WaveWriter
        {
            public static byte[] Write(PcmAudio audio)
            {
                int bytesPerSample = audio.BitsPerSample / 8;
                int dataSize = audio.Samples.Length * bytesPerSample;

                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream);

                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(0); // patched below
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((ushort)1);
                writer.Write((ushort)audio.Channels);
                writer.Write(audio.SampleRate);
                writer.Write(audio.SampleRate * audio.Channels * bytesPerSample);
                writer.Write((ushort)(audio.Channels * bytesPerSample));
                writer.Write((ushort)audio.BitsPerSample);

                foreach (var chunk in audio.ExtraChunks)
                {
                    writer.Write(Encoding.ASCII.GetBytes(chunk.Id));
                    writer.Write(chunk.Payload.Length);
                    writer.Write(chunk.Payload);
                    if ((chunk.Payload.Length & 1) == 1)
                        writer.Write((byte)0);
                }

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                foreach (int sample in audio.Samples)
                {
                    switch (audio.BitsPerSample)
                    {
                        case 8: writer.Write((byte)(sample + 128)); break;
                        case 16: writer.Write((short)sample); break;
                        case 24: writer.Write((byte)sample); writer.Write((byte)(sample >> 8)); writer.Write((byte)(sample >> 16)); break;
                        default: writer.Write(sample); break;
                    }
                }
                if ((dataSize & 1) == 1)
                    writer.Write((byte)0);

                writer.Flush();
                var bytes = stream.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
                return bytes;
            }
        }
    }
}
