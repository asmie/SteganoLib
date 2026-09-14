using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SteganoLib.Metadata
{
    /// <summary>
    /// Stores payload entries as PNG chunks placed just before <c>IEND</c>. By default
    /// each entry becomes a private ancillary chunk of type <see cref="DefaultChunkType"/>;
    /// decoders skip it and editors honouring the safe-to-copy bit keep it. With a text
    /// chunk type (<c>tEXt</c>, <c>zTXt</c> or <c>iTXt</c>) the entry is written as
    /// Base64 under <see cref="Keyword"/> so it reads as ordinary image metadata.
    /// Survives copying and lossless editing. Destroyed by re-encoding with a tool that
    /// drops unknown chunks or strips text metadata.
    /// </summary>
    public sealed class PngMetadataStore : IMetadataStore
    {
        public const string DefaultChunkType = "meTa";
        public const string DefaultKeyword = "Comment";

        private const string Text = "tEXt";
        private const string CompressedText = "zTXt";
        private const string InternationalText = "iTXt";

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly Encoding Latin1 = Encoding.Latin1;

        private readonly List<PngChunk> _chunks;

        /// <exception cref="InvalidDataException">Not a PNG, or a chunk is truncated or fails its CRC.</exception>
        public PngMetadataStore(byte[] png, string chunkType = DefaultChunkType, string keyword = DefaultKeyword)
        {
            if (png == null) throw new ArgumentNullException(nameof(png));

            ChunkType = ValidateChunkType(chunkType);
            Keyword = ValidateKeyword(keyword);
            _chunks = Parse(png);
        }

        /// <summary>Type of the chunks that carry entries.</summary>
        public string ChunkType { get; }

        /// <summary>Keyword used when <see cref="ChunkType"/> is a text chunk; ignored otherwise.</summary>
        public string Keyword { get; }

        /// <summary>Whether entries are written as Base64 text under <see cref="Keyword"/>.</summary>
        public bool IsTextChunk => IsTextType(ChunkType);

        /// <summary>All chunks in file order, including the entry chunks.</summary>
        public IReadOnlyList<PngChunk> Chunks => _chunks;

        public int MaxEntrySize
        {
            get
            {
                if (!IsTextChunk)
                    return int.MaxValue;

                int overhead = Latin1.GetByteCount(Keyword) + 1 + (ChunkType == Text ? 0 : ChunkType == CompressedText ? 1 : 4);
                return (int.MaxValue - overhead) / 4 * 3;
            }
        }

        public long MaxTotalSize => long.MaxValue;

        public static bool IsPng(ReadOnlySpan<byte> data)
        {
            return data.Length >= Signature.Length && data.Slice(0, Signature.Length).SequenceEqual(Signature);
        }

        public IReadOnlyList<byte[]> ReadEntries()
        {
            var entries = new List<byte[]>();
            foreach (var chunk in _chunks)
            {
                if (!IsMine(chunk))
                    continue;

                var payload = Decode(chunk);
                if (payload != null)
                    entries.Add(payload);
            }
            return entries;
        }

        public void WriteEntries(IReadOnlyList<byte[]> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));

            var encoded = new List<PngChunk>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry == null)
                    throw new ArgumentException("Entries must not be null.", nameof(entries));
                if (entry.Length > MaxEntrySize)
                    throw new ArgumentException($"Entry of {entry.Length} bytes exceeds the chunk limit of {MaxEntrySize}.", nameof(entries));
                encoded.Add(Encode(entry));
            }

            _chunks.RemoveAll(IsMine);
            _chunks.InsertRange(_chunks.Count - 1, encoded); // before IEND
        }

        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            stream.Write(Signature);
            var header = new byte[8];
            var crc = new byte[4];
            foreach (var chunk in _chunks)
            {
                BinaryPrimitives.WriteInt32BigEndian(header, chunk.Data.Length);
                Latin1.GetBytes(chunk.Type, 0, 4, header, 4);
                BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.Compute(header.AsSpan(4, 4), chunk.Data));
                stream.Write(header);
                stream.Write(chunk.Data);
                stream.Write(crc);
            }
            return stream.ToArray();
        }

        private static List<PngChunk> Parse(byte[] png)
        {
            if (!IsPng(png))
                throw new InvalidDataException("Not a PNG file.");

            var chunks = new List<PngChunk>();
            int pos = Signature.Length;
            while (true)
            {
                if (pos + 12 > png.Length)
                    throw new InvalidDataException("Truncated PNG chunk.");

                uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
                if (length > int.MaxValue || pos + 12 + (long)length > png.Length)
                    throw new InvalidDataException("Corrupt PNG chunk length.");

                int size = (int)length;
                string type = Latin1.GetString(png, pos + 4, 4);
                uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + size));
                if (crc != Crc32.Compute(png.AsSpan(pos + 4, 4 + size)))
                    throw new InvalidDataException($"CRC mismatch in PNG chunk {type}.");

                chunks.Add(new PngChunk(type, png.AsSpan(pos + 8, size).ToArray()));
                pos += 12 + size;

                if (type == "IEND")
                    break;
            }

            if (chunks[0].Type != "IHDR")
                throw new InvalidDataException("PNG does not start with IHDR.");

            return chunks;
        }

        private bool IsMine(PngChunk chunk)
        {
            if (chunk.Type != ChunkType)
                return false;
            if (!IsTextChunk)
                return true;

            int nul = Array.IndexOf(chunk.Data, (byte)0);
            return nul >= 0 && Latin1.GetString(chunk.Data, 0, nul) == Keyword;
        }

        private PngChunk Encode(byte[] payload)
        {
            if (!IsTextChunk)
                return new PngChunk(ChunkType, payload);

            var text = Encoding.ASCII.GetBytes(Convert.ToBase64String(payload));
            using var body = new MemoryStream();
            body.Write(Latin1.GetBytes(Keyword));
            body.WriteByte(0);
            switch (ChunkType)
            {
                case Text:
                    body.Write(text);
                    break;
                case CompressedText:
                    body.WriteByte(0); // compression method: zlib
                    body.Write(Deflate(text));
                    break;
                default: // iTXt: uncompressed, no language tag, no translated keyword
                    body.WriteByte(0);
                    body.WriteByte(0);
                    body.WriteByte(0);
                    body.WriteByte(0);
                    body.Write(text);
                    break;
            }
            return new PngChunk(ChunkType, body.ToArray());
        }

        /// <summary>Payload of an entry chunk, or <c>null</c> when a text chunk under our keyword is not ours after all.</summary>
        private byte[] Decode(PngChunk chunk)
        {
            if (!IsTextChunk)
                return chunk.Data;

            var body = chunk.Data.AsSpan(Array.IndexOf(chunk.Data, (byte)0) + 1);
            try
            {
                string text;
                switch (ChunkType)
                {
                    case Text:
                        text = Latin1.GetString(body);
                        break;
                    case CompressedText:
                        if (body.Length < 1 || body[0] != 0)
                            return null;
                        text = Latin1.GetString(Inflate(body.Slice(1)));
                        break;
                    default:
                        if (body.Length < 2 || body[1] != 0)
                            return null;
                        bool compressed = body[0] == 1;
                        int pos = 2;
                        int languageEnd = body.Slice(pos).IndexOf((byte)0);
                        if (languageEnd < 0)
                            return null;
                        pos += languageEnd + 1;
                        int translatedEnd = body.Slice(pos).IndexOf((byte)0);
                        if (translatedEnd < 0)
                            return null;
                        pos += translatedEnd + 1;
                        var utf8 = body.Slice(pos);
                        text = Encoding.UTF8.GetString(compressed ? Inflate(utf8) : utf8.ToArray());
                        break;
                }
                return Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        private static byte[] Deflate(byte[] data)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(data);
            return output.ToArray();
        }

        private static byte[] Inflate(ReadOnlySpan<byte> data)
        {
            using var input = new MemoryStream(data.ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        private static bool IsTextType(string type) => type is Text or CompressedText or InternationalText;

        private static string ValidateChunkType(string type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (type.Length != 4 || !AllAsciiLetters(type))
                throw new ArgumentException("Chunk type must be four ASCII letters.", nameof(type));
            if (IsTextType(type))
                return type;
            if (!char.IsLower(type[0]))
                throw new ArgumentException("Chunk type must be ancillary (lowercase first letter); a critical chunk would break decoders.", nameof(type));
            if (!char.IsLower(type[1]))
                throw new ArgumentException("Chunk type must be private (lowercase second letter) or one of tEXt, zTXt, iTXt.", nameof(type));
            if (!char.IsUpper(type[2]))
                throw new ArgumentException("The third letter of a chunk type must be uppercase (reserved bit).", nameof(type));
            return type;
        }

        private static bool AllAsciiLetters(string value)
        {
            foreach (char c in value)
            {
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z'))
                    return false;
            }
            return true;
        }

        private static string ValidateKeyword(string keyword)
        {
            if (keyword == null)
                throw new ArgumentNullException(nameof(keyword));
            if (keyword.Length < 1 || keyword.Length > 79)
                throw new ArgumentException("Keyword must be 1 to 79 characters.", nameof(keyword));
            if (keyword[0] == ' ' || keyword[^1] == ' ')
                throw new ArgumentException("Keyword must not start or end with a space.", nameof(keyword));
            foreach (char c in keyword)
            {
                if (!(c >= 32 && c <= 126) && !(c >= 161 && c <= 255))
                    throw new ArgumentException("Keyword must be printable Latin-1.", nameof(keyword));
            }
            return keyword;
        }
    }
}
