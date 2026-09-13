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

        /// <returns><c>false</c> when the sealed payload does not fit.</returns>
        public bool Embed(byte[] data, TCarrier carrier, StegoKey key)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return _algorithm.EmbedBytes(_envelope.Seal(data, key), carrier);
        }

        public ExtractResult Extract(TCarrier carrier, StegoKey key)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return _envelope.Open(_algorithm.ExtractBytes(carrier), key);
        }

        public bool IsPossibleToEmbed(int dataLength, TCarrier carrier)
        {
            if (dataLength < 0)
                throw new ArgumentOutOfRangeException(nameof(dataLength));
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return _algorithm.IsPossibleToEmbed(dataLength + _envelope.Overhead(dataLength), carrier);
        }
    }
}
