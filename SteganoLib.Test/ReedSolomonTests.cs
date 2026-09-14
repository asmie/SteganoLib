using System;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Metadata;
using SteganoLib.Payload;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class ReedSolomonTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x44 });

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        /// <summary>Corrupt <paramref name="count"/> distinct positions of <paramref name="block"/> with non-zero deltas.</summary>
        private static void Corrupt(byte[] block, int count, int seed, int from = 0, int to = -1)
        {
            if (to < 0) to = block.Length;
            var random = new Random(seed);
            foreach (int index in Enumerable.Range(from, to - from).OrderBy(_ => random.Next()).Take(count))
                block[index] ^= (byte)random.Next(1, 256);
        }

        // ---------- field ----------

        [Fact]
        public void Field_InversesAndPowers()
        {
            for (int a = 1; a < 256; a++)
            {
                Assert.Equal(1, GaloisField256.Multiply((byte)a, GaloisField256.Inverse((byte)a)));
                Assert.Equal((byte)a, GaloisField256.Divide(GaloisField256.Multiply((byte)a, 7), 7));
                Assert.Equal((byte)a, GaloisField256.Power(GaloisField256.LogOf((byte)a)));
            }
            Assert.Equal(1, GaloisField256.Power(0));
            Assert.Equal(1, GaloisField256.Power(255));
            Assert.Equal(2, GaloisField256.Power(1));
            Assert.Equal(GaloisField256.Power(254), GaloisField256.Power(-1));
            Assert.Equal(0x1D, GaloisField256.Power(8)); // x^8 reduces to the low bits of the primitive polynomial
            Assert.Throws<DivideByZeroException>(() => GaloisField256.Inverse(0));

            // (x + 1)(x + 2) = x^2 + 3x + 2, lowest degree first.
            Assert.Equal(new byte[] { 2, 3, 1 }, GaloisField256.MultiplyPolynomials(new byte[] { 1, 1 }, new byte[] { 2, 1 }));
            Assert.Equal(0, GaloisField256.Evaluate(new byte[] { 2, 3, 1 }, 1));
            Assert.Equal(0, GaloisField256.Evaluate(new byte[] { 2, 3, 1 }, 2));
        }

        // ---------- single blocks ----------

        [Theory]
        [InlineData(2, 1)]
        [InlineData(16, 100)]
        [InlineData(32, 223)]
        [InlineData(64, 50)]
        public void Block_IsACodeword(int parity, int dataLength)
        {
            var code = new ReedSolomonCode(parity);
            var block = new byte[dataLength + parity];
            code.EncodeBlock(Random(dataLength, parity), block);

            Assert.Equal(Random(dataLength, parity), block.Take(dataLength));
            // A codeword, read lowest degree first, vanishes at every root of the generator.
            var poly = block.Reverse().ToArray();
            for (int i = 0; i < parity; i++)
                Assert.Equal(0, GaloisField256.Evaluate(poly, GaloisField256.Power(i)));
        }

        [Theory]
        [InlineData(16, 239, 0)]
        [InlineData(16, 239, 1)]
        [InlineData(16, 239, 8)]
        [InlineData(32, 100, 16)]
        [InlineData(4, 10, 2)]
        [InlineData(254, 1, 127)]
        public void Block_CorrectsUpToHalfTheParity(int parity, int dataLength, int errors)
        {
            var code = new ReedSolomonCode(parity);
            var original = new byte[dataLength + parity];
            code.EncodeBlock(Random(dataLength, 7), original);

            var damaged = (byte[])original.Clone();
            Corrupt(damaged, errors, seed: errors + parity);

            Assert.True(code.TryDecodeBlock(damaged, out int corrected));
            Assert.Equal(errors, corrected);
            Assert.Equal(original, damaged);
        }

        [Theory]
        [InlineData(16, 239, 9)]
        [InlineData(32, 200, 17)]
        [InlineData(8, 40, 5)]
        public void Block_RejectsTooManyErrors(int parity, int dataLength, int errors)
        {
            var code = new ReedSolomonCode(parity);
            var block = new byte[dataLength + parity];
            code.EncodeBlock(Random(dataLength, 9), block);
            Corrupt(block, errors, seed: 99);

            Assert.False(code.TryDecodeBlock(block, out _));
        }

        [Fact]
        public void Block_ErrorsInParityAreCorrectedToo()
        {
            var code = new ReedSolomonCode(16);
            var original = new byte[50 + 16];
            code.EncodeBlock(Random(50, 3), original);
            var damaged = (byte[])original.Clone();
            Corrupt(damaged, 5, seed: 4, from: 50, to: 66);

            Assert.True(code.TryDecodeBlock(damaged, out int corrected));
            Assert.Equal(5, corrected);
            Assert.Equal(original, damaged);
        }

        [Fact]
        public void Block_ArgumentChecks()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReedSolomonCode(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReedSolomonCode(255));
            var code = new ReedSolomonCode(16);
            Assert.Equal(239, code.DataSize);
            Assert.Equal(8, code.MaxCorrectableErrors);
            Assert.Throws<ArgumentException>(() => code.EncodeBlock(new byte[240], new byte[256]));
            Assert.Throws<ArgumentException>(() => code.EncodeBlock(new byte[10], new byte[20]));
            Assert.Throws<ArgumentException>(() => code.TryDecodeBlock(new byte[16], out _));
        }

        // ---------- streams ----------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(223)]
        [InlineData(224)]
        [InlineData(1000)]
        public void Stream_RoundTrip_AndLengths(int length)
        {
            var code = new ReedSolomonCode(32);
            var data = Random(length, length);

            var encoded = code.Encode(data);

            Assert.Equal(code.EncodedLength(length), encoded.Length);
            Assert.Equal(length, code.MaxDataLength(encoded.Length));
            Assert.True(code.TryDecode(encoded, out var decoded, out int corrected));
            Assert.Equal(data, decoded);
            Assert.Equal(0, corrected);
        }

        [Fact]
        public void Stream_MaxDataLength_IsTight()
        {
            var code = new ReedSolomonCode(32);
            foreach (long capacity in new long[] { 0, 31, 32, 33, 255, 256, 300, 510, 511, 12345 })
            {
                long max = code.MaxDataLength(capacity);
                Assert.True(code.EncodedLength(max) <= capacity, $"capacity {capacity}: {max} bytes encode to {code.EncodedLength(max)}");
                Assert.True(code.EncodedLength(max + 1) > capacity, $"capacity {capacity}: {max + 1} bytes still fit");
            }
        }

        [Fact]
        public void Stream_CorrectsScatteredErrorsInEveryBlock()
        {
            var code = new ReedSolomonCode(16);
            var data = Random(1000, 11);
            var encoded = code.Encode(data);
            Corrupt(encoded, 15, seed: 12); // 5 blocks of 8 correctable errors; 15 at random never crowd one block

            Assert.True(code.TryDecode(encoded, out var decoded, out int corrected));
            Assert.Equal(15, corrected);
            Assert.Equal(data, decoded);
        }

        [Fact]
        public void Stream_Interleaving_TurnsABurstIntoScatteredErrors()
        {
            var data = Random(600, 13);
            const int burst = 20; // 3 blocks of 8 correctable errors: too much for one block, fine when shared

            var plain = new ReedSolomonCode(16) { Interleave = false };
            var encodedPlain = plain.Encode(data);
            for (int i = 100; i < 100 + burst; i++) encodedPlain[i] ^= 0x5A;
            Assert.False(plain.TryDecode(encodedPlain, out _, out _));

            var interleaved = new ReedSolomonCode(16);
            var encodedInterleaved = interleaved.Encode(data);
            for (int i = 100; i < 100 + burst; i++) encodedInterleaved[i] ^= 0x5A;
            Assert.True(interleaved.TryDecode(encodedInterleaved, out var decoded, out int corrected));
            Assert.Equal(burst, corrected);
            Assert.Equal(data, decoded);

            // Interleaving is a permutation; its inverse restores the stream even with a short last block.
            var stream = Random(700, 14);
            Assert.Equal(stream, ReedSolomonCode.Interleaver.Inverse(ReedSolomonCode.Interleaver.Forward(stream, 255), 255));
            Assert.NotEqual(stream, ReedSolomonCode.Interleaver.Forward(stream, 255));
        }

        [Fact]
        public void Stream_RejectsGarbageAndImpossibleLengths()
        {
            var code = new ReedSolomonCode(32);

            Assert.False(code.TryDecode(Random(300, 15), out _, out _));
            Assert.False(code.TryDecode(new byte[255 + 10], out _, out _)); // last block shorter than the parity
            Assert.True(code.TryDecode(Array.Empty<byte>(), out var empty, out _));
            Assert.Empty(empty);
        }

        // ---------- decorator ----------

        [Fact]
        public void Decorator_RepairsDamagedPixels()
        {
            using var cover = JpegImageTests.TestPicture(64, 48, 5);
            var inner = new Algorithms.LSB(new KeyedPermutationSelector(Key));
            var coder = new ErrorCorrectedAlgorithm<Image<Rgba32>>(inner, new ReedSolomonCode(32));

            long capacity = coder.Capacity(cover);
            Assert.Equal(new ReedSolomonCode(32).MaxDataLength(inner.Capacity(cover)), capacity);
            var data = Random((int)capacity, 16);
            coder.EmbedBytes(data, cover);
            Assert.Equal(data, coder.ExtractBytes(cover));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[capacity + 1], cover));

            // Flip the low bit of every channel in ten pixels: at most one payload bit each.
            var random = new Random(17);
            for (int i = 0; i < 10; i++)
            {
                int x = random.Next(cover.Width), y = random.Next(cover.Height);
                var p = cover[x, y];
                cover[x, y] = new Rgba32((byte)(p.R ^ 1), (byte)(p.G ^ 1), (byte)(p.B ^ 1), p.A);
            }

            Assert.NotEqual(data, inner.ExtractBytes(cover).Take(data.Length));
            Assert.True(coder.TryExtractBytes(cover, out var repaired, out int corrected));
            Assert.Equal(data, repaired);
            Assert.InRange(corrected, 1, 10);
        }

        [Fact]
        public void Decorator_RepairsDamagedMetadataChunk_AndGivesUpBeyondTheBound()
        {
            var cover = SamplePng();
            var coder = new ErrorCorrectedAlgorithm<MetadataCarrier>(new MetadataCoding(), new ReedSolomonCode(16));
            var data = Random(500, 18);

            var stego = coder.Embed(data, cover);

            var damaged = DamageChunk(stego, 10, seed: 19); // 3 blocks of 8 correctable errors
            Assert.True(coder.TryExtractBytes(MetadataCarrier.Load(damaged), out var repaired, out int corrected));
            Assert.Equal(data, repaired);
            Assert.Equal(10, corrected);

            var wrecked = DamageChunk(stego, 60, seed: 20);
            Assert.False(coder.TryExtractBytes(MetadataCarrier.Load(wrecked), out _, out _));
            Assert.Empty(coder.ExtractBytes(MetadataCarrier.Load(wrecked)));
        }

        [Fact]
        public void Decorator_InsidePipeline_ProtectsTheEnvelope()
        {
            var cover = SamplePng();
            var pipeline = new StegoPipeline<MetadataCarrier>(new ErrorCorrectedAlgorithm<MetadataCarrier>(new MetadataCoding(), new ReedSolomonCode(16)));
            var data = Random(300, 21);

            var stego = pipeline.Embed(data, cover, Key);

            var damaged = DamageChunk(stego, 7, seed: 22); // 2 blocks of 8 correctable errors
            var result = pipeline.ExtractFromBytes(damaged, Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);

            var wrecked = DamageChunk(stego, 80, seed: 23);
            Assert.Equal(ExtractionStatus.NotFound, pipeline.ExtractFromBytes(wrecked, Key).Status);
        }

        [Fact]
        public void Decorator_ArgumentChecks()
        {
            Assert.Throws<ArgumentNullException>(() => new ErrorCorrectedAlgorithm<MetadataCarrier>(null));
            Assert.Throws<ArgumentNullException>(() => new ErrorCorrectedAlgorithm<MetadataCarrier>(new MetadataCoding(), null));
            var coder = new ErrorCorrectedAlgorithm<MetadataCarrier>(new MetadataCoding());
            Assert.IsType<ReedSolomonCode>(coder.Code);
            Assert.Throws<ArgumentNullException>(() => coder.EmbedBytes(null, MetadataCarrier.Load(SamplePng())));
            Assert.Throws<ArgumentNullException>(() => coder.ExtractBytes(null));
        }

        private static byte[] SamplePng()
        {
            using var picture = JpegImageTests.TestPicture(16, 16, 2);
            using var stream = new System.IO.MemoryStream();
            picture.SaveAsPng(stream);
            return stream.ToArray();
        }

        /// <summary>Corrupt bytes inside the payload chunk of a PNG produced by the metadata carrier.</summary>
        private static byte[] DamageChunk(byte[] png, int errors, int seed)
        {
            var store = new PngMetadataStore(png);
            var chunk = store.Chunks.Single(c => c.Type == PngMetadataStore.DefaultChunkType);
            Corrupt(chunk.Data, errors, seed);
            return store.ToArray();
        }
    }
}
