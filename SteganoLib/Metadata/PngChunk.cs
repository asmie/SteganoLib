using System;

namespace SteganoLib.Metadata
{
    /// <summary>One PNG chunk: four-letter type and body, without length and CRC.</summary>
    public sealed class PngChunk
    {
        public PngChunk(string type, byte[] data)
        {
            if (type == null || type.Length != 4)
                throw new ArgumentException("Chunk type must be four characters.", nameof(type));

            Type = type;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public string Type { get; }

        public byte[] Data { get; }

        /// <summary>Lowercase first letter: decoders may ignore the chunk.</summary>
        public bool IsAncillary => char.IsLower(Type[0]);
    }
}
