#nullable enable

using System;

namespace SteganoLib.Audio
{
    /// <summary>A RIFF chunk other than fmt and data, kept verbatim so metadata survives re-encoding.</summary>
    public sealed class RiffChunk
    {
        public RiffChunk(string id, byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (id.Length != 4)
                throw new ArgumentException("Chunk id must be four characters.", nameof(id));

            Id = id;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public string Id { get; }

        /// <summary>Mutable body, shared with the constructor's input array. This wrapper does not copy it.</summary>
        public byte[] Payload { get; }
    }
}
