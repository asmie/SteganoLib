using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Metadata;
using SteganoLib.Payload;
using SteganoLib.Selection;
using SteganoLib.Sharing;
using SteganoLib.Text;
using Xunit;

namespace SteganoLib.Test
{
    /// <summary>
    /// Round-trip and algebraic invariants checked over random inputs. Each property
    /// runs on many generated cases; a failure prints the shrunk counterexample.
    /// </summary>
    public class PropertyTests
    {
        // ---------- generators ----------

        private static Gen<byte[]> Bytes(int minLength, int maxLength)
        {
            return Gen.Choose(minLength, maxLength).SelectMany(n => Gen.ArrayOf(ArbMap.Default.GeneratorFor<byte>(), n));
        }

        private static Gen<StegoKey> Keys()
        {
            return Bytes(1, 40).Select(bytes => StegoKey.FromBytes(bytes));
        }

        private static Gen<ColorChannels> Channels()
        {
            return Gen.Choose(1, 7).Select(bits => (ColorChannels)bits);
        }

        private static Gen<Image<Rgba32>> Images(int minSide, int maxSide)
        {
            return Gen.Choose(minSide, maxSide).SelectMany(w => Gen.Choose(minSide, maxSide).SelectMany(h => Gen.Choose(0, int.MaxValue).Select(seed => JpegImageTests.TestPicture(w, h, seed))));
        }

        private static Gen<string> Prose(int minWords, int maxWords)
        {
            string[] words = { "the", "quick", "brown", "fox", "jumps", "over", "a", "lazy", "dog", "and", "seven", "pipers", "play", "on", "Cyrillic", "looks", "Latin" };
            return Gen.Choose(minWords, maxWords).SelectMany(n => Gen.ArrayOf(Gen.Elements(words), n)).SelectMany(ws =>
                Gen.Choose(3, 12).Select(perLine =>
                    string.Join("\n", ws.Select((w, i) => (w, i)).GroupBy(t => t.i / perLine).Select(g => string.Join(' ', g.Select(t => t.w)))) + "\n"));
        }

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        // ---------- images ----------

        [Property(MaxTest = 60)]
        public Property Lsb_RoundTrips_ForAnyChannelsBitsAndMode()
        {
            var cases = Images(8, 40).SelectMany(image =>
                Channels().SelectMany(channels =>
                Gen.Choose(1, 3).SelectMany(bits =>
                Gen.Elements(LsbEmbeddingMode.Match, LsbEmbeddingMode.Replace).SelectMany(mode =>
                Keys().SelectMany(key =>
                Gen.Choose(0, int.MaxValue).Select(seed => (image, channels, bits, mode, key, seed)))))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                using var image = c.image;
                var lsb = new Algorithms.LSB(new KeyedPermutationSelector(c.key)) { Channels = c.channels, BitsPerPixel = c.bits, EmbeddingMode = c.mode };
                long capacity = lsb.Capacity(image);
                var data = Random((int)Math.Min(capacity, new Random(c.seed).Next(0, (int)capacity + 1)), c.seed);

                lsb.EmbedBytes(data, image);
                return lsb.ExtractBytes(image).SequenceEqual(data);
            });
        }

        [Property(MaxTest = 40)]
        public Property Lsb_NeverMovesASampleByMoreThanOne_AndNeverTouchesAlpha()
        {
            var cases = Images(8, 32).SelectMany(image => Keys().SelectMany(key => Gen.Choose(0, int.MaxValue).Select(seed => (image, key, seed))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                using var image = c.image;
                using var cover = image.Clone();
                var lsb = new Algorithms.LSB(new KeyedPermutationSelector(c.key));
                lsb.EmbedBytes(Random((int)lsb.Capacity(image), c.seed), image);

                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        var a = cover[x, y];
                        var b = image[x, y];
                        if (Math.Abs(a.R - b.R) > 1 || Math.Abs(a.G - b.G) > 1 || Math.Abs(a.B - b.B) > 1 || a.A != b.A)
                            return false;
                    }
                }
                return true;
            });
        }

        [Property(MaxTest = 40)]
        public Property Lsb_WrongKey_NeverYieldsThePayload()
        {
            var cases = Images(32, 48).SelectMany(image => Keys().SelectMany(key => Keys().SelectMany(other => Gen.Choose(0, int.MaxValue).Select(seed => (image, key, other, seed)))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                using var image = c.image;
                var pipeline = new StegoPipeline<Image<Rgba32>>(new Algorithms.LSB(new KeyedPermutationSelector(c.key)));
                var data = Random((int)Math.Min(16, pipeline.Capacity(image)), c.seed);
                pipeline.Embed(data, image, c.key);

                var wrong = new StegoPipeline<Image<Rgba32>>(new Algorithms.LSB(new KeyedPermutationSelector(c.other))).Extract(image, c.other);
                return pipeline.Extract(image, c.key).IsSuccess && (wrong.IsSuccess ? wrong.Data.SequenceEqual(data) && SameKey(c.key, c.other) : true);
            });
        }

        private static bool SameKey(StegoKey a, StegoKey b) => a.Derive("compare", 16).SequenceEqual(b.Derive("compare", 16));

        // ---------- envelope ----------

        [Property(MaxTest = 100)]
        public Property Envelope_SealOpen_RoundTrips_AndRejectsOtherKeys(byte[] payload, bool compress)
        {
            return Prop.ForAll(Keys().ToArbitrary(), Keys().ToArbitrary(), (key, other) =>
            {
                payload ??= Array.Empty<byte>();
                var envelope = new PayloadEnvelope { Compress = compress };
                var sealedBytes = envelope.Seal(payload, key);

                var opened = envelope.Open(sealedBytes, key);
                bool roundTrip = opened.IsSuccess && opened.Data.SequenceEqual(payload) && sealedBytes.Length <= payload.Length + envelope.Overhead;
                bool rejected = SameKey(key, other) || envelope.Open(sealedBytes, other).Status == ExtractionStatus.AuthenticationFailed;
                return roundTrip && rejected;
            });
        }

        [Property(MaxTest = 100)]
        public Property Envelope_AnySingleByteChange_IsDetected(byte[] payload, PositiveInt position, byte delta)
        {
            return Prop.ForAll(Keys().ToArbitrary(), key =>
            {
                payload ??= Array.Empty<byte>();
                var envelope = new PayloadEnvelope();
                var sealedBytes = envelope.Seal(payload, key);
                int index = position.Get % sealedBytes.Length;
                sealedBytes[index] ^= (byte)(delta == 0 ? 1 : delta);
                return !envelope.Open(sealedBytes, key).IsSuccess;
            });
        }

        // ---------- coding ----------

        [Property(MaxTest = 200)]
        public bool Gf256_IsAField(byte a, byte b, byte c)
        {
            bool commutative = GaloisField256.Multiply(a, b) == GaloisField256.Multiply(b, a);
            bool associative = GaloisField256.Multiply(GaloisField256.Multiply(a, b), c) == GaloisField256.Multiply(a, GaloisField256.Multiply(b, c));
            bool distributive = GaloisField256.Multiply(a, (byte)(b ^ c)) == (byte)(GaloisField256.Multiply(a, b) ^ GaloisField256.Multiply(a, c));
            bool inverse = a == 0 || GaloisField256.Multiply(a, GaloisField256.Inverse(a)) == 1;
            bool division = b == 0 || GaloisField256.Multiply(GaloisField256.Divide(a, b), b) == a;
            return commutative && associative && distributive && inverse && division;
        }

        [Property(MaxTest = 100)]
        public Property ReedSolomon_CorrectsAnyErrorsUpToHalfTheParity()
        {
            var cases = Gen.Choose(1, 127).SelectMany(t =>
                Bytes(0, 700).SelectMany(data =>
                Gen.Choose(0, t).SelectMany(errors =>
                Gen.Choose(0, int.MaxValue).Select(seed => (parity: 2 * t, data, errors, seed)))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                var code = new ReedSolomonCode(c.parity);
                var encoded = code.Encode(c.data);
                if (encoded.Length != code.EncodedLength(c.data.Length) || code.MaxDataLength(encoded.Length) != c.data.Length)
                    return false;

                // Damage up to t bytes of one block: pick the block, then distinct positions inside it, in stream order.
                var random = new Random(c.seed);
                int blocks = (encoded.Length + ReedSolomonCode.BlockSize - 1) / ReedSolomonCode.BlockSize;
                var plain = new ReedSolomonCode(c.parity) { Interleave = false };
                var stream = plain.Encode(c.data);
                if (blocks > 0)
                {
                    int block = random.Next(blocks);
                    int start = block * ReedSolomonCode.BlockSize;
                    int length = Math.Min(ReedSolomonCode.BlockSize, stream.Length - start);
                    foreach (int offset in Enumerable.Range(0, length).OrderBy(_ => random.Next()).Take(Math.Min(c.errors, length)))
                        stream[start + offset] ^= (byte)random.Next(1, 256);
                }

                return plain.TryDecode(stream, out var decoded, out _) && decoded.SequenceEqual(c.data)
                    && code.TryDecode(encoded, out var clean, out int corrected) && clean.SequenceEqual(c.data) && corrected == 0;
            });
        }

        [Property(MaxTest = 100)]
        public bool Interleaver_IsInvertible(NonNegativeInt length, PositiveInt blockSize)
        {
            var stream = Random(length.Get % 2000, length.Get);
            int block = 1 + blockSize.Get % 300;
            return ReedSolomonCode.Interleaver.Inverse(ReedSolomonCode.Interleaver.Forward(stream, block), block).SequenceEqual(stream);
        }

        [Property(MaxTest = 60)]
        public Property Shamir_AnyThresholdSubsetRecovers_AndFewerDoNot()
        {
            var cases = Gen.Choose(1, 20).SelectMany(n =>
                Gen.Choose(1, n).SelectMany(k =>
                Bytes(0, 64).SelectMany(secret =>
                Gen.Choose(0, int.MaxValue).Select(seed => (n, k, secret, seed)))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                var shares = new ShamirSecretSharing(c.k, c.n).Split(c.secret);
                var random = new Random(c.seed);
                var subset = shares.OrderBy(_ => random.Next()).Take(c.k).ToList();
                bool recovers = ShamirSecretSharing.Combine(subset).SequenceEqual(c.secret);

                bool fewerRejected = c.k == 1 || Throws(() => ShamirSecretSharing.Combine(subset.Take(c.k - 1).ToList()));
                return recovers && fewerRejected;
            });
        }

        [Property(MaxTest = 60)]
        public Property FeistelPermutation_IsABijection()
        {
            var cases = Gen.Choose(1, 3000).SelectMany(domain => Bytes(FeistelPermutation.KeySize, FeistelPermutation.KeySize).Select(key => (domain, key)));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                var permutation = new FeistelPermutation(c.key, c.domain);
                var seen = new HashSet<long>();
                for (long i = 0; i < c.domain; i++)
                {
                    long p = permutation.Permute(i);
                    if (p < 0 || p >= c.domain || !seen.Add(p))
                        return false;
                }
                return seen.Count == c.domain;
            });
        }

        // ---------- other carriers ----------

        [Property(MaxTest = 40)]
        public Property TextCodings_RoundTrip()
        {
            var cases = Prose(20, 200).SelectMany(text => Keys().SelectMany(key => Gen.Choose(0, 2).SelectMany(method => Gen.Choose(0, int.MaxValue).Select(seed => (text, key, method, seed)))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                IStegAlgorithm<TextCarrier> coder = c.method switch
                {
                    0 => new ZeroWidthCoding(c.key),
                    1 => new WhitespaceCoding(c.key),
                    _ => new HomoglyphCoding(c.key),
                };
                var carrier = new TextCarrier(c.text);
                long capacity = coder.Capacity(carrier);
                var data = Random((int)Math.Min(capacity, new Random(c.seed).Next(0, (int)capacity + 1)), c.seed);

                coder.EmbedBytes(data, carrier);
                return coder.ExtractBytes(new TextCarrier(carrier.Text)).SequenceEqual(data);
            });
        }

        [Property(MaxTest = 40)]
        public Property AudioLsb_RoundTrips_ThroughWaveBytes()
        {
            var cases = Gen.Choose(50, 3000).SelectMany(frames =>
                Gen.Choose(1, 3).SelectMany(channels =>
                Gen.Elements(8, 16, 24, 32).SelectMany(bits =>
                Gen.Choose(1, 3).SelectMany(bitsPerSample =>
                Keys().SelectMany(key =>
                Gen.Choose(0, int.MaxValue).Select(seed => (frames, channels, bits, bitsPerSample, key, seed)))))));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                var audio = PcmAudioTests.Synthetic(c.frames, c.channels, c.bits, seed: c.seed);
                var lsb = new AudioLsb(new KeyedSampleSelector(c.key)) { BitsPerSample = c.bitsPerSample };
                long capacity = lsb.Capacity(audio);
                var data = Random((int)Math.Min(capacity, new Random(c.seed).Next(0, (int)capacity + 1)), c.seed);

                lsb.EmbedBytes(data, audio);
                return lsb.ExtractBytes(PcmAudio.Load(audio.ToArray())).SequenceEqual(data);
            });
        }

        [Property(MaxTest = 40)]
        public Property MetadataCarriers_RoundTrip_AndRestoreTheOriginal()
        {
            var cases = Gen.Elements("png", "jpeg", "wav").SelectMany(format => Bytes(0, 3000).Select(data => (format, data)));

            return Prop.ForAll(cases.ToArbitrary(), c =>
            {
                byte[] cover = c.format switch
                {
                    "png" => Png(),
                    "jpeg" => JpegImageTests.SampleJpeg(24, 16),
                    _ => PcmAudioTests.Synthetic(200, 1, 16).ToArray(),
                };
                var coder = new MetadataCoding();
                var stego = coder.Embed(c.data, cover);
                var carrier = MetadataCarrier.Load(stego);
                bool roundTrip = coder.ExtractBytes(carrier).SequenceEqual(c.data);
                coder.Remove(carrier);
                return roundTrip && carrier.ToArray().SequenceEqual(cover);
            });
        }

        private static byte[] Png()
        {
            using var picture = JpegImageTests.TestPicture(12, 12, 4);
            using var stream = new MemoryStream();
            picture.SaveAsPng(stream);
            return stream.ToArray();
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
    }
}
