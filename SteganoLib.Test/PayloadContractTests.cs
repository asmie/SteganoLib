using System;
using System.Collections.Generic;
using System.Linq;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using Xunit;

namespace SteganoLib.Test
{
    public class PayloadContractTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 42 });

        // Deliberately permits invalid outputs to exercise the envelope's extension boundary.
        private sealed class ContractCodec : IPayloadCodec
        {
            public byte Id { get; set; } = 7;
            public int Overhead { get; set; } = 2;
            public bool NullSealed { get; set; }
            public bool NullOpened { get; set; }
            public bool OpenSucceeds { get; set; } = true;
            public int SealLengthDelta { get; set; }
            public int OpenLengthDelta { get; set; }
            public int SealCalls { get; private set; }
            public int OpenCalls { get; private set; }
            public Action OnSeal { get; set; }
            public Action OnOpen { get; set; }

            public byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData, StegoKey key)
            {
                SealCalls++;
                byte[] output = NullSealed ? null : new byte[plaintext.Length + Overhead + SealLengthDelta];
                OnSeal?.Invoke();
                return output;
            }

            public bool TryOpen(ReadOnlySpan<byte> sealedBody, ReadOnlySpan<byte> associatedData, StegoKey key, out byte[] plaintext)
            {
                OpenCalls++;
                plaintext = NullOpened ? null : new byte[sealedBody.Length - Overhead + OpenLengthDelta];
                OnOpen?.Invoke();
                return OpenSucceeds;
            }
        }

        private sealed class SpyAlgorithm : IStegAlgorithm<object>
        {
            public int Calls { get; private set; }
            public bool ReturnNull { get; set; }
            public void EmbedBytes(byte[] data, object carrier) => Calls++;
            public byte[] ExtractBytes(object carrier)
            {
                Calls++;
                return ReturnNull ? null : Array.Empty<byte>();
            }
            public long Capacity(object carrier)
            {
                Calls++;
                return 100;
            }
        }

        public static IEnumerable<object[]> InvalidOverheads => new[] { -1, int.MaxValue, Array.MaxLength - 4 }
            .Select(value => new object[] { value });

        [Theory]
        [MemberData(nameof(InvalidOverheads))]
        public void Registration_RejectsUnrepresentableOverheadForEveryCodec(int overhead)
        {
            var codec = new ContractCodec { Overhead = overhead };
            Assert.Throws<ArgumentException>(() => new PayloadEnvelope(codec));
            Assert.Throws<ArgumentException>(() => new PayloadEnvelope(new AesGcmPayloadCodec(), codec));
            Assert.Equal(0, codec.SealCalls);
            Assert.Equal(0, codec.OpenCalls);
        }

        [Fact]
        public void Registration_RejectsNullListAndNullElements()
        {
            Assert.Equal("codecs", Assert.Throws<ArgumentNullException>(() => new PayloadEnvelope((IPayloadCodec[])null)).ParamName);
            Assert.Throws<ArgumentException>(() => new PayloadEnvelope((IPayloadCodec)null));
            Assert.Throws<ArgumentException>(() => new PayloadEnvelope(new AesGcmPayloadCodec(), null));
        }

        [Fact]
        public void Registration_CopiesListAndRetainsCodecOrder()
        {
            var codecs = new IPayloadCodec[] { new HmacPayloadCodec(), new AesGcmPayloadCodec() };
            var envelope = new PayloadEnvelope(codecs);
            codecs[0] = null;
            codecs[1] = null;
            byte[] payload = { 1, 2, 3 };
            byte[] sealedData = envelope.Seal(payload, Key);
            Assert.Equal(2, sealedData[4]);
            Assert.Equal(37, envelope.Overhead);
            Assert.Equal(payload, envelope.Open(sealedData, Key).Data);
            byte[] aesData = new PayloadEnvelope(new AesGcmPayloadCodec()).Seal(payload, Key);
            Assert.Equal(payload, envelope.Open(aesData, Key).Data);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Registration_RejectsChangedDeclarations(bool changeId)
        {
            var codec = new ContractCodec();
            var envelope = new PayloadEnvelope(codec);
            byte[] data = envelope.Seal(new byte[4], Key);
            if (changeId) codec.Id++;
            else codec.Overhead++;
            Assert.Throws<InvalidOperationException>(() => envelope.Overhead);
            Assert.Throws<InvalidOperationException>(() => envelope.Seal(new byte[4], Key));
            Assert.Throws<InvalidOperationException>(() => envelope.Open(data, Key));
            Assert.Equal(1, codec.SealCalls);
            Assert.Equal(0, codec.OpenCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Registration_RejectsChangesInsideCodecCallbacks(bool open)
        {
            var codec = new ContractCodec();
            var envelope = new PayloadEnvelope(codec);
            if (open)
            {
                byte[] data = envelope.Seal(new byte[4], Key);
                codec.OnOpen = () => codec.Id++;
                Assert.Throws<InvalidOperationException>(() => envelope.Open(data, Key));
            }
            else
            {
                codec.OnSeal = () => codec.Overhead++;
                Assert.Throws<InvalidOperationException>(() => envelope.Seal(new byte[4], Key));
            }
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        public void Seal_RejectsNullOrIncorrectLengthBeforeCallingCarrier(int delta)
        {
            var codec = new ContractCodec { NullSealed = delta == 0, SealLengthDelta = delta };
            var algorithm = new SpyAlgorithm();
            var pipeline = new StegoPipeline<object>(algorithm, new PayloadEnvelope(codec));
            Assert.Throws<InvalidOperationException>(() => pipeline.Embed(new byte[4], new object(), Key));
            Assert.Equal(1, codec.SealCalls);
            Assert.Equal(0, algorithm.Calls);
        }

        [Fact]
        public void Seal_RejectsImpossibleOutputBeforeInvokingCodec()
        {
            var codec = new ContractCodec { Overhead = Array.MaxLength - 5 };
            var envelope = new PayloadEnvelope(codec);
            Assert.Equal(Array.MaxLength, envelope.Overhead);
            Assert.Throws<InvalidOperationException>(() => envelope.Seal(new byte[1], Key));
            Assert.Equal(0, codec.SealCalls);
        }

        [Fact]
        public void Seal_AllowsZeroOverheadAsACustomCodecContract()
        {
            var envelope = new PayloadEnvelope(new ContractCodec { Overhead = 0 });
            byte[] data = envelope.Seal(Array.Empty<byte>(), Key);
            Assert.Equal(5, data.Length);
            Assert.True(envelope.Open(data, Key).IsSuccess);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1)]
        public void Open_RejectsFalseSuccessWithNullOrIncorrectLength(int delta)
        {
            var codec = new ContractCodec { NullOpened = delta == 0, OpenLengthDelta = delta };
            var envelope = new PayloadEnvelope(codec);
            byte[] data = envelope.Seal(new byte[4], Key);
            Assert.Throws<InvalidOperationException>(() => envelope.Open(data, Key));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Open_IgnoresOutputWhenCodecRejectsAuthentication(bool nullOutput)
        {
            var codec = new ContractCodec { OpenSucceeds = false, NullOpened = nullOutput, OpenLengthDelta = 1 };
            var envelope = new PayloadEnvelope(codec);
            var result = envelope.Open(envelope.Seal(new byte[4], Key), Key);
            Assert.Equal(ExtractionStatus.AuthenticationFailed, result.Status);
            Assert.Null(result.Data);
        }

        [Fact]
        public void Open_RejectsBodyShorterThanOverheadWithoutInvokingCodec()
        {
            var codec = new ContractCodec();
            var envelope = new PayloadEnvelope(codec);
            byte[] data = envelope.Seal(Array.Empty<byte>(), Key);
            Assert.Equal(ExtractionStatus.AuthenticationFailed, envelope.Open(data[..^1], Key).Status);
            Assert.Equal(0, codec.OpenCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UnknownFlags_AreUnsupportedOnlyAfterAuthentication(bool hmac)
        {
            IPayloadCodec codec = hmac ? new HmacPayloadCodec() : new AesGcmPayloadCodec();
            var envelope = new PayloadEnvelope(codec);
            byte[] header = { 0x53, 0x4C, 1, 0x80, codec.Id };
            byte[] futureData = header.Concat(codec.Seal(new byte[] { 1, 2, 3 }, header, Key)).ToArray();
            Assert.Equal(ExtractionStatus.Unsupported, envelope.Open(futureData, Key).Status);

            byte[] tampered = envelope.Seal(new byte[] { 1, 2, 3 }, Key);
            tampered[3] = 0x80;
            Assert.Equal(ExtractionStatus.AuthenticationFailed, envelope.Open(tampered, Key).Status);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BuiltInCodecs_ReturnIndependentOutputs(bool hmac)
        {
            IPayloadCodec codec = hmac ? new HmacPayloadCodec() : new AesGcmPayloadCodec();
            byte[] payload = { 1, 2, 3 }, header = { 5, 6 };
            byte[] sealedData = codec.Seal(payload, header, Key);
            Array.Fill(payload, (byte)0);
            Assert.True(codec.TryOpen(sealedData, header, Key, out var first));
            Assert.Equal(new byte[] { 1, 2, 3 }, first);
            Array.Fill(first, (byte)9);
            Assert.True(codec.TryOpen(sealedData, header, Key, out var second));
            Assert.Equal(new byte[] { 1, 2, 3 }, second);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void Pipeline_NullArgumentsAreRejectedBeforeCallingCarrier(int operation)
        {
            var algorithm = new SpyAlgorithm();
            var pipeline = new StegoPipeline<object>(algorithm);
            var carrier = new object();
            (Action Run, string Parameter) action = operation switch
            {
                0 => (() => pipeline.Embed(null, carrier, Key), "data"),
                1 => (() => pipeline.Embed(new byte[1], null, Key), "carrier"),
                2 => (() => pipeline.Embed(new byte[1], carrier, null), "key"),
                3 => (() => pipeline.Extract(null, Key), "carrier"),
                4 => (() => pipeline.Extract(carrier, null), "key"),
                5 => (() => pipeline.Capacity(null), "carrier"),
                _ => (() => pipeline.IsPossibleToEmbed(1, null), "carrier"),
            };
            Assert.Equal(action.Parameter, Assert.Throws<ArgumentNullException>(action.Run).ParamName);
            Assert.Equal(0, algorithm.Calls);
        }

        [Fact]
        public void Pipeline_ReportsInvalidAlgorithmOutput()
        {
            var algorithm = new SpyAlgorithm { ReturnNull = true };
            Assert.Throws<InvalidOperationException>(() => new StegoPipeline<object>(algorithm).Extract(new object(), Key));
            Assert.Equal(1, algorithm.Calls);
        }

        [Fact]
        public void Result_PreservesExplicitSharedArrayOwnership()
        {
            byte[] data = { 1, 2, 3 };
            var result = ExtractResult.Success(data);
            var copy = result;
            Assert.Same(data, result.Data);
            result.Data[0] = 42;
            Assert.Equal(42, copy.Data[0]);
            Assert.True(result.IsSuccess);
        }

#nullable enable
        [Fact]
        public void NullableAnnotations_AllowGuardedResultAndCodecAccess()
        {
            // This method compiles with nullable analysis enabled; no null-forgiving operators are needed.
            IPayloadCodec codec = new HmacPayloadCodec();
            byte[] data = codec.Seal(new byte[] { 1, 2, 3 }, ReadOnlySpan<byte>.Empty, Key);
            if (!codec.TryOpen(data, ReadOnlySpan<byte>.Empty, Key, out var plaintext))
                throw new InvalidOperationException("Expected valid payload.");
            Assert.Equal(3, plaintext.Length);

            var result = new PayloadEnvelope().Open(new PayloadEnvelope().Seal(plaintext, Key), Key);
            if (!result.IsSuccess)
                throw new InvalidOperationException("Expected successful extraction.");
            Assert.Equal(3, result.Data.Length);
        }
    }
}
