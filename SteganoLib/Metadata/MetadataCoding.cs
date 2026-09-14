using System;
using System.Collections.Generic;

using SteganoLib.Algorithms;

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Hides the payload in container metadata rather than in the signal. Fast, exact
    /// and format-agnostic: the payload is split into entries of the store's maximum
    /// size and concatenated back on extraction. Not steganography against an examiner
    /// who inspects the file structure; pair it with <see cref="StegoPipeline{TCarrier}"/>
    /// so the entries at least carry nothing but ciphertext.
    /// </summary>
    public sealed class MetadataCoding : IStegAlgorithm<MetadataCarrier>
    {
        private int _maxEntries = 64;

        /// <summary>Entries the algorithm is willing to write. Bounds <see cref="Capacity"/> for formats without a total size limit.</summary>
        public int MaxEntries
        {
            get => _maxEntries;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "At least one entry is needed.");
                _maxEntries = value;
            }
        }

        public void EmbedBytes(byte[] data, MetadataCarrier carrier)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            long capacity = Capacity(carrier);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            int entrySize = carrier.Store.MaxEntrySize;
            var entries = new List<byte[]>();
            if (data.Length == 0)
                entries.Add(Array.Empty<byte>());
            for (int offset = 0; offset < data.Length; offset += entrySize)
                entries.Add(data.AsSpan(offset, Math.Min(entrySize, data.Length - offset)).ToArray());

            carrier.Store.WriteEntries(entries);
        }

        public byte[] ExtractBytes(MetadataCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            var entries = carrier.Store.ReadEntries();
            long total = 0;
            foreach (var entry in entries)
                total += entry.Length;
            if (total > int.MaxValue)
                throw new InvalidOperationException("Stored payload exceeds 2 GB.");

            var data = new byte[total];
            int offset = 0;
            foreach (var entry in entries)
            {
                entry.CopyTo(data, offset);
                offset += entry.Length;
            }
            return data;
        }

        public long Capacity(MetadataCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            var store = carrier.Store;
            return Math.Min(store.MaxTotalSize, (long)store.MaxEntrySize * MaxEntries);
        }

        /// <summary>Delete every entry, leaving the container as if nothing had been embedded.</summary>
        public void Remove(MetadataCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));
            carrier.Store.WriteEntries(Array.Empty<byte[]>());
        }
    }
}
