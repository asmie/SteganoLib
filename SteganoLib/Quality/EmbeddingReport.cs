using System;

namespace SteganoLib.Quality
{
    /// <summary>What an embedding cost: how full the carrier is and how far it moved from the cover.</summary>
    /// <typeparam name="TDistortion"><see cref="ImageComparison"/>, <see cref="AudioComparison"/> or <see cref="JpegComparison"/>.</typeparam>
    public sealed class EmbeddingReport<TDistortion>
    {
        public EmbeddingReport(long payloadBytes, long capacity, TDistortion distortion)
        {
            if (payloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(payloadBytes));
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            PayloadBytes = payloadBytes;
            Capacity = capacity;
            Distortion = distortion ?? throw new ArgumentNullException(nameof(distortion));
        }

        /// <summary>Bytes the caller asked to hide, before any envelope or coding overhead.</summary>
        public long PayloadBytes { get; }

        /// <summary>What the cover could have held with the same settings.</summary>
        public long Capacity { get; }

        /// <summary>Payload over capacity, 0 to 1. Lower rates are harder to detect.</summary>
        public double EmbeddingRate => Capacity == 0 ? 0 : Math.Min(1, (double)PayloadBytes / Capacity);

        public TDistortion Distortion { get; }
    }
}
