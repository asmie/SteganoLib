using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteganoLib.Algorithms;
using SteganoLib.Metadata;
using Xunit;

namespace SteganoLib.Test
{
    public class MetadataAssemblyTests
    {
        private sealed class Store : IMetadataStore
        {
            public int MaxEntrySize { get; set; } = 3;
            public long MaxTotalSize { get; set; } = long.MaxValue;
            public IReadOnlyList<byte[]> Entries { get; set; } = Array.Empty<byte[]>();
            public int Writes { get; private set; }
            public IReadOnlyList<byte[]> ReadEntries() => Entries;
            public void WriteEntries(IReadOnlyList<byte[]> entries)
            {
                Writes++;
                Entries = entries;
            }
            public byte[] ToArray() => throw new NotSupportedException();
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(6, 2)]
        [InlineData(8, 3)]
        [InlineData(9, 3)]
        public void SplitsAndAssemblesAtEntryBoundaries(int length, int count)
        {
            var store = new Store();
            var carrier = new MetadataCarrier(store);
            var coding = new MetadataCoding { MaxEntries = 3 };
            byte[] data = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            coding.EmbedBytes(data, carrier);
            Assert.Equal(count, store.Entries.Count);
            Assert.All(store.Entries, entry => Assert.InRange(entry.Length, 0, 3));
            Assert.Equal(data, coding.ExtractBytes(carrier));
        }

        [Fact]
        public void ExtractionDoesNotApplyWriteEntryBudget()
        {
            var store = new Store { Entries = new[] { new byte[] { 1 }, Array.Empty<byte>(), new byte[] { 2 } } };
            Assert.Equal(new byte[] { 1, 2 }, new MetadataCoding { MaxEntries = 1 }.ExtractBytes(new MetadataCarrier(store)));
        }

        [Theory]
        [InlineData(9)]
        [InlineData(8)]
        public void OversizedEmbeddingLeavesExistingEntries(int totalLimit)
        {
            var store = new Store { MaxTotalSize = totalLimit, Entries = new[] { new byte[] { 42 } } };
            var coding = new MetadataCoding { MaxEntries = 3 };
            Assert.Throws<CapacityExceededException>(() => coding.EmbedBytes(new byte[10], new MetadataCarrier(store)));
            Assert.Equal(new byte[] { 42 }, Assert.Single(store.Entries));
            Assert.Equal(0, store.Writes);
        }

        [Theory]
        [InlineData(-1, 10)]
        [InlineData(3, -1)]
        public void RejectsInvalidStoreLimits(int entrySize, long totalSize)
        {
            var store = new Store { MaxEntrySize = entrySize, MaxTotalSize = totalSize };
            var carrier = new MetadataCarrier(store);
            var coding = new MetadataCoding();
            Assert.Throws<InvalidOperationException>(() => coding.Capacity(carrier));
            Assert.Throws<InvalidOperationException>(() => coding.EmbedBytes(Array.Empty<byte>(), carrier));
            Assert.Throws<InvalidOperationException>(() => coding.ExtractBytes(carrier));
            Assert.Equal(0, store.Writes);
        }

        [Fact]
        public void ZeroCapacityAcceptsOnlyEmptyPayload()
        {
            var store = new Store { MaxEntrySize = 0 };
            var carrier = new MetadataCarrier(store);
            var coding = new MetadataCoding();
            coding.EmbedBytes(Array.Empty<byte>(), carrier);
            Assert.Empty(Assert.Single(store.Entries));
            Assert.Empty(coding.ExtractBytes(carrier));
            Assert.Throws<CapacityExceededException>(() => coding.EmbedBytes(new byte[1], carrier));
            Assert.Equal(1, store.Writes);
        }

        [Fact]
        public void RejectsInvalidEntriesAndTotalBeforeAssembly()
        {
            var store = new Store();
            var carrier = new MetadataCarrier(store);
            var coding = new MetadataCoding();
            foreach (var entries in new IReadOnlyList<byte[]>[] { null, new byte[][] { null }, new[] { new byte[4] } })
            {
                store.Entries = entries;
                Assert.Throws<InvalidDataException>(() => coding.ExtractBytes(carrier));
            }
            store.Entries = new[] { new byte[2], new byte[2] };
            store.MaxTotalSize = 3;
            Assert.Throws<InvalidDataException>(() => coding.ExtractBytes(carrier));
        }

        [Fact]
        public void RejectsUnrepresentableTotalWithoutAllocatingGigabytes()
        {
            // Repeated references exceed the array limit while allocating only 1 MiB.
            byte[] entry = new byte[1024 * 1024];
            var store = new Store
            {
                MaxEntrySize = entry.Length,
                Entries = Enumerable.Repeat(entry, Array.MaxLength / entry.Length + 1).ToArray(),
            };
            Assert.Throws<InvalidOperationException>(() => new MetadataCoding().ExtractBytes(new MetadataCarrier(store)));
        }
    }
}
