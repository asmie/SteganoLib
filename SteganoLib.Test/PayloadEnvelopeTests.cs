using System;
using System.Linq;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using Xunit;

namespace SteganoLib.Test
{
    public class PayloadEnvelopeTests
    {
        private static readonly StegoKey Key = StegoKey.FromPassphrase("secret", iterations: 10);
        private static readonly StegoKey OtherKey = StegoKey.FromPassphrase("other", iterations: 10);

        public static TheoryData<IPayloadCodec> Codecs => new()
        {
            new AesGcmPayloadCodec(),
            new HmacPayloadCodec(),
        };

        [Theory]
        [MemberData(nameof(Codecs))]
        public void SealAndOpen_RoundTrip(IPayloadCodec codec)
        {
            var envelope = new PayloadEnvelope(codec);
            var payload = new byte[] { 1, 2, 3, 4, 5 };

            var result = envelope.Open(envelope.Seal(payload, Key), Key);

            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(payload, result.Data);
        }

        [Theory]
        [MemberData(nameof(Codecs))]
        public void SealAndOpen_EmptyPayload_RoundTrip(IPayloadCodec codec)
        {
            var envelope = new PayloadEnvelope(codec);

            var result = envelope.Open(envelope.Seal(Array.Empty<byte>(), Key), Key);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Data);
        }

        [Theory]
        [MemberData(nameof(Codecs))]
        public void Open_WrongKey_AuthenticationFailed(IPayloadCodec codec)
        {
            var envelope = new PayloadEnvelope(codec);

            var result = envelope.Open(envelope.Seal(new byte[] { 9, 9, 9 }, Key), OtherKey);

            Assert.Equal(ExtractionStatus.AuthenticationFailed, result.Status);
            Assert.Null(result.Data);
        }

        [Theory]
        [MemberData(nameof(Codecs))]
        public void Open_TamperedBody_AuthenticationFailed(IPayloadCodec codec)
        {
            var envelope = new PayloadEnvelope(codec);
            var sealedData = envelope.Seal(new byte[] { 9, 9, 9 }, Key);
            sealedData[sealedData.Length - 1] ^= 0x01;

            Assert.Equal(ExtractionStatus.AuthenticationFailed, envelope.Open(sealedData, Key).Status);
        }

        [Theory]
        [MemberData(nameof(Codecs))]
        public void Open_TamperedHeaderFlag_AuthenticationFailed(IPayloadCodec codec)
        {
            var envelope = new PayloadEnvelope(codec);
            foreach (var payload in new[] { Array.Empty<byte>(), new byte[] { 9, 9, 9 } })
            {
                var sealedData = envelope.Seal(payload, Key);
                sealedData[3] ^= 0x01; // the compression flag is in the clear but bound into the tag

                Assert.Equal(ExtractionStatus.AuthenticationFailed, envelope.Open(sealedData, Key).Status);
            }
        }

        [Fact]
        public void Open_NoMagic_NotFound()
        {
            var envelope = new PayloadEnvelope();

            Assert.Equal(ExtractionStatus.NotFound, envelope.Open(Array.Empty<byte>(), Key).Status);
            Assert.Equal(ExtractionStatus.NotFound, envelope.Open(new byte[] { 0, 0, 0, 0, 0, 0 }, Key).Status);
        }

        [Fact]
        public void Open_UnknownVersion_Unsupported()
        {
            var envelope = new PayloadEnvelope();
            var sealedData = envelope.Seal(new byte[] { 1 }, Key);
            sealedData[2] = 99;

            Assert.Equal(ExtractionStatus.Unsupported, envelope.Open(sealedData, Key).Status);
        }

        [Fact]
        public void Open_UnknownCodec_Unsupported()
        {
            var sealedData = new PayloadEnvelope(new AesGcmPayloadCodec()).Seal(new byte[] { 1 }, Key);

            var result = new PayloadEnvelope(new HmacPayloadCodec()).Open(sealedData, Key);

            Assert.Equal(ExtractionStatus.Unsupported, result.Status);
        }

        [Fact]
        public void Open_DefaultEnvelope_AcceptsBothCodecs()
        {
            var payload = new byte[] { 7, 7 };
            var fromGcm = new PayloadEnvelope(new AesGcmPayloadCodec()).Seal(payload, Key);
            var fromHmac = new PayloadEnvelope(new HmacPayloadCodec()).Seal(payload, Key);

            var reader = new PayloadEnvelope();
            Assert.Equal(payload, reader.Open(fromGcm, Key).Data);
            Assert.Equal(payload, reader.Open(fromHmac, Key).Data);
        }

        [Fact]
        public void Seal_AesGcm_DiffersBetweenCalls()
        {
            var envelope = new PayloadEnvelope(new AesGcmPayloadCodec());
            var payload = new byte[] { 1, 2, 3 };

            Assert.NotEqual(envelope.Seal(payload, Key), envelope.Seal(payload, Key));
        }

        [Fact]
        public void Compress_RepetitiveData_ShrinksAndRoundTrips()
        {
            var envelope = new PayloadEnvelope(new HmacPayloadCodec()) { Compress = true };
            var payload = Enumerable.Repeat((byte)0x41, 4096).ToArray();

            var sealedData = envelope.Seal(payload, Key);
            Assert.True(sealedData.Length < payload.Length / 4);

            var result = envelope.Open(sealedData, Key);
            Assert.True(result.IsSuccess);
            Assert.Equal(payload, result.Data);
        }

        [Fact]
        public void Compress_RandomData_FallsBackToUncompressed()
        {
            var envelope = new PayloadEnvelope(new HmacPayloadCodec()) { Compress = true };
            var payload = new byte[2048];
            new Random(1).NextBytes(payload);

            var sealedData = envelope.Seal(payload, Key);
            Assert.Equal(0, sealedData[3] & 0x01);
            Assert.Equal(payload, envelope.Open(sealedData, Key).Data);
        }

        [Fact]
        public void Overhead_MatchesActualSealedSize()
        {
            var envelope = new PayloadEnvelope();
            var payload = new byte[100];

            Assert.Equal(envelope.Seal(payload, Key).Length - payload.Length, envelope.Overhead);
            Assert.Equal(5 + 32, new PayloadEnvelope(new HmacPayloadCodec()).Overhead);
        }

        [Fact]
        public void Constructor_DuplicateCodecIds_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PayloadEnvelope(new HmacPayloadCodec(), new HmacPayloadCodec()));
        }

        [Fact]
        public void Constructor_NoCodecs_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PayloadEnvelope(Array.Empty<IPayloadCodec>()));
        }
    }
}
