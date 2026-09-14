using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SteganoLib.Audio;
using SteganoLib.Containers;

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Stores payload entries as RIFF chunks of a WAVE file, inserted before the
    /// <c>data</c> chunk. The default id <see cref="DefaultChunkId"/> is the standard
    /// padding chunk that every reader skips by definition. Works on any RIFF WAVE,
    /// including float and compressed formats, because the audio bytes are copied verbatim.
    /// Survives copying and metadata-preserving edits. Destroyed by re-encoding or by
    /// tools that drop unknown chunks.
    /// </summary>
    public sealed class WavMetadataStore : IMetadataStore
    {
        public const string DefaultChunkId = "JUNK";

        private static readonly string[] ReservedIds = { "RIFF", "RIFX", "LIST", "fmt ", "data" };
        private static readonly Encoding Ascii = Encoding.ASCII;

        private readonly List<RiffChunk> _chunks;

        /// <exception cref="InvalidDataException">Invalid RIFF WAVE signature, chunk boundaries, padding or nested lists.</exception>
        public WavMetadataStore(byte[] wav, string chunkId = DefaultChunkId)
        {
            if (wav == null) throw new ArgumentNullException(nameof(wav));

            ChunkId = ValidateChunkId(chunkId);
            _chunks = Parse(wav);
        }

        /// <summary>Four-character id of the entry chunks.</summary>
        public string ChunkId { get; }

        /// <summary>All chunks in file order, entry chunks included.</summary>
        public IReadOnlyList<RiffChunk> Chunks => _chunks;

        public int MaxEntrySize => int.MaxValue;

        /// <summary>The RIFF size field is 32 bits; this is what remains after the other chunks.</summary>
        public long MaxTotalSize
        {
            get
            {
                long used = 12;
                foreach (var chunk in _chunks)
                {
                    if (chunk.Id != ChunkId)
                        used += 8 + chunk.Payload.Length + (chunk.Payload.Length & 1);
                }
                return Math.Max(0, uint.MaxValue - used);
            }
        }

        public static bool IsWav(ReadOnlySpan<byte> data)
        {
            return data.Length >= 12 && Tag(data, 0) == "RIFF" && Tag(data, 8) == "WAVE";
        }

        public IReadOnlyList<byte[]> ReadEntries()
        {
            var entries = new List<byte[]>();
            foreach (var chunk in _chunks)
            {
                if (chunk.Id == ChunkId)
                    entries.Add(chunk.Payload);
            }
            return entries;
        }

        public void WriteEntries(IReadOnlyList<byte[]> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var encoded = new List<RiffChunk>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry == null)
                    throw new ArgumentException("Entries must not be null.", nameof(entries));
                encoded.Add(new RiffChunk(ChunkId, entry));
            }

            _chunks.RemoveAll(c => c.Id == ChunkId);

            int insertAt = _chunks.FindIndex(c => c.Id == "data");
            _chunks.InsertRange(insertAt < 0 ? _chunks.Count : insertAt, encoded);
        }

        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            var header = new byte[8];
            stream.Write(Ascii.GetBytes("RIFF"));
            stream.Write(header, 0, 4); // size, patched below
            stream.Write(Ascii.GetBytes("WAVE"));
            foreach (var chunk in _chunks)
            {
                Ascii.GetBytes(chunk.Id, 0, 4, header, 0);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), chunk.Payload.Length);
                stream.Write(header);
                stream.Write(chunk.Payload);
                if ((chunk.Payload.Length & 1) == 1)
                    stream.WriteByte(0);
            }

            var bytes = stream.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)Math.Min(uint.MaxValue, (long)bytes.Length - 8));
            return bytes;
        }

        private static List<RiffChunk> Parse(byte[] wav)
        {
            int end = RiffReader.ContainerEnd(wav, "WAVE");
            var chunks = new List<RiffChunk>();
            foreach (var (id, offset, size) in RiffReader.Chunks(wav, 12, end))
                chunks.Add(new RiffChunk(id, wav.AsSpan(offset, size).ToArray()));
            return chunks;
        }

        private static string Tag(ReadOnlySpan<byte> data, int offset) => Ascii.GetString(data.Slice(offset, 4));

        private static string ValidateChunkId(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (id.Length != 4)
                throw new ArgumentException("Chunk id must be four characters.", nameof(id));
            foreach (char c in id)
            {
                if (c < 0x20 || c > 0x7E)
                    throw new ArgumentException("Chunk id must be printable ASCII.", nameof(id));
            }
            if (Array.IndexOf(ReservedIds, id) >= 0)
                throw new ArgumentException($"Chunk id {id} is reserved by the RIFF WAVE format.", nameof(id));
            return id;
        }
    }
}
