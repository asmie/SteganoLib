using System;
using System.Collections.Generic;

using SteganoLib.Algorithms;

namespace SteganoLib.Sharing
{
    /// <summary>
    /// Spreads a payload over several carriers so that any <see cref="Threshold"/> of them
    /// recover it and fewer reveal nothing. The payload is split with
    /// <see cref="ShamirSecretSharing"/> and share i goes into carrier i through the inner
    /// algorithm. Extraction accepts any subset of the carriers, in any order; carriers that
    /// hold no share are skipped. Wrap it in a <see cref="StegoPipeline{TCarrier}"/> so a
    /// wrong or corrupted share is caught by the envelope instead of yielding garbage.
    /// </summary>
    public sealed class SharedCoding<TCarrier> : IStegAlgorithm<IReadOnlyList<TCarrier>>
    {
        public SharedCoding(IStegAlgorithm<TCarrier> inner, int threshold)
        {
            if (threshold < 1 || threshold > 255)
                throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be 1 to 255.");

            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Threshold = threshold;
        }

        public IStegAlgorithm<TCarrier> Inner { get; }

        /// <summary>Carriers needed to recover the payload.</summary>
        public int Threshold { get; }

        /// <exception cref="ArgumentException">Fewer carriers than the threshold, or more than 255.</exception>
        public void EmbedBytes(byte[] data, IReadOnlyList<TCarrier> carriers)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Validate(carriers);
            if (carriers.Count < Threshold)
                throw new ArgumentException($"At least {Threshold} carriers are needed, {carriers.Count} given.", nameof(carriers));

            long capacity = Capacity(carriers);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            var shares = new ShamirSecretSharing(Threshold, carriers.Count).Split(data);
            for (int i = 0; i < carriers.Count; i++)
                Inner.EmbedBytes(shares[i].ToBytes(), carriers[i]);
        }

        /// <summary>The payload, or an empty array when fewer than the threshold number of carriers hold shares.</summary>
        public byte[] ExtractBytes(IReadOnlyList<TCarrier> carriers)
        {
            Validate(carriers);

            var shares = new List<Share>();
            var indices = new HashSet<int>();
            foreach (var carrier in carriers)
            {
                var share = Share.TryParse(Inner.ExtractBytes(carrier));
                if (share == null || share.Threshold != Threshold)
                    continue;
                if (shares.Count > 0 && share.Data.Length != shares[0].Data.Length)
                    continue;
                if (indices.Add(share.Index))
                    shares.Add(share);
            }

            return shares.Count >= Threshold ? ShamirSecretSharing.Combine(shares) : Array.Empty<byte>();
        }

        /// <summary>Every carrier receives a share as long as the payload, so the smallest carrier decides.</summary>
        public long Capacity(IReadOnlyList<TCarrier> carriers)
        {
            Validate(carriers);

            long capacity = long.MaxValue;
            foreach (var carrier in carriers)
                capacity = Math.Min(capacity, Inner.Capacity(carrier));
            return Math.Max(0, capacity - Share.HeaderSize);
        }

        private static void Validate(IReadOnlyList<TCarrier> carriers)
        {
            if (carriers == null) throw new ArgumentNullException(nameof(carriers));
            if (carriers.Count == 0) throw new ArgumentException("At least one carrier is required.", nameof(carriers));
            if (carriers.Count > 255) throw new ArgumentException("At most 255 carriers are supported.", nameof(carriers));
            foreach (var carrier in carriers)
            {
                if (carrier == null)
                    throw new ArgumentException("Carriers must not be null.", nameof(carriers));
            }
        }
    }
}
