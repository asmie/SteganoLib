using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

using SteganoLib.Coding;

namespace SteganoLib.Algorithms
{
    /// <summary>A carrier viewed as an ordered sequence of one-bit slots.</summary>
    internal abstract class SlotCarrier<TSlot>
    {
        public abstract long TotalSlots();

        public abstract IEnumerable<TSlot> Slots();

        public abstract bool Read(TSlot slot);

        /// <summary>Set the slot to <c>bit</c>; <c>up</c> is a random direction hint for algorithms that move a value by one.</summary>
        public abstract void Write(TSlot slot, bool bit, bool up);

        public abstract double Cost(TSlot slot);
    }

    /// <summary>
    /// Header, direct and trellis embedding shared by the slot-based algorithms.
    /// Header layout: trellis height (0 = direct), trellis width, big-endian length.
    /// </summary>
    internal static class SlotEmbedding
    {
        public const int HeaderSize = 6;
        public const int HeaderBits = HeaderSize * 8;

        public static long Capacity(long totalSlots) => Math.Max(0, totalSlots / 8 - HeaderSize);

        public static void Embed<TSlot>(SlotCarrier<TSlot> carrier, byte[] data, SyndromeTrellisCoder coder, int maxTrellisWidth)
        {
            long capacity = Capacity(carrier.TotalSlots());
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            var directions = new BitArray(RandomNumberGenerator.GetBytes(HeaderSize + data.Length));

            if (coder == null)
            {
                using var slots = carrier.Slots().GetEnumerator();
                Write(carrier, slots, new BitArray(Header(0, 0, data.Length)), directions, 0, data.Length, capacity);
                Write(carrier, slots, new BitArray(data), directions, HeaderBits, data.Length, capacity);
                return;
            }

            EmbedWithTrellis(carrier, data, coder, maxTrellisWidth, directions, capacity);
        }

        public static byte[] Extract<TSlot>(SlotCarrier<TSlot> carrier)
        {
            long totalSlots = carrier.TotalSlots();
            using var slots = carrier.Slots().GetEnumerator();

            var header = new byte[HeaderSize];
            if (!ReadBytes(carrier, slots, header))
                return Array.Empty<byte>();

            int height = header[0];
            int width = header[1];
            int dataLength = (header[2] << 24) | (header[3] << 16) | (header[4] << 8) | header[5];
            if (dataLength < 0 || dataLength > Capacity(totalSlots))
                return Array.Empty<byte>();

            var result = new byte[dataLength];
            if (height == 0)
            {
                if (width != 0 || !ReadBytes(carrier, slots, result))
                    return Array.Empty<byte>();
                return result;
            }

            if (height < SyndromeTrellisCoder.MinHeight || height > SyndromeTrellisCoder.MaxHeight || width < 1)
                return Array.Empty<byte>();

            long messageBits = (long)dataLength * 8;
            long coverBits = messageBits * width;
            if (coverBits > totalSlots - HeaderBits)
                return Array.Empty<byte>();

            var stego = new bool[coverBits];
            for (long i = 0; i < coverBits; i++)
            {
                if (!slots.MoveNext())
                    return Array.Empty<byte>();
                stego[i] = carrier.Read(slots.Current);
            }

            var message = new bool[messageBits];
            new SyndromeTrellisCoder(height).Extract(stego, message);
            for (long i = 0; i < messageBits; i++)
            {
                if (message[i])
                    result[i / 8] |= (byte)(1 << (int)(i % 8));
            }
            return result;
        }

        private static void EmbedWithTrellis<TSlot>(SlotCarrier<TSlot> carrier, byte[] data, SyndromeTrellisCoder coder, int maxTrellisWidth, BitArray directions, long capacity)
        {
            long messageBits = (long)data.Length * 8;
            long available = carrier.TotalSlots() - HeaderBits;
            int width = messageBits == 0 ? 1 : (int)Math.Min(maxTrellisWidth, available / messageBits);
            if (width < 1)
                throw new CapacityExceededException(data.Length, capacity);

            using var slots = carrier.Slots().GetEnumerator();
            var headerSlots = Take(slots, HeaderBits);
            var coverSlots = Take(slots, messageBits * width);
            if (headerSlots.Count != HeaderBits || coverSlots.Count != messageBits * width)
                throw new CapacityExceededException(data.Length, capacity);

            // Costs come from the untouched cover, before the header is written.
            var cover = new bool[coverSlots.Count];
            var costs = new double[coverSlots.Count];
            for (int i = 0; i < coverSlots.Count; i++)
            {
                cover[i] = carrier.Read(coverSlots[i]);
                costs[i] = carrier.Cost(coverSlots[i]);
            }

            var message = new bool[messageBits];
            var payload = new BitArray(data);
            for (int i = 0; i < message.Length; i++)
                message[i] = payload[i];

            var stego = new bool[cover.Length];
            if (double.IsPositiveInfinity(coder.Embed(cover, costs, message, stego)))
                throw new CapacityExceededException(data.Length, capacity);

            var header = new BitArray(Header(coder.ConstraintHeight, width, data.Length));
            for (int i = 0; i < HeaderBits; i++)
                carrier.Write(headerSlots[i], header[i], directions[i]);

            for (int i = 0; i < stego.Length; i++)
            {
                if (stego[i] != cover[i])
                    carrier.Write(coverSlots[i], stego[i], directions[(HeaderBits + i) % directions.Length]);
            }
        }

        private static byte[] Header(int height, int width, int length)
        {
            return new[]
            {
                (byte)height,
                (byte)width,
                (byte)(length >> 24),
                (byte)(length >> 16),
                (byte)(length >> 8),
                (byte)length,
            };
        }

        private static List<TSlot> Take<TSlot>(IEnumerator<TSlot> slots, long count)
        {
            var list = new List<TSlot>((int)Math.Min(count, int.MaxValue));
            for (long i = 0; i < count && slots.MoveNext(); i++)
                list.Add(slots.Current);
            return list;
        }

        private static void Write<TSlot>(SlotCarrier<TSlot> carrier, IEnumerator<TSlot> slots, BitArray bits, BitArray directions, int directionOffset, int dataLength, long capacity)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                if (!slots.MoveNext())
                    throw new CapacityExceededException(dataLength, capacity);
                carrier.Write(slots.Current, bits[i], directions[directionOffset + i]);
            }
        }

        private static bool ReadBytes<TSlot>(SlotCarrier<TSlot> carrier, IEnumerator<TSlot> slots, byte[] target)
        {
            for (int i = 0; i < target.Length * 8; i++)
            {
                if (!slots.MoveNext())
                    return false;
                if (carrier.Read(slots.Current))
                    target[i / 8] |= (byte)(1 << (i % 8));
            }
            return true;
        }
    }
}
