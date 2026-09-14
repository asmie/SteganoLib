using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using SteganoLib.Selection;
using SteganoLib.Sharing;
using Xunit;

namespace SteganoLib.Test
{
    public class SharingTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x66 });

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static Algorithms.LSB Lsb() => new(new KeyedPermutationSelector(Key));

        private static List<Image<Rgba32>> Covers(int count, int width = 64, int height = 48)
        {
            return Enumerable.Range(0, count).Select(i => JpegImageTests.TestPicture(width, height, 500 + i)).ToList();
        }

        private static void DisposeAll(IEnumerable<Image<Rgba32>> images)
        {
            foreach (var image in images) image.Dispose();
        }

        // ---------- Shamir ----------

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 3)]
        [InlineData(2, 2)]
        [InlineData(2, 3)]
        [InlineData(3, 5)]
        [InlineData(5, 5)]
        [InlineData(10, 255)]
        public void Shamir_AnyThresholdSubsetRecovers(int threshold, int count)
        {
            var secret = Random(64, threshold * 100 + count);
            var shares = new ShamirSecretSharing(threshold, count).Split(secret);

            Assert.Equal(count, shares.Count);
            Assert.Equal(Enumerable.Range(1, count), shares.Select(s => s.Index));
            Assert.All(shares, s => Assert.Equal(threshold, s.Threshold));
            Assert.All(shares, s => Assert.Equal(64, s.Data.Length));

            var random = new Random(count);
            for (int trial = 0; trial < 5; trial++)
            {
                var subset = shares.OrderBy(_ => random.Next()).Take(threshold).ToList();
                Assert.Equal(secret, ShamirSecretSharing.Combine(subset));
            }
            Assert.Equal(secret, ShamirSecretSharing.Combine(shares)); // extra shares are fine
        }

        [Fact]
        public void Shamir_FewerThanThreshold_RevealsNothing()
        {
            var sharing = new ShamirSecretSharing(2, 3);
            var secret = new byte[] { 0x42 };

            // With one share of a 2-of-3 split the share byte is uniformly random.
            var seen = new HashSet<byte>();
            for (int i = 0; i < 300; i++)
                seen.Add(sharing.Split(secret)[0].Data[0]);
            Assert.True(seen.Count > 150, $"only {seen.Count} distinct share values in 300 splits");

            var shares = sharing.Split(Random(10, 1));
            Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(new[] { shares[0] }));
        }

        [Fact]
        public void Shamir_RejectsMismatchedShares()
        {
            var a = new ShamirSecretSharing(2, 3).Split(Random(8, 2));
            var b = new ShamirSecretSharing(3, 3).Split(Random(8, 3));
            var c = new ShamirSecretSharing(2, 3).Split(Random(9, 4));

            Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(new[] { a[0], a[0] }));
            Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(new[] { a[0], b[1] }));
            Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(new[] { a[0], c[1] }));
            Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(Array.Empty<Share>()));
            Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(new Share[] { a[0], null }));
            Assert.Throws<ArgumentNullException>(() => ShamirSecretSharing.Combine(null));

            // Shares from two different splits combine to garbage, not to either secret.
            var other = new ShamirSecretSharing(2, 3).Split(Random(8, 5));
            Assert.NotEqual(Random(8, 2), ShamirSecretSharing.Combine(new[] { a[0], other[1] }));
        }

        [Fact]
        public void Shamir_ParameterChecks_AndEmptySecret()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShamirSecretSharing(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShamirSecretSharing(4, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShamirSecretSharing(1, 256));
            Assert.Throws<ArgumentNullException>(() => new ShamirSecretSharing(2, 3).Split(null));

            var shares = new ShamirSecretSharing(2, 2).Split(Array.Empty<byte>());
            Assert.All(shares, s => Assert.Empty(s.Data));
            Assert.Empty(ShamirSecretSharing.Combine(shares));
        }

        [Fact]
        public void Share_Serialization()
        {
            var share = new Share(3, 7, new byte[] { 1, 2, 3 });
            var bytes = share.ToBytes();

            Assert.Equal(new byte[] { 3, 7, 1, 2, 3 }, bytes);
            var parsed = Share.FromBytes(bytes);
            Assert.Equal(3, parsed.Threshold);
            Assert.Equal(7, parsed.Index);
            Assert.Equal(share.Data, parsed.Data);

            Assert.Null(Share.TryParse(new byte[] { 3 }));
            Assert.Null(Share.TryParse(new byte[] { 0, 7 }));
            Assert.Null(Share.TryParse(new byte[] { 3, 0 }));
            Assert.Null(Share.TryParse(null));
            Assert.Throws<ArgumentException>(() => Share.FromBytes(new byte[] { 0, 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Share(2, 0, new byte[1]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Share(2, 256, new byte[1]));
        }

        // ---------- shared coding ----------

        [Fact]
        public void Coding_AnyTwoOfThreeImagesRecover_OneDoesNot()
        {
            var covers = Covers(3);
            try
            {
                var coder = new SharedCoding<Image<Rgba32>>(Lsb(), threshold: 2);
                var data = Random(200, 6);

                coder.EmbedBytes(data, covers);

                Assert.Equal(data, coder.ExtractBytes(covers));
                Assert.Equal(data, coder.ExtractBytes(new[] { covers[0], covers[1] }));
                Assert.Equal(data, coder.ExtractBytes(new[] { covers[2], covers[0] }));
                Assert.Equal(data, coder.ExtractBytes(new[] { covers[1], covers[2] }));
                Assert.Empty(coder.ExtractBytes(new[] { covers[1] }));

                // Every image holds a different share; none holds the payload itself.
                var raw = covers.Select(c => Lsb().ExtractBytes(c)).ToList();
                Assert.Equal(3, raw.Select(r => Convert.ToBase64String(r)).Distinct().Count());
                Assert.All(raw, r => Assert.NotEqual(data, r.Skip(Share.HeaderSize).ToArray()));
            }
            finally
            {
                DisposeAll(covers);
            }
        }

        [Fact]
        public void Coding_IgnoresCarriersWithoutShares_AndDuplicates()
        {
            var covers = Covers(4);
            try
            {
                var coder = new SharedCoding<Image<Rgba32>>(Lsb(), threshold: 2);
                var data = Random(50, 7);

                coder.EmbedBytes(data, new[] { covers[0], covers[1], covers[2] });

                Assert.Equal(data, coder.ExtractBytes(new[] { covers[3], covers[0], covers[3], covers[2] }));
                Assert.Empty(coder.ExtractBytes(new[] { covers[3], covers[1], covers[1] })); // one distinct share
                Assert.Empty(new SharedCoding<Image<Rgba32>>(Lsb(), threshold: 3).ExtractBytes(covers)); // wrong threshold
            }
            finally
            {
                DisposeAll(covers);
            }
        }

        [Fact]
        public void Coding_Capacity_IsSmallestCarrierMinusHeader()
        {
            var covers = new List<Image<Rgba32>> { JpegImageTests.TestPicture(64, 48, 1), JpegImageTests.TestPicture(32, 24, 2), JpegImageTests.TestPicture(64, 48, 3) };
            try
            {
                var coder = new SharedCoding<Image<Rgba32>>(Lsb(), threshold: 2);
                long small = Lsb().Capacity(covers[1]);

                Assert.Equal(small - Share.HeaderSize, coder.Capacity(covers));
                Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[small - 1], covers));
                coder.EmbedBytes(Random((int)(small - Share.HeaderSize), 8), covers);
                Assert.Equal(Random((int)(small - Share.HeaderSize), 8), coder.ExtractBytes(covers));
            }
            finally
            {
                DisposeAll(covers);
            }
        }

        [Fact]
        public void Coding_Pipeline_AuthenticatesRecoveredPayload()
        {
            var covers = Covers(4);
            var clean = Covers(1, 64, 48);
            try
            {
                var pipeline = new StegoPipeline<IReadOnlyList<Image<Rgba32>>>(new SharedCoding<Image<Rgba32>>(Lsb(), threshold: 3));
                var data = Random(120, 9);

                pipeline.Embed(data, covers, Key);

                var result = pipeline.Extract(new[] { covers[3], covers[1], covers[0] }, Key);
                Assert.Equal(ExtractionStatus.Success, result.Status);
                Assert.Equal(data, result.Data);
                Assert.Equal(ExtractionStatus.NotFound, pipeline.Extract(new[] { covers[0], covers[1] }, Key).Status);
                Assert.Equal(ExtractionStatus.AuthenticationFailed, pipeline.Extract(covers, StegoKey.FromBytes(new byte[] { 0x67 })).Status);

                // A share from another split mixed in is rejected by the envelope rather than returned as garbage.
                pipeline.Embed(Random(120, 10), new[] { clean[0], covers[3], covers[2] }, Key);
                var mixed = pipeline.Extract(new[] { covers[0], covers[1], clean[0] }, Key);
                Assert.NotEqual(ExtractionStatus.Success, mixed.Status);
            }
            finally
            {
                DisposeAll(covers);
                DisposeAll(clean);
            }
        }

        [Fact]
        public void Coding_ArgumentChecks()
        {
            Assert.Throws<ArgumentNullException>(() => new SharedCoding<Image<Rgba32>>(null, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SharedCoding<Image<Rgba32>>(Lsb(), 0));
            var coder = new SharedCoding<Image<Rgba32>>(Lsb(), 3);
            var covers = Covers(2);
            try
            {
                Assert.Throws<ArgumentException>(() => coder.EmbedBytes(new byte[1], covers));
                Assert.Throws<ArgumentException>(() => coder.EmbedBytes(new byte[1], new List<Image<Rgba32>>()));
                Assert.Throws<ArgumentException>(() => coder.ExtractBytes(new Image<Rgba32>[] { covers[0], null }));
                Assert.Throws<ArgumentNullException>(() => coder.EmbedBytes(null, covers));
                Assert.Throws<ArgumentNullException>(() => coder.Capacity(null));
            }
            finally
            {
                DisposeAll(covers);
            }
        }

        [Fact]
        public void FileHelpers_RoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var inputs = new List<string>();
                var outputs = new List<string>();
                for (int i = 0; i < 3; i++)
                {
                    using var picture = JpegImageTests.TestPicture(48, 32, 600 + i);
                    inputs.Add(Path.Combine(dir, $"cover{i}.png"));
                    outputs.Add(Path.Combine(dir, $"stego{i}.png"));
                    picture.SaveAsPng(inputs[i]);
                }
                var data = Random(80, 11);

                var coder = new SharedCoding<Image<Rgba32>>(Lsb(), 2);
                coder.EmbedBytes(data, inputs, outputs);
                Assert.Equal(data, coder.ExtractBytes(new[] { outputs[2], outputs[0] }));
                Assert.Empty(coder.ExtractBytes(inputs));

                var pipeline = new StegoPipeline<IReadOnlyList<Image<Rgba32>>>(coder);
                pipeline.Embed(data, inputs, outputs, Key);
                Assert.Equal(data, pipeline.Extract(new[] { outputs[1], outputs[2] }, Key).Data);
                Assert.Equal(ExtractionStatus.NotFound, pipeline.Extract(new[] { outputs[1] }, Key).Status);

                Assert.Throws<ArgumentException>(() => coder.EmbedBytes(data, inputs, outputs.Take(2).ToList()));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
