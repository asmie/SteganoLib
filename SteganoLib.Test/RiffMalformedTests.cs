using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Audio;
using SteganoLib.Metadata;
using SteganoLib.Video;
using Xunit;

namespace SteganoLib.Test
{
    public class RiffMalformedTests
    {
        private static byte[] Join(params byte[][] arrays) => arrays.SelectMany(a => a).ToArray();
        private static byte[] Tag(string id) => Encoding.ASCII.GetBytes(id);
        private static byte[] Chunk(string id, byte[] body)
        {
            var chunk = new byte[8 + body.Length + (body.Length & 1)];
            Tag(id).CopyTo(chunk, 0);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), body.Length);
            body.CopyTo(chunk, 8);
            return chunk;
        }
        private static byte[] List(string type, params byte[][] chunks) => Chunk("LIST", Join(Tag(type), Join(chunks)));
        private static byte[] Riff(string form, params byte[][] chunks) => Chunk("RIFF", Join(Tag(form), Join(chunks)));
        private static byte[] Set32(byte[] bytes, int offset, uint value)
        {
            var result = (byte[])bytes.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(offset), value);
            return result;
        }
        private static byte[] Set16(byte[] bytes, int offset, ushort value)
        {
            var result = (byte[])bytes.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(offset), value);
            return result;
        }
        private static byte[] Format(int bits = 16, bool extensible = false)
        {
            var format = new byte[extensible ? 40 : 16];
            BinaryPrimitives.WriteUInt16LittleEndian(format, extensible ? (ushort)0xFFFE : (ushort)1);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 1);
            BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), 8000);
            BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), 8000 * bits / 8);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), (ushort)(bits / 8));
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), (ushort)bits);
            if (extensible)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(16), 22);
                BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(18), (ushort)bits);
                new Guid("00000001-0000-0010-8000-00aa00389b71").ToByteArray().CopyTo(format, 24);
            }
            return format;
        }
        private static byte[] Wave(byte[] format = null, byte[] samples = null) => Riff("WAVE",
            Chunk("fmt ", format ?? Format()), Chunk("data", samples ?? new byte[] { 0, 0, 255, 127 }));

        [Theory]
        [InlineData(0u)]
        [InlineData(3u)]
        [InlineData(4u)]
        [InlineData(0x7FFFFFFFu)]
        [InlineData(0xFFFFFFFFu)]
        public void InvalidContainerSizes_AreRejectedByAllRiffReaders(uint size)
        {
            var wav = Set32(Wave(), 4, size);
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(wav));
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(wav));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Set32(Avi(), 4, size)));
        }

        [Theory]
        [InlineData(3u)]
        [InlineData(0x7FFFFFFFu)]
        [InlineData(0xFFFFFFFFu)]
        public void ChunkSizeCannotCrossParentBoundary(uint size)
        {
            var corrupt = Set32(Chunk("JUNK", new byte[2]), 4, size);
            var wav = Riff("WAVE", Chunk("fmt ", Format()), Chunk("data", new byte[2]), corrupt);
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(wav));
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(wav));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(movie: corrupt)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        public void PartialChunkHeaders_AreRejected(int length)
        {
            var wav = Riff("WAVE", Chunk("fmt ", Format()), Chunk("data", new byte[2]), new byte[length]);
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(wav));
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(wav));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(movie: new byte[length])));
        }

        [Fact]
        public void MissingPaddingAndShortListTypes_AreRejected()
        {
            var unpadded = Chunk("JUNK", new byte[1])[..^1];
            // Nesting ensures the outer RIFF pad cannot stand in for the missing child pad.
            var list = List("INFO", unpadded);
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(Riff("WAVE", list)));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(movie: List("rec ", unpadded))));
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(Riff("WAVE", Chunk("LIST", new byte[2]))));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(movie: Chunk("LIST", new byte[2]))));
        }

        public static IEnumerable<object[]> InvalidPcmFormats()
        {
            yield return new object[] { "short fmt", new byte[15] };
            yield return new object[] { "partial extension size", new byte[17] };
            yield return new object[] { "no channels", Set16(Format(), 2, 0) };
            yield return new object[] { "no sample rate", Set32(Format(), 4, 0) };
            yield return new object[] { "unrepresentable sample rate", Set32(Format(), 4, uint.MaxValue) };
            yield return new object[] { "wrong byte rate", Set32(Format(), 8, 1) };
            yield return new object[] { "wrong alignment", Set16(Format(), 12, 1) };
            yield return new object[] { "overflowing byte rate", Set32(Format(), 4, int.MaxValue) };
            yield return new object[] { "short extensible format", Format(extensible: true)[..39] };
            yield return new object[] { "extensible size too small", Set16(Format(extensible: true), 16, 21) };
            yield return new object[] { "extension crosses chunk", Set16(Format(extensible: true), 16, 23) };
            yield return new object[] { "invalid valid bits", Set16(Format(extensible: true), 18, 17) };
        }

        [Theory]
        [MemberData(nameof(InvalidPcmFormats))]
        public void InvalidPcmFormat_ThrowsInvalidData(string description, byte[] format)
        {
            var error = Record.Exception(() => PcmAudio.Load(Wave(format)));
            Assert.True(error is InvalidDataException, $"{description}: {error}");
        }

        [Theory]
        [InlineData(24)]
        [InlineData(26)]
        [InlineData(28)]
        [InlineData(39)]
        public void ExtensiblePcm_RequiresTheEntireSubformatGuid(int position)
        {
            var format = Format(extensible: true);
            format[position] ^= 0xFF;
            var wav = Wave(format);
            Assert.Throws<NotSupportedException>(() => PcmAudio.Load(wav));
            // Metadata does not interpret sample encoding and must preserve unfamiliar formats.
            Assert.Equal(wav, new WavMetadataStore(wav).ToArray());
        }

        [Fact]
        public void ReducedPrecisionPcm_IsExplicitlyUnsupported()
        {
            var wav = Wave(Set16(Format(24, extensible: true), 18, 20), new byte[3]);
            Assert.Throws<NotSupportedException>(() => PcmAudio.Load(wav));
            Assert.Equal(wav, new WavMetadataStore(wav).ToArray());
        }

        [Fact]
        public void ExtensibleChannelMasksNeedNotMatchChannelCount()
        {
            var format = Set32(Format(extensible: true), 20, 3);
            Assert.Equal(new[] { 0, 32767 }, PcmAudio.Load(Wave(format)).Samples);
        }

        [Fact]
        public void PcmFraming_DoesNotDropPartialSamplesOrDuplicateChunks()
        {
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(Wave(samples: new byte[3])));
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(Riff("WAVE", Chunk("data", new byte[2]), Chunk("fmt ", Format()))));
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(Riff("WAVE", Chunk("fmt ", Format()), Chunk("fmt ", Format()), Chunk("data", new byte[2]))));
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(Riff("WAVE", Chunk("fmt ", Format()), Chunk("data", new byte[2]), Chunk("data", new byte[2]))));
            var stereo = Set16(Set16(Set32(Format(), 8, 32000), 12, 4), 2, 2);
            Assert.Throws<InvalidDataException>(() => PcmAudio.Load(Wave(stereo, new byte[2])));
        }

        [Fact]
        public void OddLengthPcmAndUnknownChunks_RoundTrip()
        {
            var wav = Riff("WAVE", Chunk("fmt ", Format(8)), Chunk("JUNK", new byte[] { 4, 5, 6 }), Chunk("data", new byte[] { 0, 128, 255 }));
            var audio = PcmAudio.Load(wav);
            Assert.Equal(new[] { -128, 0, 127 }, audio.Samples);
            Assert.Equal(audio.Samples, PcmAudio.Load(audio.ToArray()).Samples);
            Assert.Equal(wav, new WavMetadataStore(wav).ToArray());
        }

        private static byte[] Avi(byte[] bitmap = null, byte[] streamHeader = null, byte[] movie = null, bool movieFirst = false, byte[] headerExtra = null, byte[] streamExtra = null)
        {
            var main = new byte[56];
            BinaryPrimitives.WriteInt32LittleEndian(main.AsSpan(24), 1);
            var header = List("hdrl", Chunk("avih", main), List("strl", Chunk("strh", streamHeader ?? StreamHeader()), Chunk("strf", bitmap ?? Bitmap()), streamExtra ?? Array.Empty<byte>()), headerExtra ?? Array.Empty<byte>());
            var movi = List("movi", movie ?? Chunk("00db", new byte[] { 3, 2, 1, 0 }));
            return movieFirst ? Riff("AVI ", movi, header) : Riff("AVI ", header, movi);
        }
        private static byte[] Bitmap()
        {
            var data = new byte[40];
            BinaryPrimitives.WriteInt32LittleEndian(data, 40);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 24);
            return data;
        }
        private static byte[] StreamHeader()
        {
            var data = new byte[56];
            Tag("vids").CopyTo(data, 0);
            Tag("DIB ").CopyTo(data, 4);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), 25);
            return data;
        }

        [Theory]
        [InlineData(4, 0u)]
        [InlineData(8, 0u)]
        [InlineData(8, 0x80000000u)]
        [InlineData(4, 0x7FFFFFFFu)]
        [InlineData(0, 39u)]
        [InlineData(0, 41u)]
        public void InvalidAviBitmap_IsRejectedBeforeFrameDecoding(int offset, uint value)
        {
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(bitmap: Set32(Bitmap(), offset, value))));
        }

        [Fact]
        public void AviDimensions_CannotOverflowFrameSize()
        {
            var bitmap = Set16(Set32(Set32(Bitmap(), 4, int.MaxValue), 8, int.MaxValue), 14, 32);
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(bitmap: bitmap)));
            Assert.Throws<ArgumentOutOfRangeException>(() => AviVideo.CreateRgb(int.MaxValue, int.MaxValue, 32));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(5)]
        public void WrongAviRgbFrameLength_IsRejectedOnLoad(int size)
        {
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(movie: Chunk("00db", new byte[size]))));
        }

        [Fact]
        public void AviHeaderOrderDuplicatesAndTiming_AreValidated()
        {
            Assert.Throws<NotSupportedException>(() => AviVideo.Load(Avi(movie: Chunk("00db", Array.Empty<byte>()))));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(movieFirst: true)));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(headerExtra: Chunk("avih", new byte[56]))));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(bitmap: Set16(Bitmap(), 12, 0))));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(streamHeader: Set32(StreamHeader(), 20, 0))));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(streamHeader: Set32(StreamHeader(), 24, 0))));
            Assert.Throws<NotSupportedException>(() => AviVideo.Load(Avi(movie: List("INFO", Chunk("JUNK", new byte[2])))));
        }

        [Fact]
        public void DuplicateAviListsAndStreamHeaders_AreRejected()
        {
            var avi = Avi();
            int headerLength = 8 + BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(16));
            var header = avi.AsSpan(12, headerLength).ToArray();
            var movie = avi.AsSpan(12 + headerLength).ToArray();
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Riff("AVI ", header, header, movie)));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Riff("AVI ", header, movie, movie)));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(streamExtra: Chunk("strh", StreamHeader()))));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(streamExtra: Chunk("strf", Bitmap()))));
            Assert.Throws<NotSupportedException>(() => AviVideo.Load(Avi(headerExtra: List("strl", Chunk("strh", StreamHeader()), Chunk("strf", Bitmap())))));
        }

        [Fact]
        public void DeeplyNestedRecords_PreserveFrameOrderWithoutRecursion()
        {
            const int depth = 20_000;
            var frame = Chunk("00db", new byte[] { 6, 5, 4, 0 });
            var nested = new byte[depth * 12 + frame.Length];
            for (int i = 0; i < depth; i++)
            {
                int pos = i * 12;
                Tag("LIST").CopyTo(nested, pos);
                BinaryPrimitives.WriteInt32LittleEndian(nested.AsSpan(pos + 4), nested.Length - pos - 8);
                Tag("rec ").CopyTo(nested, pos + 8);
            }
            frame.CopyTo(nested, depth * 12);
            var video = AviVideo.Load(Avi(movie: Join(Chunk("00db", new byte[] { 3, 2, 1, 0 }), nested, Chunk("00db", new byte[] { 9, 8, 7, 0 }))));
            Assert.Equal(3, video.FrameCount);
            using var first = video.DecodeFrame(0);
            using var second = video.DecodeFrame(1);
            using var third = video.DecodeFrame(2);
            Assert.Equal(new Rgba32(1, 2, 3), first[0, 0]);
            Assert.Equal(new Rgba32(4, 5, 6), second[0, 0]);
            Assert.Equal(new Rgba32(7, 8, 9), third[0, 0]);
            Assert.Equal(3, AviVideo.Load(video.ToArray()).FrameCount);
        }

        [Fact]
        public void MjpegDimensions_AreCheckedWhenDecoding()
        {
            var bitmap = Bitmap();
            Tag("MJPG").CopyTo(bitmap, 16);
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Avi(bitmap: bitmap, movie: Chunk("00dc", new byte[4]))));
            var jpeg = JpegImageTests.SampleJpeg(8, 8);
            var video = AviVideo.Load(Avi(bitmap: bitmap, movie: Chunk("00dc", jpeg)));
            Assert.Throws<InvalidDataException>(() => video.DecodeJpegFrame(0));
        }

        [Fact]
        public void TruncationAndTrailingData_AreRejected()
        {
            var wav = Wave();
            var avi = Avi();
            for (int length = 0; length < wav.Length; length++)
            {
                var prefix = wav.AsSpan(0, length).ToArray();
                Assert.Throws<InvalidDataException>(() => PcmAudio.Load(prefix));
                Assert.Throws<InvalidDataException>(() => new WavMetadataStore(prefix));
            }
            for (int length = 0; length < avi.Length; length++)
                Assert.Throws<InvalidDataException>(() => AviVideo.Load(avi.AsSpan(0, length).ToArray()));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Join(avi, new byte[2])));
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(Join(wav, new byte[2])));
            Assert.Throws<NotSupportedException>(() => AviVideo.Load(Join(avi, Riff("AVIX"))));
        }
    }
}
