using System;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib
{
    /// <summary>
    /// Wraps an algorithm with a <see cref="PayloadEnvelope"/>: payloads are sealed
    /// before embedding and verified after extraction.
    /// </summary>
    public sealed class StegoPipeline<TCarrier>
    {
        private readonly IStegAlgorithm<TCarrier> _algorithm;
        private readonly PayloadEnvelope _envelope;

        public StegoPipeline(IStegAlgorithm<TCarrier> algorithm)
            : this(algorithm, new PayloadEnvelope())
        {
        }

        public StegoPipeline(IStegAlgorithm<TCarrier> algorithm, PayloadEnvelope envelope)
        {
            _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
            _envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        }

        public IStegAlgorithm<TCarrier> Algorithm => _algorithm;

        public PayloadEnvelope Envelope => _envelope;

        /// <exception cref="CapacityExceededException">The carrier cannot hold the sealed payload.</exception>
        public void Embed(byte[] data, TCarrier carrier, StegoKey key)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            long capacity = Capacity(carrier);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            _algorithm.EmbedBytes(_envelope.Seal(data, key), carrier);
        }

        public ExtractResult Extract(TCarrier carrier, StegoKey key)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return _envelope.Open(_algorithm.ExtractBytes(carrier), key);
        }

        /// <summary>Largest payload that fits once envelope overhead is taken off. Compression may allow more.</summary>
        public long Capacity(TCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return Math.Max(0, _algorithm.Capacity(carrier) - _envelope.Overhead);
        }

        public bool IsPossibleToEmbed(long dataLength, TCarrier carrier)
        {
            return dataLength >= 0 && dataLength <= Capacity(carrier);
        }
    }
}
