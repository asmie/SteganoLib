using System;
using System.Linq;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Payload;
using SteganoLib.Sharing;
using Xunit;

namespace SteganoLib.Test
{
    /// <summary>The span overloads must behave exactly like the array ones.</summary>
    public class SpanApiTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x88 });

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        [Fact]
        public void ReedSolomon_SpanOverloads_MatchArrays()
        {
            var code = new ReedSolomonCode(16);
            var data = Random(700, 1);
            var buffer = new byte[1000];
            data.CopyTo(buffer, 100);

            var fromArray = code.Encode(data);
            var fromSpan = code.Encode(buffer.AsSpan(100, 700));
            Assert.Equal(fromArray, fromSpan);

            fromSpan[10] ^= 0xFF;
            Assert.True(code.TryDecode((ReadOnlySpan<byte>)fromSpan, out var decoded, out int corrected));
            Assert.Equal(data, decoded);
            Assert.Equal(1, corrected);
            Assert.True(code.TryDecode(ReadOnlySpan<byte>.Empty, out var empty, out _));
            Assert.Empty(empty);
        }

        [Fact]
        public void Envelope_SpanOverloads_MatchArrays()
        {
            var envelope = new PayloadEnvelope();
            var data = Random(300, 2);
            var buffer = new byte[500];
            data.CopyTo(buffer, 50);

            var sealedBytes = envelope.Seal(buffer.AsSpan(50, 300), Key);
            Assert.Equal(300 + envelope.Overhead, sealedBytes.Length);

            var opened = envelope.Open((ReadOnlySpan<byte>)sealedBytes, Key);
            Assert.True(opened.IsSuccess);
            Assert.Equal(data, opened.Data);
            Assert.Equal(ExtractionStatus.NotFound, envelope.Open(ReadOnlySpan<byte>.Empty, Key).Status);
            Assert.True(envelope.Open(envelope.Seal(data, Key), Key).IsSuccess);
        }

        [Fact]
        public void Shamir_SpanOverload_MatchesArray()
        {
            var sharing = new ShamirSecretSharing(2, 3);
            var secret = Random(40, 3);
            var buffer = new byte[100];
            secret.CopyTo(buffer, 30);

            var shares = sharing.Split(buffer.AsSpan(30, 40));

            Assert.Equal(3, shares.Count);
            Assert.Equal(secret, ShamirSecretSharing.Combine(shares.Take(2).ToList()));
        }

        [Fact]
        public void Interleaver_AcceptsSpans()
        {
            var stream = Random(600, 4);
            var forward = ReedSolomonCode.Interleaver.Forward(stream.AsSpan(), 255);
            Assert.Equal(stream, ReedSolomonCode.Interleaver.Inverse(forward.AsSpan(), 255));
        }
    }
}
