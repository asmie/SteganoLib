using System;

using SteganoLib.Coding;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// Wraps an algorithm so that the bytes it embeds are protected by an error
    /// correcting code. A few corrupted bytes in the extracted stream, from noise, a
    /// damaged file or a careless edit, are repaired before the payload is returned.
    /// Place it inside a <see cref="StegoPipeline{TCarrier}"/> so the envelope and its
    /// authentication tag are protected too. The inner algorithm's own header is not
    /// covered; damage there still loses the payload.
    /// </summary>
    public sealed class ErrorCorrectedAlgorithm<TCarrier> : IStegAlgorithm<TCarrier>
    {
        public ErrorCorrectedAlgorithm(IStegAlgorithm<TCarrier> inner)
            : this(inner, new ReedSolomonCode())
        {
        }

        public ErrorCorrectedAlgorithm(IStegAlgorithm<TCarrier> inner, IErrorCorrectionCode code)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        public IStegAlgorithm<TCarrier> Inner { get; }

        public IErrorCorrectionCode Code { get; }

        public void EmbedBytes(byte[] data, TCarrier carrier)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            long capacity = Capacity(carrier);
            if (!IsPossibleToEmbed(data.Length, carrier))
                throw new CapacityExceededException(data.Length, capacity);

            Inner.EmbedBytes(Code.Encode(data), carrier);
        }

        /// <summary>The repaired payload, or an empty array when the damage exceeds what the code can fix.</summary>
        public byte[] ExtractBytes(TCarrier carrier)
        {
            return TryExtractBytes(carrier, out var data, out _) ? data : Array.Empty<byte>();
        }

        /// <summary>Extract and report how many bytes had to be repaired.</summary>
        public bool TryExtractBytes(TCarrier carrier, out byte[] data, out int correctedSymbols)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            return Code.TryDecode(Inner.ExtractBytes(carrier), out data, out correctedSymbols);
        }

        /// <summary>Check the encoded length against the inner algorithm, including its framing requirements.</summary>
        public bool IsPossibleToEmbed(long dataLength, TCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));
            if (dataLength < 0 || dataLength > Capacity(carrier))
                return false;

            return Inner.IsPossibleToEmbed(Code.EncodedLength(dataLength), carrier);
        }

        public long Capacity(TCarrier carrier)
        {
            if (carrier == null) throw new ArgumentNullException(nameof(carrier));

            return Code.MaxDataLength(Inner.Capacity(carrier));
        }
    }
}
