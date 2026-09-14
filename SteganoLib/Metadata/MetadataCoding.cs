using System;
using System.Collections.Generic;
using System.IO;

using SteganoLib.Algorithms;

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Hides the payload in container metadata rather than in the signal. Fast, exact
    /// and format-agnostic: the payload is split into entries of the store's maximum
    /// size and concatenated back on extraction. Not steganography against an examiner
    /// who inspects the file structure; pair it with <see cref="StegoPipeline{TCarrier}"/>
    /// so the entries at least carry nothing but ciphertext.
    /// Entries have no sequence numbers or integrity checks; use an authenticated pipeline
    /// to detect missing, reordered or modified payload bytes.
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
            for (int offset = 0; offset < data.Length;)
            {
                int length = Math.Min(entrySize, data.Length - offset);
                entries.Add(data.AsSpan(offset, length).ToArray());
                offset += length;
            }

            carrier.Store.WriteEntries(entries);
        }

        public byte[] ExtractBytes(MetadataCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            var store = carrier.Store;
            ValidateLimits(store);
            var entries = store.ReadEntries() ?? throw new InvalidDataException("Metadata store returned a null entry list.");
            long total = 0;
            foreach (var entry in entries)
            {
                if (entry == null || entry.Length > store.MaxEntrySize)
                    throw new InvalidDataException("Metadata store returned a null or oversized entry.");
                total += entry.Length;
                if (total > store.MaxTotalSize)
                    throw new InvalidDataException("Stored payload exceeds the container limit.");
                if (total > Array.MaxLength)
                    throw new InvalidOperationException("Stored payload exceeds the maximum byte array length.");
            }

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
            ValidateLimits(store);
            return Math.Min(store.MaxTotalSize, (long)store.MaxEntrySize * MaxEntries);
        }

        private static void ValidateLimits(IMetadataStore store)
        {
            if (store.MaxEntrySize < 0 || store.MaxTotalSize < 0)
                throw new InvalidOperationException("Metadata store size limits must not be negative.");
        }

        /// <summary>Delete every entry, leaving the container as if nothing had been embedded.</summary>
        public void Remove(MetadataCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));
            carrier.Store.WriteEntries(Array.Empty<byte[]>());
        }
    }
}
