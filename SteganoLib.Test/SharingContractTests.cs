#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using SteganoLib.Sharing;
using Xunit;

namespace SteganoLib.Test
{
    public class SharingContractTests
    {
        [Fact]
        public void Share_CopiesConstructorInput_ButAllowsExplicitDataEdits()
        {
            byte[] input = { 10, 20 };
            var first = new Share(1, 1, input);
            var second = new Share(1, 2, input);

            input[0] = 99;
            Assert.Equal(new byte[] { 10, 20 }, first.Data);
            first.Data[1] = 30;

            Assert.Equal(new byte[] { 10, 20 }, second.Data);
            Assert.Equal(new byte[] { 1, 1, 10, 30 }, first.ToBytes());
            Assert.Equal(new byte[] { 10, 30 }, ShamirSecretSharing.Combine(new[] { first }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Share_ParsingAndSerializationHaveIndependentBuffers(bool useTryParse)
        {
            byte[] input = { 2, 3, 10, 20 };
            var share = useTryParse ? Share.TryParse(input) : Share.FromBytes(input);
            // This guard also checks nullable flow for TryParse's public contract.
            if (share == null)
                throw new InvalidOperationException("Valid share was not parsed.");

            input[2] = 99;
            var output = share.ToBytes();
            output[3] = 88;
            Assert.Equal(new byte[] { 10, 20 }, share.Data);

            share.Data[0] = 11;
            Assert.Equal(new byte[] { 2, 3, 99, 20 }, input);
            Assert.Equal(new byte[] { 2, 3, 10, 88 }, output);
            Assert.Equal(new byte[] { 2, 3, 11, 20 }, share.ToBytes());
        }

        [Fact]
        public void Share_NullParsingInputHasExplicitContracts()
        {
            Assert.Null(Share.TryParse(null));
            Assert.Equal("bytes", Assert.Throws<ArgumentNullException>(() => Share.FromBytes(null!)).ParamName);
        }

        [Fact]
        public void Split_ReturnsReadOnlyCollectionWithIndependentShareBuffers()
        {
            byte[] secret = { 10, 20 };
            var shares = new ShamirSecretSharing(1, 2).Split(secret);
            secret[0] = 99;
            Assert.Equal(new byte[] { 10, 20 }, shares[0].Data);
            shares[0].Data[1] = 30;
            Assert.Equal(new byte[] { 10, 20 }, shares[1].Data);
            Assert.Equal(new byte[] { 10, 20 }, ShamirSecretSharing.Combine(new[] { shares[1] }));

            var list = Assert.IsAssignableFrom<IList<Share>>(shares);
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list.Clear());
            Assert.Throws<NotSupportedException>(() => list[0] = shares[1]);
            Assert.Equal(2, shares.Count);
        }

        [Fact]
        public void Combine_RejectsImpossibleShareCountBeforeAccessingItems()
        {
            var error = Assert.Throws<ArgumentException>(() => ShamirSecretSharing.Combine(new OversizedShareList()));
            Assert.Equal("shares", error.ParamName);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void SharedCoding_RepeatedEmbeddingTargetIsRejectedBeforeInnerCalls(int threshold)
        {
            var inner = new MemoryAlgorithm();
            var coder = new SharedCoding<MemoryCarrier>(inner, threshold);
            var carrier = new MemoryCarrier();
            var carriers = new[] { carrier, carrier };
            var original = carrier.Payload;

            Assert.Equal(0, coder.Capacity(carriers));
            Assert.False(coder.IsPossibleToEmbed(0, carriers));
            Assert.False(coder.IsPossibleToEmbed(1, carriers));
            var error = Assert.Throws<ArgumentException>(() => coder.EmbedBytes(new byte[] { 10 }, carriers));

            Assert.Equal("carriers", error.ParamName);
            Assert.Equal(0, inner.CapacityCalls);
            Assert.Equal(0, inner.EmbedCalls);
            Assert.Same(original, carrier.Payload);
        }

        [Fact]
        public void SharedCoding_DistinctValueEqualCarriersRoundTrip()
        {
            var coder = new SharedCoding<MemoryCarrier>(new MemoryAlgorithm(), 2);
            var carriers = new[] { new MemoryCarrier(), new MemoryCarrier(), new MemoryCarrier() };
            Assert.Equal(carriers[0], carriers[1]);
            Assert.NotSame(carriers[0], carriers[1]);
            byte[] data = { 10, 20, 30 };

            Assert.Equal(62, coder.Capacity(carriers));
            Assert.True(coder.IsPossibleToEmbed(data.Length, carriers));
            coder.EmbedBytes(data, carriers);

            Assert.Equal(data, coder.ExtractBytes(new[] { carriers[2], carriers[0] }));
            Assert.Equal(data, coder.ExtractBytes(new[] { carriers[2], carriers[0], carriers[2] }));
            Assert.Empty(coder.ExtractBytes(new[] { carriers[0], carriers[0] }));
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        public void SharedCoding_RejectsNegativeInnerCapacityBeforeEmbedding(long capacity)
        {
            var inner = new MemoryAlgorithm();
            var coder = new SharedCoding<MemoryCarrier>(inner, 2);
            var carriers = new[] { new MemoryCarrier(), new MemoryCarrier { Available = capacity } };

            Assert.Throws<InvalidOperationException>(() => coder.Capacity(carriers));
            Assert.Throws<InvalidOperationException>(() => coder.EmbedBytes(Array.Empty<byte>(), carriers));
            Assert.Equal(0, inner.EmbedCalls);
        }

        [Fact]
        public void SharedCoding_NullInnerPayloadIsAContractError()
        {
            var coder = new SharedCoding<MemoryCarrier>(new MemoryAlgorithm { ReturnNull = true }, 1);
            Assert.Throws<InvalidOperationException>(() => coder.ExtractBytes(new[] { new MemoryCarrier() }));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        public void SharedCoding_PreflightsAllCapacitiesBeforeWriting(int payloadLength, long available)
        {
            var inner = new MemoryAlgorithm();
            var coder = new SharedCoding<MemoryCarrier>(inner, 2);
            var carriers = new[] { new MemoryCarrier(), new MemoryCarrier { Available = available } };
            var original = carriers[0].Payload;

            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[payloadLength], carriers));

            Assert.Equal(0, inner.EmbedCalls);
            Assert.Same(original, carriers[0].Payload);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SharedCoding_InnerFailurePreservesOrderAndLeavesLaterCarriersUntouched(bool writeBeforeThrow)
        {
            var failure = new InvalidOperationException("Inner embedding failed.");
            var carriers = new[] { new MemoryCarrier(), new MemoryCarrier(), new MemoryCarrier() };
            var inner = new MemoryAlgorithm
            {
                FailingCarrier = carriers[1],
                Failure = failure,
                WriteBeforeThrow = writeBeforeThrow
            };
            var coder = new SharedCoding<MemoryCarrier>(inner, 2);
            var secondOriginal = carriers[1].Payload;
            var thirdOriginal = carriers[2].Payload;
            byte[] data = { 10, 20, 30 };

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => coder.EmbedBytes(data, carriers)));

            Assert.Equal(2, inner.EmbedCalls);
            var firstShare = Share.FromBytes(carriers[0].Payload);
            Assert.Equal(1, firstShare.Index);
            Assert.Equal(2, firstShare.Threshold);
            Assert.Equal(data.Length, firstShare.Data.Length);
            if (writeBeforeThrow)
                Assert.Equal(data, coder.ExtractBytes(new[] { carriers[0], carriers[1] }));
            else
                Assert.Same(secondOriginal, carriers[1].Payload);
            Assert.Same(thirdOriginal, carriers[2].Payload);
        }

        [Theory]
        [InlineData(0, "data")]
        [InlineData(1, "data")]
        [InlineData(2, "key")]
        [InlineData(3, "key")]
        public void FileHelpers_RejectNullPayloadAndKeyBeforeOpeningInput(int operation, string parameter)
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var coder = new SharedCoding<Image<Rgba32>>(new Algorithms.LSB(new KeyedPermutationSelector(key)), 1);
            var pipeline = new StegoPipeline<IReadOnlyList<Image<Rgba32>>>(coder);
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var inputs = new[] { Path.Combine(directory, "missing.png") };
            var outputs = new[] { Path.Combine(directory, "output.png") };

            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                switch (operation)
                {
                    case 0: coder.EmbedBytes(null!, inputs, outputs); break;
                    case 1: pipeline.Embed(null!, inputs, outputs, key); break;
                    case 2: pipeline.Embed(Array.Empty<byte>(), inputs, outputs, null!); break;
                    case 3: pipeline.Extract(inputs, null!); break;
                }
            });

            Assert.Equal(parameter, error.ParamName);
            Assert.False(Directory.Exists(directory));
        }

        private sealed class MemoryCarrier
        {
            public byte[] Payload { get; set; } = Array.Empty<byte>();
            public long Available { get; init; } = 64;

            public override bool Equals(object? obj) => obj is MemoryCarrier;
            public override int GetHashCode() => 0;
        }

        private sealed class MemoryAlgorithm : IStegAlgorithm<MemoryCarrier>
        {
            public int CapacityCalls { get; private set; }
            public int EmbedCalls { get; private set; }
            public bool ReturnNull { get; init; }
            public MemoryCarrier? FailingCarrier { get; init; }
            public InvalidOperationException? Failure { get; init; }
            public bool WriteBeforeThrow { get; init; }

            public void EmbedBytes(byte[] data, MemoryCarrier carrier)
            {
                EmbedCalls++;
                bool fail = ReferenceEquals(carrier, FailingCarrier);
                if (!fail || WriteBeforeThrow)
                    carrier.Payload = (byte[])data.Clone();
                if (fail)
                    throw Failure!;
            }

            public byte[] ExtractBytes(MemoryCarrier carrier) => ReturnNull ? null! : (byte[])carrier.Payload.Clone();

            public long Capacity(MemoryCarrier carrier)
            {
                CapacityCalls++;
                return carrier.Available;
            }
        }

        private sealed class OversizedShareList : IReadOnlyList<Share>
        {
            public int Count => 256;
            public Share this[int index] => throw new InvalidOperationException("Items must not be accessed.");
            public IEnumerator<Share> GetEnumerator() => throw new InvalidOperationException("Items must not be accessed.");
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
