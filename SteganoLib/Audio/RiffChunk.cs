using System;

namespace SteganoLib.Audio
{
    /// <summary>A RIFF chunk other than fmt and data, kept verbatim so metadata survives re-encoding.</summary>
    public sealed class RiffChunk
    {
        public RiffChunk(string id, byte[] payload)
        {
            if (id == null || id.Length != 4)
                throw new ArgumentException("Chunk id must be four characters.", nameof(id));

            Id = id;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public string Id { get; }

        public byte[] Payload { get; }
    }
}
