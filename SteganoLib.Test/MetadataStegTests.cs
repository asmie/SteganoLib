using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Metadata;
using SteganoLib.Payload;
using Xunit;

namespace SteganoLib.Test
{
    public class MetadataStegTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x5A });
        private static readonly StegoKey OtherKey = StegoKey.FromBytes(new byte[] { 0x5B });

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static byte[] SamplePng(int width = 32, int height = 24)
        {
            using var picture = JpegImageTests.TestPicture(width, height, 3);
            using var stream = new MemoryStream();
            picture.SaveAsPng(stream);
            return stream.ToArray();
        }

        private static byte[] SampleJpeg()
        {
            using var picture = JpegImageTests.TestPicture(48, 40, 4);
            using var stream = new MemoryStream();
            picture.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 80 });
            return stream.ToArray();
        }

        /// <summary>A baseline file whose frame marker is rewritten to SOF2, as a progressive JPEG would carry.</summary>
        private static byte[] ProgressiveHeaderJpeg()
        {
            var jpeg = SampleJpeg();
            for (int i = 2; i < jpeg.Length - 1; i++)
            {
                if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xC0)
                {
                    jpeg[i + 1] = 0xC2;
                    return jpeg;
                }
            }
            throw new InvalidOperationException("No SOF0 marker found.");
        }

        private static byte[] SampleWav() => PcmAudioTests.Synthetic(2000, 2, 16).ToArray();

        /// <summary>A 32-bit float WAVE that <see cref="PcmAudio"/> refuses but the metadata store handles.</summary>
        private static byte[] FloatWav(int frames = 100)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((ushort)3); // IEEE float
            writer.Write((ushort)1);
            writer.Write(8000);
            writer.Write(8000 * 4);
            writer.Write((ushort)4);
            writer.Write((ushort)32);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(frames * 4);
            for (int i = 0; i < frames; i++)
                writer.Write((float)Math.Sin(i * 0.1));
            writer.Flush();
            var bytes = stream.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
            return bytes;
        }

        /// <summary>Insert a hand-built tEXt chunk before IEND.</summary>
        private static byte[] InsertTextChunk(byte[] png, string keyword, string text)
        {
            var body = Encoding.Latin1.GetBytes(keyword + "\0" + text);
            var type = Encoding.ASCII.GetBytes("tEXt");
            using var stream = new MemoryStream();
            stream.Write(png, 0, png.Length - 12);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
            stream.Write(length);
            stream.Write(type);
            stream.Write(body);
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(type, body));
            stream.Write(crc);
            stream.Write(png, png.Length - 12, 12);
            return stream.ToArray();
        }

        private static byte[] Sample(string format) => format switch
        {
            "png" => SamplePng(),
            "jpeg" => SampleJpeg(),
            _ => SampleWav(),
        };

        private static MetadataFormat Expected(string format) => format switch
        {
            "png" => MetadataFormat.Png,
            "jpeg" => MetadataFormat.Jpeg,
            _ => MetadataFormat.Wav,
        };

        public static TheoryData<string> Formats => new() { "png", "jpeg", "wav" };

        public static TheoryData<string, int> FormatsAndLengths
        {
            get
            {
                var data = new TheoryData<string, int>();
                foreach (string format in new[] { "png", "jpeg", "wav" })
                    foreach (int length in new[] { 0, 1, 1000 })
                        data.Add(format, length);
                return data;
            }
        }

        private static void AssertPixelsEqual(byte[] expectedFile, byte[] actualFile)
        {
            using var expected = Image.Load<Rgba32>(expectedFile);
            using var actual = Image.Load<Rgba32>(actualFile);
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert.True(expected[x, y] == actual[x, y], $"Pixel ({x},{y}) differs.");
        }

        // ---------- every format ----------

        [Theory]
        [MemberData(nameof(FormatsAndLengths))]
        public void RoundTrip(string format, int length)
        {
            var cover = Sample(format);
            var data = Random(length, length + 1);
            var coder = new MetadataCoding();

            Assert.Equal(Expected(format), MetadataCarrier.Detect(cover));

            var stego = coder.Embed(data, cover);

            Assert.Equal(Expected(format), MetadataCarrier.Detect(stego));
            Assert.Equal(data, coder.ExtractFromBytes(stego));
            Assert.True(stego.Length > cover.Length + length);
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void CleanCover_ExtractsEmpty_AndSurvivesLoadSaveUnchanged(string format)
        {
            var cover = Sample(format);

            Assert.Empty(new MetadataCoding().ExtractFromBytes(cover));
            Assert.Equal(cover, MetadataCarrier.Load(cover).ToArray());
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void EmbedTwice_ReplacesPayload(string format)
        {
            var coder = new MetadataCoding();
            var first = coder.Embed(Random(500, 1), Sample(format));
            var second = coder.Embed(Random(20, 2), first);

            var carrier = MetadataCarrier.Load(second);
            Assert.Single(carrier.Store.ReadEntries());
            Assert.Equal(Random(20, 2), coder.ExtractBytes(carrier));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void Remove_RestoresOriginalBytes(string format)
        {
            var cover = Sample(format);
            var coder = new MetadataCoding();

            var carrier = MetadataCarrier.Load(coder.Embed(Random(300, 3), cover));
            coder.Remove(carrier);

            Assert.Equal(cover, carrier.ToArray());
            Assert.Empty(coder.ExtractBytes(carrier));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void Pipeline_RoundTrip_WrongKeyRejected(string format)
        {
            var cover = Sample(format);
            var data = Random(64, 4);
            var pipeline = new StegoPipeline<MetadataCarrier>(new MetadataCoding());

            var stego = pipeline.Embed(data, cover, Key);

            var result = pipeline.ExtractFromBytes(stego, Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);
            Assert.Equal(ExtractionStatus.AuthenticationFailed, pipeline.ExtractFromBytes(stego, OtherKey).Status);
            Assert.Equal(ExtractionStatus.NotFound, pipeline.ExtractFromBytes(cover, Key).Status);
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void FileAndStreamHelpers(string format)
        {
            var input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllBytes(input, Sample(format));
                var data = Random(40, 5);
                var coder = new MetadataCoding();

                coder.EmbedBytes(data, input, output);
                Assert.Equal(data, coder.ExtractBytes(output));
                using (var stream = File.OpenRead(output))
                    Assert.Equal(data, coder.ExtractBytes(stream));

                var pipeline = new StegoPipeline<MetadataCarrier>(coder);
                pipeline.Embed(data, input, output, Key);
                Assert.Equal(data, pipeline.Extract(output, Key).Data);

                using var inStream = File.OpenRead(input);
                using var outStream = new MemoryStream();
                pipeline.Embed(data, inStream, outStream, Key);
                outStream.Position = 0;
                Assert.Equal(data, pipeline.Extract(outStream, Key).Data);
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }

        // ---------- carrier ----------

        [Fact]
        public void Carrier_UnknownSignature_Rejected()
        {
            var garbage = Encoding.ASCII.GetBytes("hello world, this is not a container");

            Assert.Null(MetadataCarrier.Detect(garbage));
            Assert.Throws<NotSupportedException>(() => MetadataCarrier.Load(garbage));
            Assert.Throws<InvalidDataException>(() => new PngMetadataStore(garbage));
            Assert.Throws<InvalidDataException>(() => new JpegMetadataStore(garbage));
            Assert.Throws<InvalidDataException>(() => new WavMetadataStore(garbage));
        }

        [Fact]
        public void Coding_ArgumentChecks()
        {
            var coder = new MetadataCoding();
            var carrier = MetadataCarrier.Load(SamplePng());

            Assert.Throws<ArgumentOutOfRangeException>(() => coder.MaxEntries = 0);
            Assert.Throws<ArgumentNullException>(() => coder.EmbedBytes(null, carrier));
            Assert.Throws<ArgumentNullException>(() => coder.EmbedBytes(new byte[1], null));
            Assert.Throws<ArgumentNullException>(() => coder.ExtractBytes(null));
            Assert.Throws<ArgumentNullException>(() => new MetadataCarrier(null));
        }

        // ---------- PNG ----------

        [Fact]
        public void Png_DecodesPixelIdentical_ChunkSitsBeforeIend()
        {
            var cover = SamplePng();
            var data = Random(777, 6);

            var stego = new MetadataCoding().Embed(data, cover);

            AssertPixelsEqual(cover, stego);
            var store = new PngMetadataStore(stego);
            Assert.Equal("IHDR", store.Chunks[0].Type);
            Assert.Equal("meTa", store.Chunks[^2].Type);
            Assert.Equal("IEND", store.Chunks[^1].Type);
            Assert.Equal(data, store.Chunks[^2].Data);
            Assert.True(store.Chunks[^2].IsAncillary);
        }

        [Theory]
        [InlineData("tEXt")]
        [InlineData("zTXt")]
        [InlineData("iTXt")]
        public void Png_TextModes_RoundTrip_AndReadableAsMetadata(string chunkType)
        {
            var options = new MetadataOptions { PngChunkType = chunkType, PngKeyword = "Comment" };
            var data = Random(300, 7);
            var coder = new MetadataCoding();

            var stego = coder.Embed(data, SamplePng(), options);

            Assert.Equal(data, coder.ExtractFromBytes(stego, options));
            Assert.Empty(coder.ExtractFromBytes(stego)); // default chunk type sees nothing

            using var image = Image.Load<Rgba32>(stego);
            var text = image.Metadata.GetPngMetadata().TextData.Single(t => t.Keyword == "Comment");
            Assert.Equal(Convert.ToBase64String(data), text.Value);
        }

        [Fact]
        public void Png_TextMode_LeavesOtherKeywordsAlone()
        {
            // A cover that already carries two tEXt chunks: a foreign keyword and our keyword with plain text.
            var cover = SamplePng(16, 16);
            cover = InsertTextChunk(cover, "Author", "someone");
            cover = InsertTextChunk(cover, "Comment", "not base64!");
            var options = new MetadataOptions { PngChunkType = "tEXt" };
            var coder = new MetadataCoding();

            Assert.Empty(coder.ExtractFromBytes(cover, options)); // foreign Comment is not valid Base64

            var stego = coder.Embed(Random(10, 9), cover, options);

            Assert.Equal(Random(10, 9), coder.ExtractFromBytes(stego, options));
            using var image = Image.Load<Rgba32>(stego);
            var texts = image.Metadata.GetPngMetadata().TextData;
            Assert.Contains(texts, t => t.Keyword == "Author" && t.Value == "someone");
            Assert.Single(texts, t => t.Keyword == "Comment");
        }

        [Theory]
        [InlineData("MeTa")]  // critical
        [InlineData("mETa")]  // public, not a text chunk
        [InlineData("meta")]  // reserved bit
        [InlineData("me1a")]
        [InlineData("meTaa")]
        [InlineData("eXIf")]
        public void Png_InvalidChunkType_Rejected(string chunkType)
        {
            Assert.Throws<ArgumentException>(() => new PngMetadataStore(SamplePng(), chunkType));
        }

        [Theory]
        [InlineData("")]
        [InlineData(" lead")]
        [InlineData("trail ")]
        [InlineData("tab\there")]
        public void Png_InvalidKeyword_Rejected(string keyword)
        {
            Assert.Throws<ArgumentException>(() => new PngMetadataStore(SamplePng(), "tEXt", keyword));
            Assert.Throws<ArgumentException>(() => new PngMetadataStore(SamplePng(), "tEXt", new string('k', 80)));
        }

        [Fact]
        public void Png_CorruptCrc_Rejected()
        {
            var png = SamplePng();
            png[16] ^= 0x01; // inside IHDR data

            var error = Assert.Throws<InvalidDataException>(() => new PngMetadataStore(png));
            Assert.Contains("IHDR", error.Message);
        }

        [Fact]
        public void Png_TruncatedFile_Rejected()
        {
            var png = SamplePng();
            Assert.Throws<InvalidDataException>(() => new PngMetadataStore(png.AsSpan(0, png.Length - 6).ToArray()));
        }

        [Fact]
        public void Crc32_KnownVector()
        {
            Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));
            Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("1234"), Encoding.ASCII.GetBytes("56789")));
        }

        // ---------- JPEG ----------

        [Fact]
        public void Jpeg_ProgressiveHeader_HandledVerbatim()
        {
            var cover = ProgressiveHeaderJpeg();
            var data = Random(321, 18);
            var coder = new MetadataCoding();

            Assert.Throws<NotSupportedException>(() => JpegImage.Load(cover));

            var stego = coder.Embed(data, cover);

            Assert.Equal(data, coder.ExtractFromBytes(stego));
            var carrier = MetadataCarrier.Load(stego);
            coder.Remove(carrier);
            Assert.Equal(cover, carrier.ToArray());
        }

        [Fact]
        public void Jpeg_DecodesPixelIdentical_SegmentAfterApp0()
        {
            var cover = SampleJpeg();
            var data = Random(1234, 10);

            var stego = new MetadataCoding().Embed(data, cover);

            AssertPixelsEqual(cover, stego);
            var store = new JpegMetadataStore(stego);
            var segments = store.Segments;
            int index = segments.ToList().FindIndex(s => s.Marker == 0xE9);
            Assert.True(index >= 0);
            Assert.All(segments.Take(index), s => Assert.True(JpegMarker.IsApp(s.Marker), "only APPn segments precede the entry"));
            Assert.False(JpegMarker.IsApp(segments[index + 1].Marker));
            Assert.Equal(Encoding.ASCII.GetBytes("META\0"), segments[index].Payload.AsSpan(0, 5).ToArray());
            Assert.Equal(data, segments[index].Payload.AsSpan(5).ToArray());
        }

        [Fact]
        public void Jpeg_CoefficientDecoderStillLoads_AndKeepsTheSegment()
        {
            var data = Random(99, 11);
            var stego = new MetadataCoding().Embed(data, SampleJpeg());

            var image = JpegImage.Load(stego);
            var segment = image.Segments.Single(s => s.Marker == 0xE9);
            Assert.Equal(data, segment.Payload.AsSpan(5).ToArray());

            // A coefficient-level re-encode keeps the metadata segment too.
            Assert.Equal(data, new MetadataCoding().ExtractFromBytes(image.ToArray()));
        }

        [Fact]
        public void Jpeg_LargePayload_SplitsAcrossSegments()
        {
            var data = Random(200_000, 12);
            var coder = new MetadataCoding();

            var stego = coder.Embed(data, SampleJpeg());

            var store = new JpegMetadataStore(stego);
            Assert.Equal(65533 - 5, store.MaxEntrySize);
            Assert.Equal(4, store.ReadEntries().Count);
            Assert.All(store.Segments, s => Assert.True(s.Payload.Length <= 65533));
            Assert.Equal(data, coder.ExtractFromBytes(stego));
            AssertPixelsEqual(SampleJpeg(), stego);
        }

        [Fact]
        public void Jpeg_Capacity_BoundedByMaxEntries()
        {
            IStegAlgorithm<MetadataCarrier> coder = new MetadataCoding { MaxEntries = 2 };
            var carrier = MetadataCarrier.Load(SampleJpeg());
            long capacity = coder.Capacity(carrier);

            Assert.Equal(2L * (65533 - 5), capacity);
            Assert.True(coder.IsPossibleToEmbed(capacity, carrier));
            Assert.False(coder.IsPossibleToEmbed(capacity + 1, carrier));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[capacity + 1], carrier));

            coder.EmbedBytes(Random((int)capacity, 13), carrier);
            Assert.Equal(Random((int)capacity, 13), coder.ExtractBytes(carrier));
        }

        [Fact]
        public void Jpeg_CustomMarkerAndEmptyIdentifier()
        {
            var options = new MetadataOptions { JpegAppNumber = 12, JpegIdentifier = string.Empty };
            var data = Random(50, 14);
            var coder = new MetadataCoding();

            var stego = coder.Embed(data, SampleJpeg(), options);

            Assert.Equal(data, coder.ExtractFromBytes(stego, options));
            Assert.Empty(coder.ExtractFromBytes(stego));
            var store = new JpegMetadataStore(stego, 12, string.Empty);
            Assert.Equal(65533, store.MaxEntrySize);
            Assert.Equal(0xEC, store.Segments.Single(s => s.Payload.AsSpan().SequenceEqual(data)).Marker);
        }

        [Fact]
        public void Jpeg_InvalidOptions_Rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new JpegMetadataStore(SampleJpeg(), 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => new JpegMetadataStore(SampleJpeg(), -1));
            Assert.Throws<ArgumentException>(() => new JpegMetadataStore(SampleJpeg(), 9, "bad\nid"));
            Assert.Throws<ArgumentException>(() => new JpegMetadataStore(SampleJpeg(), 9, new string('x', 65)));
        }

        [Fact]
        public void Jpeg_CorruptHeader_Rejected()
        {
            // SOI, then an APP0 segment with a two-byte body and nothing after it.
            var noScan = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00 };
            Assert.Throws<InvalidDataException>(() => new JpegMetadataStore(noScan));

            // Segment length below the two bytes the length field itself occupies.
            var badLength = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x01, 0x00 };
            Assert.Throws<InvalidDataException>(() => new JpegMetadataStore(badLength));

            // Length running past the end of the file.
            var truncated = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x10, 0x00, 0x00 };
            Assert.Throws<InvalidDataException>(() => new JpegMetadataStore(truncated));
        }

        // ---------- WAV ----------

        [Fact]
        public void Wav_SamplesIdentical_ChunkBeforeData()
        {
            var original = PcmAudioTests.Synthetic(1500, 2, 24);
            var data = Random(333, 15);

            var stego = new MetadataCoding().Embed(data, original.ToArray());

            var loaded = PcmAudio.Load(stego);
            Assert.Equal(original.Samples, loaded.Samples);
            Assert.Equal(original.BitsPerSample, loaded.BitsPerSample);
            var chunk = Assert.Single(loaded.ExtraChunks);
            Assert.Equal("JUNK", chunk.Id);
            Assert.Equal(data, chunk.Payload);

            var store = new WavMetadataStore(stego);
            Assert.Equal(new[] { "fmt ", "JUNK", "data" }, store.Chunks.Select(c => c.Id));
            Assert.Equal(0, stego.Length % 2);
            Assert.Equal(stego.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(stego.AsSpan(4)));
        }

        [Fact]
        public void Wav_FloatFormat_HandledByteForByte()
        {
            var cover = FloatWav();
            var data = Random(64, 16);
            var coder = new MetadataCoding();

            Assert.Throws<NotSupportedException>(() => PcmAudio.Load(cover));

            var stego = coder.Embed(data, cover);

            Assert.Equal(data, coder.ExtractFromBytes(stego));
            var carrier = MetadataCarrier.Load(stego);
            coder.Remove(carrier);
            Assert.Equal(cover, carrier.ToArray());
        }

        [Fact]
        public void Wav_CustomChunkId_RoundTrip()
        {
            var options = new MetadataOptions { WavChunkId = "ab12" };
            var data = Random(20, 17);
            var coder = new MetadataCoding();

            var stego = coder.Embed(data, SampleWav(), options);

            Assert.Equal(data, coder.ExtractFromBytes(stego, options));
            Assert.Empty(coder.ExtractFromBytes(stego));
        }

        [Theory]
        [InlineData("data")]
        [InlineData("fmt ")]
        [InlineData("LIST")]
        [InlineData("RIFF")]
        [InlineData("abc")]
        [InlineData("abéd")]
        public void Wav_InvalidChunkId_Rejected(string id)
        {
            Assert.Throws<ArgumentException>(() => new WavMetadataStore(SampleWav(), id));
        }

        [Fact]
        public void Wav_Capacity_ReflectsRiffSizeField()
        {
            var cover = SampleWav();
            var store = new WavMetadataStore(cover);

            Assert.Equal(uint.MaxValue - cover.Length, store.MaxTotalSize);
            Assert.Equal(store.MaxTotalSize, new MetadataCoding().Capacity(new MetadataCarrier(store)));
        }
    }
}
