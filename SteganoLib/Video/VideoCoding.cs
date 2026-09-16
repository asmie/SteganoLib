#nullable enable

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
    /// reached. Each operation captures Spread before callbacks. Keep the frame sequence
    /// and the shared frame algorithm's configuration stable during operations.
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

        /// <summary>
        /// Check frame capacities and piece feasibility before modifying frames in order.
        /// If embedding fails, earlier frames may already be changed, the failing frame
        /// follows the sequence and algorithm's failure contracts, and later frames remain untouched.
        /// </summary>
        public void EmbedBytes(byte[] data, IFrameSequence<TFrame> frames)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            bool spread = Spread;
            if (data.Length > Array.MaxLength - HeaderSize)
                throw new ArgumentException("Payload and video header exceed the maximum byte array length.", nameof(data));
            long[] capacities = Capacities(frames);
            UInt128 total = Sum(capacities);
            long payloadLength = (long)HeaderSize + data.Length;
            if ((UInt128)payloadLength > total)
                throw new CapacityExceededException(data.Length, PayloadCapacity(total));

            long[] pieces = spread ? Proportional(capacities, total, payloadLength) : Sequential(capacities, payloadLength);
            if (!CanEmbedPieces(frames, pieces))
                throw new CapacityExceededException(data.Length, PayloadCapacity(total));

            var payload = new byte[payloadLength];
            BinaryPrimitives.WriteUInt32BigEndian(payload, (uint)data.Length);
            data.CopyTo(payload, HeaderSize);

            int last = LastUsedFrame(pieces);

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

            int count = FrameCount(frames);
            using var buffer = new MemoryStream();
            long total = -1;
            for (int i = 0; i < count; i++)
            {
                var piece = frames.Read(i, frame => FrameAlgorithm.ExtractBytes(frame))
                    ?? throw new InvalidOperationException("The frame algorithm returned null instead of a payload byte array.");
                buffer.Write(piece, 0, piece.Length);

                if (total < 0 && buffer.Length >= HeaderSize)
                {
                    uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer.GetBuffer());
                    if (length > Array.MaxLength - HeaderSize)
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

        /// <summary>Check room for the video length header and each frame's piece before modifying any frame.</summary>
        public bool IsPossibleToEmbed(long dataLength, IFrameSequence<TFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (dataLength < 0 || dataLength > long.MaxValue - HeaderSize)
                return false;

            bool spread = Spread;
            var capacities = Capacities(frames);
            UInt128 total = Sum(capacities);
            long payloadLength = dataLength + HeaderSize;
            if ((UInt128)payloadLength > total)
                return false;

            var pieces = spread ? Proportional(capacities, total, payloadLength) : Sequential(capacities, payloadLength);
            return CanEmbedPieces(frames, pieces);
        }

        private bool CanEmbedPieces(IFrameSequence<TFrame> frames, long[] pieces)
        {
            int last = LastUsedFrame(pieces);
            for (int i = 0; i <= last; i++)
            {
                if (!frames.Read(i, frame => FrameAlgorithm.IsPossibleToEmbed(pieces[i], frame)))
                    return false;
            }
            return true;
        }

        private static int LastUsedFrame(long[] pieces)
        {
            int last = pieces.Length - 1;
            while (last >= 0 && pieces[last] == 0)
                last--;
            return last;
        }

        public long Capacity(IFrameSequence<TFrame> frames)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            return PayloadCapacity(Sum(Capacities(frames)));
        }

        private long[] Capacities(IFrameSequence<TFrame> frames)
        {
            var capacities = new long[FrameCount(frames)];
            for (int i = 0; i < capacities.Length; i++)
            {
                capacities[i] = frames.Read(i, frame => FrameAlgorithm.Capacity(frame));
                if (capacities[i] < 0)
                    throw new InvalidOperationException("The frame algorithm returned a negative capacity.");
            }
            return capacities;
        }

        private static int FrameCount(IFrameSequence<TFrame> frames)
        {
            int count = frames.Count;
            if (count < 0)
                throw new InvalidOperationException("The sequence returned a negative frame count.");
            return count;
        }

        private static long PayloadCapacity(UInt128 total)
        {
            if (total <= HeaderSize) return 0;
            UInt128 capacity = total - HeaderSize;
            return capacity > (UInt128)long.MaxValue ? long.MaxValue : (long)capacity;
        }

        private static UInt128 Sum(long[] values)
        {
            // At most int.MaxValue frames, each with a nonnegative long capacity.
            UInt128 sum = 0;
            foreach (long v in values)
                sum += (UInt128)v;
            return sum;
        }

        private static long[] Proportional(long[] capacities, UInt128 total, long payloadLength)
        {
            var pieces = new long[capacities.Length];
            long assigned = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = (long)((UInt128)capacities[i] * (UInt128)payloadLength / total);
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
