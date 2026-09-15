using System;
using System.Collections;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SteganoLib.Audio;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Metadata;
using Xunit;

namespace SteganoLib.Test
{
    public class MetadataOwnershipTests
    {
        public static TheoryData<string> Formats => new() { "png", "tEXt", "zTXt", "iTXt", "jpeg", "wav" };

        private static byte[] Cover(string format)
        {
            if (format == "jpeg") return JpegImageTests.SampleJpeg();
            if (format == "wav") return PcmAudioTests.Synthetic(100, 1, 16).ToArray();
            using var picture = JpegImageTests.TestPicture(8, 8, 1);
            using var stream = new MemoryStream();
            picture.SaveAsPng(stream);
            return stream.ToArray();
        }

        private static IMetadataStore Store(string format, byte[] file = null) => format switch
        {
            "jpeg" => new JpegMetadataStore(file ?? Cover(format)),
            "wav" => new WavMetadataStore(file ?? Cover(format)),
            _ => new PngMetadataStore(file ?? Cover(format), format == "png" ? "meTa" : format),
        };

        private static IList View(IMetadataStore store) => store switch
        {
            PngMetadataStore png => (IList)png.Chunks,
            JpegMetadataStore jpeg => (IList)jpeg.Segments,
            WavMetadataStore wav => (IList)wav.Chunks,
            _ => throw new InvalidOperationException(),
        };

        [Theory]
        [MemberData(nameof(Formats))]
        public void WritingCopiesEveryInputBuffer(string format)
        {
            var store = Store(format);
            byte[] input = { 1, 2, 3 };
            var entries = new[] { input, input };
            store.WriteEntries(entries);
            byte[] before = store.ToArray();
            Array.Fill(input, (byte)9);
            entries[0] = new byte[] { 42 };
            Assert.Equal(before, store.ToArray());
            Assert.All(store.ReadEntries(), entry => Assert.Equal(new byte[] { 1, 2, 3 }, entry));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void ReadingReturnsIndependentBuffers(string format)
        {
            var store = Store(format);
            store.WriteEntries(new[] { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 } });
            byte[] before = store.ToArray();
            var read = store.ReadEntries();
            read[0][0] = 99;
            read[1][0] = 88;
            Assert.Equal(before, store.ToArray());
            var again = store.ReadEntries();
            Assert.Equal(new byte[] { 1, 2, 3 }, again[0]);
            Assert.Equal(new byte[] { 4, 5, 6 }, again[1]);
            Assert.NotSame(read[0], again[0]);
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void ConstructionDoesNotRetainContainerInput(string format)
        {
            byte[] input = Cover(format);
            var store = Store(format, input);
            byte[] before = store.ToArray();
            Array.Clear(input);
            Assert.Equal(before, store.ToArray());
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void CollectionViewsAreReadOnlyAndReflectWrites(string format)
        {
            var store = Store(format);
            IList view = View(store);
            int count = view.Count;
            byte[] before = store.ToArray();
            Assert.True(view.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => view.Clear());
            Assert.Throws<NotSupportedException>(() => view[0] = view[0]);
            Assert.Equal(before, store.ToArray());
            store.WriteEntries(new[] { new byte[] { 1 } });
            Assert.Equal(count + 1, view.Count);
            store.WriteEntries(Array.Empty<byte[]>());
            Assert.Equal(count, view.Count);
            Assert.Same(view, View(store));
        }

        [Theory]
        [MemberData(nameof(Formats))]
        public void InvalidWritesPreserveExistingEntries(string format)
        {
            var store = Store(format);
            store.WriteEntries(new[] { new byte[] { 1, 2, 3 } });
            byte[] before = store.ToArray();
            Assert.Throws<ArgumentNullException>(() => store.WriteEntries(null));
            Assert.Throws<ArgumentException>(() => store.WriteEntries(new byte[][] { new byte[] { 4 }, null }));
            Assert.Equal(before, store.ToArray());
        }

        [Theory]
        [InlineData("png")]
        [InlineData("jpeg")]
        [InlineData("wav")]
        public void LowLevelPayloadArraysRemainEditableWithoutAliasingOtherEntries(string format)
        {
            var store = Store(format);
            byte[] input = { 1, 2, 3 };
            store.WriteEntries(new[] { input, input });
            byte[] body = store switch
            {
                PngMetadataStore png => png.Chunks.First(c => c.Type == "meTa").Data,
                JpegMetadataStore jpeg => jpeg.Segments.First(s => s.Marker == jpeg.Marker).Payload,
                WavMetadataStore wav => wav.Chunks.First(c => c.Id == wav.ChunkId).Payload,
                _ => throw new InvalidOperationException(),
            };
            body[^1] = 7;
            Assert.Equal(new byte[] { 1, 2, 7 }, store.ReadEntries()[0]);
            Assert.Equal(input, store.ReadEntries()[1]);
            Assert.Equal(new byte[] { 1, 2, 7 }, Store(format, store.ToArray()).ReadEntries()[0]);
        }

        [Fact]
        public void PcmCloneOwnsItsMetadataBuffers()
        {
            var store = Store("wav");
            store.WriteEntries(new[] { new byte[] { 1, 2, 3 } });
            var original = PcmAudio.Load(store.ToArray());
            var clone = original.Clone();
            byte[] before = original.ToArray();
            Assert.Equal(before, clone.ToArray());
            clone.ExtraChunks[0].Payload[0] = 42;
            clone.Samples[0]++;
            Assert.Equal(before, original.ToArray());
            original.ExtraChunks[0].Payload[1] = 43;
            Assert.Equal(2, clone.ExtraChunks[0].Payload[1]);
            Assert.Throws<NotSupportedException>(() => ((IList)original.ExtraChunks).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList)clone.ExtraChunks).Clear());
        }

        [Fact]
        public void JpegCloneOwnsItsMetadataBuffers()
        {
            var store = Store("jpeg");
            store.WriteEntries(new[] { new byte[] { 1, 2, 3 } });
            var original = JpegImage.Load(store.ToArray());
            var clone = original.Clone();
            byte[] before = original.ToArray();
            Assert.Equal(before, clone.ToArray());
            var originalSegment = original.Segments.Single(s => s.Marker == 0xE9);
            var cloneSegment = clone.Segments.Single(s => s.Marker == 0xE9);
            cloneSegment.Payload[^1] = 42;
            Assert.Equal(before, original.ToArray());
            originalSegment.Payload[^2] = 43;
            Assert.Equal(2, cloneSegment.Payload[^2]);
            Assert.Throws<NotSupportedException>(() => ((IList)original.Segments).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList)clone.Segments).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList)clone.Components).Clear());
        }

        [Theory]
        [InlineData("m1Ta")]
        [InlineData("méTa")]
        [InlineData("meT\0")]
        public void PngChunkRejectsNonAsciiLetters(string type)
        {
            Assert.Throws<ArgumentException>(() => new PngChunk(type, Array.Empty<byte>()));
        }

        [Fact]
        public void StandaloneChunksRetainExplicitBufferSharing()
        {
            byte[] data = { 1 };
            Assert.Same(data, new PngChunk("meTa", data).Data);
            Assert.Same(data, new RiffChunk("JUNK", data).Payload);
            Assert.Same(data, new JpegSegment(0xE9, data).Payload);
            Assert.Throws<ArgumentNullException>(() => new PngChunk(null, data));
            Assert.Throws<ArgumentNullException>(() => new RiffChunk(null, data));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HelpersRejectNullPayloadBeforeReadingInput(bool pipeline)
        {
            using var input = new MemoryStream(new byte[] { 1, 2, 3 });
            using var output = new MemoryStream();
            var algorithm = new MetadataCoding();
            var key = StegoKey.FromBytes(new byte[] { 1 });
            if (pipeline)
                Assert.Throws<ArgumentNullException>(() => new StegoPipeline<MetadataCarrier>(algorithm).Embed(null, input, output, key));
            else
                Assert.Throws<ArgumentNullException>(() => algorithm.EmbedBytes(null, input, output));
            Assert.Equal(0, input.Position);
            Assert.Equal(0, output.Length);
        }

        [Fact]
        public void PipelineHelpersRejectNullKeysBeforeReadingInput()
        {
            using var input = new MemoryStream(new byte[] { 1, 2, 3 });
            using var output = new MemoryStream();
            var pipeline = new StegoPipeline<MetadataCarrier>(new MetadataCoding());
            Assert.Throws<ArgumentNullException>(() => pipeline.Embed(new byte[] { 1 }, input, output, null));
            Assert.Throws<ArgumentNullException>(() => pipeline.Extract(input, null));
            Assert.Equal(0, input.Position);
            Assert.Equal(0, output.Length);
        }
    }
}
