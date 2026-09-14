using System;
using System.Buffers.Binary;
using System.IO;

using SteganoLib.Algorithms;

namespace SteganoLib.Video
{
    /// <summary>
    /// Runs an image algorithm over the frames of a video. The payload, prefixed with
    /// its length, is cut into one piece per frame and each piece is embedded with the
    /// frame algorithm, which brings its own header and keying. By default the pieces
    /// are proportional to frame capacity so every frame carries the same low rate;
    /// with <see cref="Spread"/> off, frames are filled one after another and the rest
    /// stay untouched. Extraction reads frames in order until the announced length is
    /// reached.
    /// </summary>
    /// <typeparam name="TFrame">Frame type the wrapped algorithm works on.</typeparam>
    public sealed class VideoCoding<TFrame> : IStegAlgorithm<IFrameSequence<TFrame>>
    {
        private const int HeaderSize = 4;

        public VideoCoding(IStegAlgorithm<TFrame> frameAlgorithm)
        {
            FrameAlgorithm = frameAlgorithm ?? throw new ArgumentNullException(nameof(frameAlgorithm));
        }

        public IStegAlgorithm<TFrame> FrameAlgorithm { get; }

        /// <summary>Distribute the payload over all frames in proportion to their capacity instead of filling frames in order.</summary>
        public bool Spread { get; set; } = true;

        public void EmbedBytes(byte[] data, IFrameSequence<TFrame> frames)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            long[] capacities = Capacities(frames);
            long total = Sum(capacities);
            long payloadLength = (long)HeaderSize + data.Length;
            if (payloadLength > total)
                throw new CapacityExceededException(data.Length, Math.Max(0, total - HeaderSize));

            var payload = new byte[payloadLength];
            BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)data.Length);
            data.CopyTo(payload, HeaderSize);

            long[] pieces = Spread ? Proportional(capacities, total, payloadLength) : Sequential(capacities, payloadLength);

            int last = pieces.Length - 1;
            while (last > 0 && pieces[last] == 0)
                last--;

            long offset = 0;
            for (int i = 0; i <= last; i++)
            {
                var piece = payload.AsSpan((int)offset, (int)pieces[i]).ToArray();
                frames.Modify(i, frame => FrameAlgorithm.EmbedBytes(piece, frame));
                offset += pieces[i];
            }
        }

        public byte[] ExtractBytes(IFrameSequence<TFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            using var buffer = new MemoryStream();
            long total = -1;
            for (int i = 0; i < frames.Count; i++)
            {
                var piece = frames.Read(i, frame => FrameAlgorithm.ExtractBytes(frame));
                buffer.Write(piece, 0, piece.Length);

                if (total < 0 && buffer.Length >= HeaderSize)
                {
                    uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer.GetBuffer());
                    if (length > int.MaxValue - HeaderSize)
                        return Array.Empty<byte>();
                    total = HeaderSize + length;
                }

                if (total >= 0 && buffer.Length >= total)
                    break;
            }

            if (total < 0 || buffer.Length < total)
                return Array.Empty<byte>();

            return buffer.GetBuffer().AsSpan(HeaderSize, (int)(total - HeaderSize)).ToArray();
        }

        public long Capacity(IFrameSequence<TFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            return Math.Max(0, Sum(Capacities(frames)) - HeaderSize);
        }

        private long[] Capacities(IFrameSequence<TFrame> frames)
        {
            var capacities = new long[frames.Count];
            for (int i = 0; i < capacities.Length; i++)
                capacities[i] = Math.Max(0, frames.Read(i, frame => FrameAlgorithm.Capacity(frame)));
            return capacities;
        }

        private static long Sum(long[] values)
        {
            long sum = 0;
            foreach (long v in values)
                sum += v;
            return sum;
        }

        private static long[] Proportional(long[] capacities, long total, long payloadLength)
        {
            var pieces = new long[capacities.Length];
            long assigned = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = (long)((decimal)capacities[i] * payloadLength / total);
                assigned += pieces[i];
            }

            // Rounding left a few bytes over; hand them to the first frames with room.
            for (int i = 0; assigned < payloadLength; i = (i + 1) % pieces.Length)
            {
                if (pieces[i] < capacities[i])
                {
                    pieces[i]++;
                    assigned++;
                }
            }
            return pieces;
        }

        private static long[] Sequential(long[] capacities, long payloadLength)
        {
            var pieces = new long[capacities.Length];
            long remaining = payloadLength;
            for (int i = 0; i < pieces.Length && remaining > 0; i++)
            {
                pieces[i] = Math.Min(capacities[i], remaining);
                remaining -= pieces[i];
            }
            return pieces;
        }
    }
}
