using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Jpeg;
using Xunit;

namespace SteganoLib.Test
{
    public class JpegMalformedTests
    {
        // Hand-authored single-block JPEG: DC category 0 and AC EOB each have code 0.
        // Its entropy byte is 00 followed by six padding ones. No encoder under test
        // participates in generating the fixture.
        private static readonly byte[] Frame = { 8, 0, 8, 0, 8, 1, 1, 0x11, 0 };
        private static readonly byte[] Scan = { 1, 1, 0, 0, 63, 0 };
        private static readonly byte[] Quant = Join(new byte[] { 0 }, Enumerable.Repeat((byte)1, 64).ToArray());

        private static byte[] Join(params byte[][] arrays) => arrays.SelectMany(a => a).ToArray();

        private static byte[] Segment(byte marker, params byte[] body) => Join(
            new byte[] { 0xFF, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2) }, body);

        private static byte[] Table(byte tableClass, byte symbol) => Join(
            new byte[] { tableClass, 1 }, new byte[15], new byte[] { symbol });

        private static byte[] Minimal(byte[] frame = null, byte[] scan = null, byte[] quant = null,
            byte[] huffman = null, byte[] entropy = null, byte[] beforeScan = null, byte frameMarker = 0xC0) => Join(
                new byte[] { 0xFF, 0xD8 }, Segment(0xDB, quant ?? Quant), Segment(frameMarker, frame ?? Frame),
                Segment(0xC4, huffman ?? Join(Table(0, 0), Table(0x10, 0))), beforeScan ?? Array.Empty<byte>(),
                Segment(0xDA, scan ?? Scan), entropy ?? new byte[] { 0x3F }, new byte[] { 0xFF, 0xD9 });

        private static byte[] Change(byte[] bytes, int index, byte value)
        {
            var changed = (byte[])bytes.Clone();
            changed[index] = value;
            return changed;
        }

        public static IEnumerable<object[]> InvalidFiles()
        {
            yield return new object[] { "duplicate frame component", Minimal(frame: new byte[] { 8, 0, 8, 0, 8, 2, 1, 0x11, 0, 1, 0x11, 0 }) };
            yield return new object[] { "missing component scan", Minimal(frame: new byte[] { 8, 0, 8, 0, 8, 2, 1, 0x11, 0, 2, 0x11, 0 }) };
            yield return new object[] { "duplicate scan component", Minimal(scan: new byte[] { 2, 1, 0, 1, 0, 0, 63, 0 }) };
            yield return new object[] { "unknown scan component", Minimal(scan: Change(Scan, 1, 2)) };
            foreach (byte selector in new byte[] { 0x40, 0x04, 0xFF, 0x20, 0x02, 0x11 })
                yield return new object[] { $"invalid or missing Huffman selector {selector:X2}", Minimal(scan: Change(Scan, 2, selector)) };
            yield return new object[] { "missing quantisation table", Minimal(frame: Change(Frame, 8, 1)) };
            foreach (byte sampling in new byte[] { 0x01, 0x10, 0x51, 0x15 })
                yield return new object[] { $"invalid sampling {sampling:X2}", Minimal(frame: Change(Frame, 7, sampling)) };
            yield return new object[] { "invalid quantisation selector", Minimal(frame: Change(Frame, 8, 4)) };
            yield return new object[] { "zero width", Minimal(frame: Change(Frame, 4, 0)) };
            yield return new object[] { "invalid precision", Minimal(frame: Change(Frame, 0, 0)) };
            yield return new object[] { "invalid spectral start", Minimal(scan: Change(Scan, 3, 1)) };
            yield return new object[] { "invalid spectral end", Minimal(scan: Change(Scan, 4, 62)) };
            yield return new object[] { "successive approximation", Minimal(scan: Change(Scan, 5, 1)) };
            yield return new object[] { "empty Huffman segment", Minimal(huffman: Array.Empty<byte>()) };
            yield return new object[] { "truncated Huffman counts", Minimal(huffman: new byte[16]) };
            yield return new object[] { "truncated Huffman symbols", Minimal(huffman: Table(0, 0)[..^1]) };
            yield return new object[] { "empty Huffman table", Minimal(huffman: new byte[17]) };
            yield return new object[] { "invalid Huffman class", Minimal(huffman: Table(0x20, 0)) };
            yield return new object[] { "invalid Huffman ID", Minimal(huffman: Table(0x04, 0)) };
            yield return new object[] { "all-ones Huffman code", Minimal(huffman: Join(new byte[] { 0, 2 }, new byte[15], new byte[] { 0, 1 })) };
            yield return new object[] { "oversubscribed Huffman tree", Minimal(huffman: Join(new byte[] { 0, 3 }, new byte[15], new byte[] { 0, 1, 2 })) };
            yield return new object[] { "duplicate Huffman symbol", Minimal(huffman: Join(new byte[] { 0, 0, 2 }, new byte[14], new byte[] { 0, 0 })) };
            yield return new object[] { "empty quantisation segment", Minimal(quant: Array.Empty<byte>()) };
            yield return new object[] { "truncated quantisation table", Minimal(quant: Quant[..^1]) };
            yield return new object[] { "truncated 16-bit quantisation table", Minimal(quant: Change(Quant, 0, 0x10)) };
            yield return new object[] { "invalid quantisation precision", Minimal(quant: Change(Quant, 0, 0x20)) };
            yield return new object[] { "invalid quantisation ID", Minimal(quant: Change(Quant, 0, 4)) };
            yield return new object[] { "zero quantisation value", Minimal(quant: Change(Quant, 1, 0)) };
            yield return new object[] { "empty scan", Minimal(entropy: Array.Empty<byte>()) };
            yield return new object[] { "zero padding", Minimal(entropy: new byte[] { 0 }) };
            yield return new object[] { "extra entropy byte", Minimal(entropy: new byte[] { 0x3F, 0 }) };
            yield return new object[] { "invalid DC category", Minimal(huffman: Join(Table(0, 12), Table(0x10, 0))) };
            foreach (byte ac in new byte[] { 0x0B, 0x10, 0xE0 })
                yield return new object[] { $"invalid AC symbol {ac:X2}", Minimal(huffman: Join(Table(0, 0), Table(0x10, ac))) };
            yield return new object[] { "zero run past block end", Minimal(huffman: Join(Table(0, 0), Table(0x10, 0xF0)), entropy: new byte[] { 0x07 }) };
            yield return new object[] { "DC outside 11-bit range", Minimal(huffman: Join(Table(0, 11), Table(0x10, 0)), entropy: new byte[] { 0x7F, 0xEF }) };
            yield return new object[] { "missing restart", Minimal(frame: Change(Frame, 4, 16), beforeScan: Segment(0xDD, 0, 1), entropy: new byte[] { 0x3F, 0x3F }) };
            yield return new object[] { "wrong restart number", Minimal(frame: Change(Frame, 4, 16), beforeScan: Segment(0xDD, 0, 1), entropy: new byte[] { 0x3F, 0xFF, 0xD1, 0x3F }) };
            yield return new object[] { "stray restart", Minimal(beforeScan: new byte[] { 0xFF, 0xD0 }) };
            yield return new object[] { "repeated SOI", Minimal(beforeScan: new byte[] { 0xFF, 0xD8 }) };
            yield return new object[] { "stuffing outside scan", Minimal(beforeScan: new byte[] { 0xFF, 0 }) };
            yield return new object[] { "garbage between markers", Minimal(beforeScan: new byte[] { 42 }) };
            yield return new object[] { "repeated scan", Minimal(entropy: Join(new byte[] { 0x3F }, Segment(0xDA, Scan), new byte[] { 0x3F })) };
            yield return new object[] { "repeated frame", Minimal(beforeScan: Segment(0xC0, Frame)) };
            yield return new object[] { "too many interleaved blocks", Minimal(frame: new byte[] { 8, 0, 8, 0, 8, 2, 1, 0x44, 0, 2, 0x11, 0 }, scan: new byte[] { 2, 1, 0, 2, 0, 0, 63, 0 }) };
        }

        [Theory]
        [MemberData(nameof(InvalidFiles))]
        public void MalformedFiles_ThrowInvalidData(string description, byte[] data)
        {
            var error = Record.Exception(() => JpegImage.Load(data));
            Assert.True(error is InvalidDataException, $"{description}: {error}");
        }

        [Theory]
        [InlineData(0xC0)]
        [InlineData(0xC4)]
        [InlineData(0xDB)]
        [InlineData(0xDA)]
        [InlineData(0xDD)]
        [InlineData(0xE1)]
        public void SegmentLengths_AreCheckedBeforeReadingBodies(byte marker)
        {
            foreach (int length in new[] { 0, 1, 65535 })
            {
                var malformed = new byte[] { 0xFF, marker, (byte)(length >> 8), (byte)length };
                Assert.Throws<InvalidDataException>(() => JpegImage.Load(Minimal(beforeScan: malformed)));
            }
        }

        [Fact]
        public void EveryTruncatedPrefix_IsRejected()
        {
            var data = Minimal();
            for (int length = 0; length < data.Length; length++)
                Assert.Throws<InvalidDataException>(() => JpegImage.Load(data.AsSpan(0, length).ToArray()));
        }

        [Fact]
        public void IndependentSingleBlockFixture_DecodesToZeroCoefficientsAndGreyPixels()
        {
            var data = Minimal();
            var image = JpegImage.Load(data);
            Assert.All(image.Components.Single().Coefficients, value => Assert.Equal(0, value));
            using var decoded = Image.Load<Rgba32>(data);
            using var reencoded = Image.Load<Rgba32>(image.ToArray());
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    Assert.Equal(new Rgba32(128, 128, 128), decoded[x, y]);
                    Assert.Equal(decoded[x, y], reencoded[x, y]);
                }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LongMarkerFillRuns_AreHandledWithoutRecursion(bool restart)
        {
            var fill = Enumerable.Repeat((byte)0xFF, 1_000_000).ToArray();
            var entropy = restart
                ? Join(new byte[] { 0x3F }, fill, new byte[] { 0xD0, 0x3F })
                : Join(new byte[] { 0x3F }, fill);
            var data = Minimal(frame: Change(Frame, 4, restart ? (byte)16 : (byte)8), entropy: entropy,
                beforeScan: restart ? Segment(0xDD, 0, 1) : null);
            Assert.All(JpegImage.Load(data).Components.Single().Coefficients, value => Assert.Equal(0, value));
            Assert.Throws<InvalidDataException>(() => JpegImage.Load(Minimal(entropy: fill)));
        }

        [Theory]
        [InlineData(8192)]
        [InlineData(65535)]
        public void ImpossibleDimensions_AreRejectedBeforeLargeAllocation(int dimension)
        {
            var frame = (byte[])Frame.Clone();
            frame[1] = frame[3] = (byte)(dimension >> 8);
            frame[2] = frame[4] = (byte)dimension;
            var data = Minimal(frame: frame);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<InvalidDataException>(() => JpegImage.Load(data));
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 100_000);
        }

        [Fact]
        public void RedefinedUsedQuantisationTable_IsRejectedInsteadOfChangingSavedPixels()
        {
            var changed = Change(Quant, 1, 2);
            Assert.Throws<NotSupportedException>(() => JpegImage.Load(Minimal(entropy: Join(new byte[] { 0x3F }, Segment(0xDB, changed)))));
            // Identical definitions and replacement before use retain their meaning.
            Assert.NotNull(JpegImage.Load(Minimal(entropy: Join(new byte[] { 0x3F }, Segment(0xDB, Quant)))));
            Assert.Equal(2, JpegImage.Load(Minimal(beforeScan: Segment(0xDB, changed))).QuantizationTables[0][0]);
        }

        [Fact]
        public void EntropyByteStuffing_RemainsSupported()
        {
            var reader = new BitReader(new byte[] { 0xFF, 0, 0xFF, 0xD9 }, 0);
            Assert.Equal(255, reader.ReadBits(8));
            Assert.Equal(2, reader.FinishScan());
        }

        [Fact]
        public void ExtendedSequential_HighTableIdsAndWideQuantisationRemainSupported()
        {
            var quant = Join(new byte[] { 0x10 }, Enumerable.Repeat(new byte[] { 1, 1 }, 64).SelectMany(a => a).ToArray());
            var data = Minimal(frameMarker: 0xC1, quant: quant, scan: Change(Scan, 2, 0x33),
                huffman: Join(Table(3, 0), Table(0x13, 0)));
            var image = JpegImage.Load(data);
            Assert.Equal(0xC1, image.FrameMarker);
            Assert.All(image.QuantizationTables[0], value => Assert.Equal(257, value));
            var reloaded = JpegImage.Load(image.ToArray());
            Assert.Equal(image.QuantizationTables[0], reloaded.QuantizationTables[0]);
            Assert.Equal(image.Components[0].Coefficients, reloaded.Components[0].Coefficients);
        }

        [Fact]
        public void SingleByteMutations_DoNotEscapeAsIndexingOrArithmeticErrors()
        {
            var data = Minimal();
            for (int position = 0; position < data.Length; position++)
            {
                foreach (byte value in new byte[] { 0, 1, 127, 255 })
                {
                    var error = Record.Exception(() => JpegImage.Load(Change(data, position, value)));
                    Assert.True(error == null || error is InvalidDataException || error is NotSupportedException,
                        $"Byte {position}, value {value}: {error}");
                }
            }
        }
    }
}
