#nullable enable

using System;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib
{
    /// <summary>
    /// Wraps an algorithm with a <see cref="PayloadEnvelope"/>: payloads are sealed
    /// before embedding and verified after extraction.
    /// Algorithm and envelope objects are shared, not cloned. Keep their configuration
    /// and carrier data stable during each operation. Concurrent use requires independent
    /// carriers and thread-safe algorithm, selector, cost model and codec implementations.
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

            ArgumentNullException.ThrowIfNull(key);
            var sealedData = _envelope.Seal(data, key);
            if (!_algorithm.IsPossibleToEmbed(sealedData.Length, carrier))
                throw new CapacityExceededException(sealedData.Length, _algorithm.Capacity(carrier));

            _algorithm.EmbedBytes(sealedData, carrier);
        }

        public ExtractResult Extract(TCarrier carrier, StegoKey key)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            ArgumentNullException.ThrowIfNull(key);
            var data = _algorithm.ExtractBytes(carrier)
                ?? throw new InvalidOperationException("The algorithm returned null instead of a payload byte array.");
            return _envelope.Open(data, key);
        }

        /// <summary>
        /// Payload length budget after envelope overhead, without accounting for compression.
        /// Compression may allow larger payloads. Zero can also mean that the envelope does
        /// not fit; use <see cref="IsPossibleToEmbed"/> to check an empty payload.
        /// </summary>
        public long Capacity(TCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return Math.Max(0, _algorithm.Capacity(carrier) - _envelope.Overhead);
        }

        /// <summary>
        /// Check the uncompressed payload length, including envelope and inner framing.
        /// False does not rule out a compressible payload. Payload-dependent costs
        /// in the inner algorithm may still prevent embedding after this check succeeds.
        /// </summary>
        public bool IsPossibleToEmbed(long dataLength, TCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));
            if (dataLength < 0 || dataLength > long.MaxValue - _envelope.Overhead)
                return false;

            return _algorithm.IsPossibleToEmbed(dataLength + _envelope.Overhead, carrier);
        }
    }
}
