#nullable enable

using System.Collections.Generic;

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Opaque metadata entries of a container file: PNG chunks, JPEG APPn segments,
    /// RIFF chunks. Each store owns one entry kind (a chunk type, a marker and
    /// identifier, a chunk id); entries of that kind are the payload. Other entry
    /// contents are preserved, though container padding may be normalised.
    /// Store instances are mutable. Synchronise reads, writes and direct chunk edits
    /// externally when sharing a store between threads.
    /// </summary>
    public interface IMetadataStore
    {
        /// <summary>Largest number of payload bytes one entry can carry; must be nonnegative.</summary>
        int MaxEntrySize { get; }

        /// <summary>Nonnegative upper bound on payload bytes across all entries imposed by the file format.</summary>
        long MaxTotalSize { get; }

        /// <summary>Independent copies of entry payloads in file order. The list and entries must not be null; modifying them must not change the store.</summary>
        IReadOnlyList<byte[]> ReadEntries();

        /// <summary>Replace entries with copies of the supplied buffers. An empty list removes them all. Invalid input must leave existing entries unchanged.</summary>
        void WriteEntries(IReadOnlyList<byte[]> entries);

        /// <summary>Serialise the container with the current entries.</summary>
        byte[] ToArray();
    }
}
