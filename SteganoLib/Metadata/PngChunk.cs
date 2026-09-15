#nullable enable

using System;

namespace SteganoLib.Metadata
{
    /// <summary>One PNG chunk: four-letter type and body, without length and CRC.</summary>
    public sealed class PngChunk
    {
        public PngChunk(string type, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type.Length != 4)
                throw new ArgumentException("Chunk type must be four characters.", nameof(type));
            foreach (char c in type)
                if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
                    throw new ArgumentException("Chunk type must contain only ASCII letters.", nameof(type));

            Type = type;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public string Type { get; }

        /// <summary>Mutable body, shared with the constructor's input array. This wrapper does not copy it.</summary>
        public byte[] Data { get; }

        /// <summary>Lowercase first letter: decoders may ignore the chunk.</summary>
        public bool IsAncillary => char.IsLower(Type[0]);
    }
}
