using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Metadata;
using Xunit;

namespace SteganoLib.Test
{
    public class MetadataValidationTests
    {
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] Header = { 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0 };
        private static readonly byte[] Frame = { 8, 0, 8, 0, 8, 1, 1, 0x11, 0 };
        private static readonly byte[] Scan = { 1, 1, 0, 0, 63, 0 };

        private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        private static byte[] Change(byte[] source, int index, byte value)
        {
            var copy = (byte[])source.Clone();
            copy[index] = value;
            return copy;
        }

        private static byte[] Compress(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
                zlib.Write(data);
            return output.ToArray();
        }

        // Independent bitwise CRC, so corrupted fixtures still have valid chunk checksums.
        private static byte[] Chunk(string type, byte[] data)
        {
            var bytes = new byte[data.Length + 12];
            BinaryPrimitives.WriteInt32BigEndian(bytes, data.Length);
            Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
            data.CopyTo(bytes, 8);
            uint crc = uint.MaxValue;
            foreach (byte value in bytes.AsSpan(4, data.Length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(bytes.Length - 4), ~crc);
            return bytes;
        }

        private static byte[] Png(params byte[][] chunks) => Join(PngSignature, Join(chunks));
        private static byte[] Ihdr => Chunk("IHDR", Header);
        private static byte[] Idat => Chunk("IDAT", Compress(new byte[] { 0, 10, 20, 30 }));
        private static byte[] Iend => Chunk("IEND", Array.Empty<byte>());

        public static IEnumerable<object[]> InvalidPngs()
        {
            yield return new object[] { "IHDR missing", Png(Idat, Iend) };
            yield return new object[] { "IHDR duplicate", Png(Ihdr, Ihdr, Idat, Iend) };
            yield return new object[] { "IHDR short", Png(Chunk("IHDR", Header[..^1]), Idat, Iend) };
            yield return new object[] { "IHDR long", Png(Chunk("IHDR", Join(Header, new byte[1])), Idat, Iend) };
            foreach (var change in new (int Index, byte Value)[] { (3, 0), (7, 0), (0, 128), (4, 128), (8, 3), (9, 1), (9, 5), (10, 1), (11, 1), (12, 2) })
                yield return new object[] { $"IHDR field {change}", Png(Chunk("IHDR", Change(Header, change.Index, change.Value)), Idat, Iend) };
            yield return new object[] { "IDAT missing", Png(Ihdr, Iend) };
            yield return new object[] { "IDAT separated", Png(Ihdr, Idat, Chunk("teSt", Array.Empty<byte>()), Idat, Iend) };
            yield return new object[] { "invalid type", Png(Ihdr, Idat, Chunk("me1a", new byte[1]), Iend) };
            yield return new object[] { "IEND nonempty", Png(Ihdr, Idat, Chunk("IEND", new byte[1])) };
            yield return new object[] { "IEND duplicate", Png(Ihdr, Idat, Iend, Iend) };
            yield return new object[] { "trailing byte", Join(Png(Ihdr, Idat, Iend), new byte[1]) };
            yield return new object[] { "palette late", Png(Ihdr, Idat, Chunk("PLTE", new byte[3]), Iend) };
            yield return new object[] { "palette duplicate", Png(Ihdr, Chunk("PLTE", new byte[3]), Chunk("PLTE", new byte[3]), Idat, Iend) };
            foreach (int length in new[] { 0, 2, 769, 771 })
                yield return new object[] { $"palette length {length}", Png(Ihdr, Chunk("PLTE", new byte[length]), Idat, Iend) };
            foreach (byte color in new byte[] { 0, 4 })
                yield return new object[] { $"grayscale palette {color}", Png(Chunk("IHDR", Change(Header, 9, color)), Chunk("PLTE", new byte[3]), Idat, Iend) };
            yield return new object[] { "indexed palette missing", Png(Chunk("IHDR", Change(Header, 9, 3)), Idat, Iend) };
            yield return new object[] { "indexed palette too large", Png(Chunk("IHDR", Change(Change(Header, 9, 3), 8, 1)), Chunk("PLTE", new byte[9]), Idat, Iend) };
            yield return new object[] { "indexed depth 16", Png(Chunk("IHDR", Change(Change(Header, 9, 3), 8, 16)), Chunk("PLTE", new byte[3]), Idat, Iend) };
            yield return new object[] { "truecolor depth 1", Png(Chunk("IHDR", Change(Header, 8, 1)), Idat, Iend) };
            yield return new object[] { "oversized chunk", Join(PngSignature, new byte[] { 0x7F, 0xFF, 0xFF, 0xFF, 73, 72, 68, 82, 0, 0, 0, 0 }) };
        }

        [Theory]
        [MemberData(nameof(InvalidPngs))]
        public void Png_RejectsInvalidStructure(string reason, byte[] file)
        {
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.Throws<InvalidDataException>(() => new PngMetadataStore(file));
        }

        [Fact]
        public void Png_PreservesConsecutiveDataAndUnknownChunks()
        {
            byte[] compressed = Compress(new byte[] { 0, 10, 20, 30 });
            byte[] file = Png(Ihdr, Chunk("teSt", new byte[] { 5 }), Chunk("IDAT", compressed[..3]),
                Chunk("IDAT", Array.Empty<byte>()), Chunk("IDAT", compressed[3..]), Iend);
            var store = new PngMetadataStore(file);
            store.WriteEntries(new[] { new byte[] { 1, 2, 3 } });
            using (var image = Image.Load<Rgb24>(store.ToArray()))
                Assert.Equal(new Rgb24(10, 20, 30), image[0, 0]);
            store.WriteEntries(Array.Empty<byte[]>());
            Assert.Equal(file, store.ToArray());
        }

        [Fact]
        public void Png_IndexedPalette_PreservesPixels()
        {
            byte[] file = Png(Chunk("IHDR", Change(Change(Header, 9, 3), 8, 1)), Chunk("PLTE", new byte[] { 10, 20, 30 }),
                Chunk("IDAT", Compress(new byte[] { 0, 0 })), Iend);
            var store = new PngMetadataStore(file);
            store.WriteEntries(new[] { new byte[] { 1 } });
            using var image = Image.Load<Rgb24>(store.ToArray());
            Assert.Equal(new Rgb24(10, 20, 30), image[0, 0]);
            store.WriteEntries(Array.Empty<byte[]>());
            Assert.Equal(file, store.ToArray());
        }

        [Fact]
        public void Png_RejectsEveryTruncatedPrefix()
        {
            byte[] file = Png(Ihdr, Idat, Iend);
            for (int length = 0; length < file.Length; length++)
                Assert.Throws<InvalidDataException>(() => new PngMetadataStore(file[..length]));
        }

        public static IEnumerable<object[]> InvalidTextChunks()
        {
            foreach (string type in new[] { "tEXt", "zTXt", "iTXt" })
            {
                yield return new object[] { type, Encoding.ASCII.GetBytes("Comment"), "missing keyword terminator" };
                foreach (string keyword in new[] { "", " Comment", "Comment ", "Two  spaces", "Tab\tkey", "Non\u00a0breaking", new string('a', 80) })
                    yield return new object[] { type, Join(Encoding.Latin1.GetBytes(keyword + "\0"), new byte[4]), $"keyword {keyword}" };
            }
            yield return new object[] { "tEXt", Encoding.ASCII.GetBytes("Comment\0QQ==\0"), "NUL in text" };
            foreach (byte[] body in new[] { Array.Empty<byte>(), new byte[] { 1 } })
                yield return new object[] { "zTXt", Join(Encoding.ASCII.GetBytes("Comment\0"), body), "compression method" };
            foreach (byte[] body in new[]
            {
                new byte[] { 0 }, new byte[] { 2, 0, 0, 0 }, new byte[] { 0, 1, 0, 0 },
                new byte[] { 0, 0 }, new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 255, 0, 0 },
                new byte[] { 0, 0, 0, 0xC0, 0x80, 0 }, new byte[] { 0, 0, 0, 0, 0xC0, 0x80 },
                new byte[] { 0, 0, 0, 0, 0 },
            })
                yield return new object[] { "iTXt", Join(Encoding.ASCII.GetBytes("Comment\0"), body), "international text fields" };
        }

        [Theory]
        [MemberData(nameof(InvalidTextChunks))]
        public void Png_RejectsMalformedTextEvenUnderAnotherSelectedChunkType(string type, byte[] data, string reason)
        {
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.Throws<InvalidDataException>(() => new PngMetadataStore(Png(Ihdr, Idat, Chunk(type, data), Iend)));
        }

        [Theory]
        [InlineData("zTXt")]
        [InlineData("iTXt")]
        public void Png_CompressedText_RejectsEveryTruncatedStream(string type)
        {
            byte[] compressed = Compress(Encoding.ASCII.GetBytes("AQIDBA=="));
            byte[] prefix = Join(Encoding.ASCII.GetBytes("Comment\0"), type == "zTXt" ? new byte[] { 0 } : new byte[] { 1, 0, 0, 0 });
            for (int length = 0; length < compressed.Length; length++)
            {
                var store = new PngMetadataStore(Png(Ihdr, Idat, Chunk(type, Join(prefix, compressed[..length])), Iend), type);
                Assert.Throws<InvalidDataException>(() => store.ReadEntries());
            }
        }

        [Theory]
        [InlineData("zTXt")]
        [InlineData("iTXt")]
        public void Png_CompressedText_RequiresDeflateDataEvenWithAnEmptyTextChecksum(string type)
        {
            byte[] prefix = Join(Encoding.ASCII.GetBytes("Comment\0"), type == "zTXt" ? new byte[] { 0 } : new byte[] { 1, 0, 0, 0 });
            byte[] headerAndChecksum = { 0x78, 0x9C, 0, 0, 0, 1 };
            var store = new PngMetadataStore(Png(Ihdr, Idat, Chunk(type, Join(prefix, headerAndChecksum)), Iend), type);
            Assert.Throws<InvalidDataException>(() => store.ReadEntries());
        }

        [Theory]
        [InlineData("zTXt")]
        [InlineData("iTXt")]
        public void Png_CompressedText_RoundTripsAndRejectsCorruptChecksum(string type)
        {
            byte[] prefix = Join(Encoding.ASCII.GetBytes("Comment\0"), type == "zTXt" ? new byte[] { 0 } : new byte[] { 1, 0, 0, 0 });
            byte[] compressed = Compress(Encoding.ASCII.GetBytes("AQIDBA=="));
            byte[] file = Png(Ihdr, Idat, Chunk(type, Join(prefix, compressed)), Iend);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(new PngMetadataStore(file, type).ReadEntries()));
            compressed[^1] ^= 1;
            var corrupt = new PngMetadataStore(Png(Ihdr, Idat, Chunk(type, Join(prefix, compressed)), Iend), type);
            Assert.Throws<InvalidDataException>(() => corrupt.ReadEntries());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Png_InternationalText_PreservesUnicodeAndLanguage(bool compressed)
        {
            byte[] prefix = Join(Encoding.ASCII.GetBytes("Comment\0"), new byte[] { compressed ? (byte)1 : (byte)0, 0 },
                Encoding.UTF8.GetBytes("pl-PL\0Komentarz 🐱\0"));
            byte[] text = Encoding.ASCII.GetBytes("AQID");
            byte[] file = Png(Ihdr, Idat, Chunk("iTXt", Join(prefix, compressed ? Compress(text) : text)), Iend);
            var store = new PngMetadataStore(file, "iTXt");
            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(store.ReadEntries()));
            Assert.Equal(file, store.ToArray());
        }

        [Fact]
        public void Png_RejectsInvalidUtf8InCompressedText()
        {
            byte[] data = Join(Encoding.ASCII.GetBytes("Comment\0"), new byte[] { 1, 0, 0, 0 }, Compress(new byte[] { 0xC0, 0x80 }));
            var store = new PngMetadataStore(Png(Ihdr, Idat, Chunk("iTXt", data), Iend), "iTXt");
            Assert.Throws<InvalidDataException>(() => store.ReadEntries());
        }

        [Fact]
        public void Png_RejectsConsecutiveSpacesInConfiguredKeyword()
        {
            Assert.Throws<ArgumentException>(() => new PngMetadataStore(Png(Ihdr, Idat, Iend), "tEXt", "Two  spaces"));
        }

        private static byte[] Segment(byte marker, byte[] body) =>
            Join(new byte[] { 255, marker, (byte)((body.Length + 2) >> 8), (byte)(body.Length + 2) }, body);

        private static byte[] Jpeg(byte[] frame = null, byte[] scan = null, byte[] entropy = null, byte[] header = null) =>
            Join(new byte[] { 255, 216 }, header ?? Array.Empty<byte>(), Segment(0xC0, frame ?? Frame),
                Segment(0xDA, scan ?? Scan), entropy ?? new byte[] { 0x3F }, new byte[] { 255, 217 });

        public static IEnumerable<object[]> InvalidJpegs()
        {
            yield return new object[] { "no scan", new byte[] { 255, 216, 255, 217 } };
            yield return new object[] { "no frame", Join(new byte[] { 255, 216 }, Segment(0xDA, Scan), new byte[] { 0x3F, 255, 217 }) };
            yield return new object[] { "nested SOI", Jpeg(header: new byte[] { 255, 216 }) };
            yield return new object[] { "restart outside entropy", Jpeg(header: new byte[] { 255, 208 }) };
            yield return new object[] { "stuffed zero outside entropy", Jpeg(header: new byte[] { 255, 0 }) };
            yield return new object[] { "frame component length", Jpeg(frame: Change(Frame, 5, 2)) };
            yield return new object[] { "empty frame", Jpeg(frame: Array.Empty<byte>()) };
            yield return new object[] { "zero width", Jpeg(frame: Change(Frame, 4, 0)) };
            yield return new object[] { "empty scan", Jpeg(scan: Array.Empty<byte>()) };
            yield return new object[] { "scan component length", Jpeg(scan: Change(Scan, 0, 2)) };
            yield return new object[] { "zero scan components", Jpeg(scan: new byte[4]) };
            yield return new object[] { "too many scan components", Jpeg(scan: Join(new byte[] { 5 }, new byte[13])) };
            yield return new object[] { "truncated scan segment", Jpeg(entropy: new byte[] { 255, 218, 255, 255 }) };
            yield return new object[] { "truncated postscan metadata", Jpeg(entropy: new byte[] { 0x3F, 255, 233, 0, 20 }) };
            yield return new object[] { "invalid postscan segment length", Jpeg(entropy: new byte[] { 0x3F, 255, 233, 0, 1 }) };
            yield return new object[] { "stuffing after fill bytes", Jpeg(entropy: new byte[] { 255, 255, 0 }) };
            yield return new object[] { "DNL outside entropy", Jpeg(header: Segment(0xDC, new byte[] { 0, 8 })) };
            yield return new object[] { "empty DNL", Jpeg(entropy: Segment(0xDC, Array.Empty<byte>())) };
            yield return new object[] { "zero DNL", Jpeg(entropy: Segment(0xDC, new byte[2])) };
            yield return new object[] { "trailing bytes", Join(Jpeg(), new byte[1]) };
        }

        [Theory]
        [MemberData(nameof(InvalidJpegs))]
        public void Jpeg_RejectsInvalidStructure(string reason, byte[] file)
        {
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.Throws<InvalidDataException>(() => new JpegMetadataStore(file));
        }

        [Fact]
        public void Jpeg_RejectsEveryTruncatedPrefix()
        {
            byte[] file = JpegImageTests.SampleJpeg();
            for (int length = 0; length < file.Length; length++)
                Assert.Throws<InvalidDataException>(() => new JpegMetadataStore(file[..length]));
        }

        [Fact]
        public void Jpeg_PreservesFillBytesAndStandaloneMarkers()
        {
            byte[] file = Jpeg(header: Join(Enumerable.Repeat((byte)255, 100_000).ToArray(),
                Segment(0xE1, new byte[] { 1, 2, 3 }), new byte[] { 255, 1 }),
                entropy: new byte[] { 1, 255, 0, 255, 208, 2, 255, 1, 3 });
            var store = new JpegMetadataStore(file);
            store.WriteEntries(new[] { new byte[] { 4, 5 } });
            Assert.Equal(new byte[] { 4, 5 }, Assert.Single(new JpegMetadataStore(store.ToArray()).ReadEntries()));
            store.WriteEntries(Array.Empty<byte[]>());
            Assert.Equal(file, store.ToArray());
        }

        [Fact]
        public void Jpeg_DnlResumesEntropyBytes()
        {
            byte[] file = Jpeg(frame: Change(Frame, 2, 0), entropy: Join(new byte[] { 0x3F }, Segment(0xDC, new byte[] { 0, 8 }), new byte[] { 0x3F }));
            Assert.Equal(file, new JpegMetadataStore(file).ToArray());
        }

        [Fact]
        public void Jpeg_ProgressiveTwoScanFixture_PreservesPixelsAndPostscanMetadata()
        {
            // Independent 8x8 gray image. DC zero and AC EOB each use the one-bit code 0.
            byte[] dcTable = Join(new byte[] { 0, 1 }, new byte[15], new byte[] { 0 });
            byte[] acTable = Join(new byte[] { 0x10, 1 }, new byte[15], new byte[] { 0 });
            byte[] file = Join(new byte[] { 255, 216 }, Segment(0xDB, Join(new byte[1], Enumerable.Repeat((byte)1, 64).ToArray())),
                Segment(0xC2, Frame), Segment(0xC4, Join(dcTable, acTable)),
                Segment(0xDA, new byte[] { 1, 1, 0, 0, 0, 0 }), new byte[] { 0x7F },
                Segment(0xFE, Encoding.ASCII.GetBytes("Between scans")),
                Segment(0xDA, new byte[] { 1, 1, 0, 1, 63, 0 }), new byte[] { 0x7F, 255, 217 });
            var store = new JpegMetadataStore(file);
            store.WriteEntries(new[] { new byte[] { 7, 8, 9 } });
            byte[] encoded = store.ToArray();
            Assert.Equal(new byte[] { 7, 8, 9 }, Assert.Single(new JpegMetadataStore(encoded).ReadEntries()));
            using (var picture = Image.Load<Rgb24>(encoded))
            {
                Assert.Equal(8, picture.Width);
                Assert.Equal(8, picture.Height);
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                        Assert.Equal(new Rgb24(128, 128, 128), picture[x, y]);
            }
            store.WriteEntries(Array.Empty<byte[]>());
            Assert.Equal(file, store.ToArray());
        }
    }
}
