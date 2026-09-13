using System;
using System.Collections;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Selection;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// LSB (Least Significant Bit) image steganography.
    /// <para>
    /// Bits go into the least significant bit of the enabled colour channels.
    /// Alpha is never touched: changing it is easy to spot and breaks on opaque formats.
    /// </para>
    /// <para>
    /// Pixel order comes from an <see cref="IPixelSelector"/>; sender and receiver
    /// must use an equivalent selector. A 4-byte big-endian length header is written
    /// in front of the payload so extraction knows where to stop.
    /// </para>
    /// </summary>
    public class LSB : IStegAlgorithm<Image<Rgba32>>
    {
        private const int HeaderSize = 4;
        private const int HeaderBits = HeaderSize * 8;

        private readonly IPixelSelector _selector;
        private int _modifyMaxBitsInByte = 1;

        public LSB(IPixelSelector selector)
        {
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        /// <summary>
        /// Embed <paramref name="data"/> into <paramref name="image"/> in place.
        /// </summary>
        /// <returns><c>false</c> if the image lacks capacity for the payload.</returns>
        public bool EmbedBytes(byte[] data, Image<Rgba32> image)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            if (!IsPossibleToEmbed(data.Length, image))
                return false;

            byte[] framed = new byte[HeaderSize + data.Length];
            framed[0] = (byte)(data.Length >> 24);
            framed[1] = (byte)(data.Length >> 16);
            framed[2] = (byte)(data.Length >> 8);
            framed[3] = (byte)data.Length;
            Array.Copy(data, 0, framed, HeaderSize, data.Length);

            var bits = new BitArray(framed);
            using var slots = Slots(image.Width, image.Height).GetEnumerator();

            for (int i = 0; i < bits.Length; i++)
            {
                if (!slots.MoveNext())
                    return false;

                var (x, y, channel) = slots.Current;
                var pixel = image[x, y];
                WriteBit(ref pixel, channel, bits[i]);
                image[x, y] = pixel;
            }

            return true;
        }

        /// <summary>
        /// Extract a previously embedded payload from <paramref name="image"/>.
        /// </summary>
        /// <returns>
        /// The payload, or an empty array if the length header is not plausible for this image.
        /// </returns>
        public byte[] ExtractBytes(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            using var slots = Slots(image.Width, image.Height).GetEnumerator();

            byte[] header = new byte[HeaderSize];
            if (!ReadBytes(image, slots, header))
                return Array.Empty<byte>();

            int dataLength = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (dataLength < 0 || !IsPossibleToEmbed(dataLength, image))
                return Array.Empty<byte>();

            byte[] result = new byte[dataLength];
            if (!ReadBytes(image, slots, result))
                return Array.Empty<byte>();

            return result;
        }

        /// <summary>
        /// Whether <paramref name="dataLength"/> bytes plus the header fit into
        /// <paramref name="image"/> with the current channel and bits-per-pixel settings.
        /// Returns <c>false</c> when no channel is enabled.
        /// </summary>
        public bool IsPossibleToEmbed(int dataLength, Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            if (dataLength < 0)
                return false;

            int channels = EnabledChannelCount();
            if (channels == 0)
                return false;

            long totalBits = ((long)dataLength + HeaderSize) * 8;
            long bitsPerPixel = Math.Min(_modifyMaxBitsInByte, channels);
            long pixelsPerCycle = (channels + bitsPerPixel - 1) / bitsPerPixel;
            long fullCycles = totalBits / channels;
            long remainingBits = totalBits % channels;
            long pixelsForRemaining = (remainingBits + bitsPerPixel - 1) / bitsPerPixel;
            long pixelsNeeded = fullCycles * pixelsPerCycle + pixelsForRemaining;

            return pixelsNeeded <= (long)image.Width * image.Height;
        }

        /// <summary>Whether to modify the red channel. Default <c>true</c>.</summary>
        public bool ModifyR { get; set; } = true;

        /// <summary>Whether to modify the green channel. Default <c>true</c>.</summary>
        public bool ModifyG { get; set; } = true;

        /// <summary>Whether to modify the blue channel. Default <c>true</c>.</summary>
        public bool ModifyB { get; set; } = true;

        /// <summary>
        /// Maximum number of bits written into one pixel before moving to the next one.
        /// Default <c>1</c>. Values above the number of enabled channels behave like the channel count.
        /// </summary>
        public int ModifyMaxBitsInByte
        {
            get => _modifyMaxBitsInByte;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be at least 1.");
                _modifyMaxBitsInByte = value;
            }
        }

        /// <summary>Selector that decides the pixel order.</summary>
        public IPixelSelector PixelSelector => _selector;

        private int EnabledChannelCount() => (ModifyR ? 1 : 0) + (ModifyG ? 1 : 0) + (ModifyB ? 1 : 0);

        // One slot per bit: a pixel and the channel index (0 = R, 1 = G, 2 = B).
        // Channels rotate across pixels so a cycle of C bits is spread over ceil(C / M) pixels.
        private IEnumerable<(int X, int Y, int Channel)> Slots(int width, int height)
        {
            var channels = new List<int>(3);
            if (ModifyR) channels.Add(0);
            if (ModifyG) channels.Add(1);
            if (ModifyB) channels.Add(2);
            if (channels.Count == 0)
                yield break;

            int perPixel = Math.Min(_modifyMaxBitsInByte, channels.Count);
            int next = 0;

            foreach (var p in _selector.Pixels(width, height))
            {
                int written = 0;
                while (next < channels.Count && written < perPixel)
                {
                    yield return (p.X, p.Y, channels[next]);
                    next++;
                    written++;
                }

                if (next == channels.Count)
                    next = 0;
            }
        }

        private static bool ReadBytes(Image<Rgba32> image, IEnumerator<(int X, int Y, int Channel)> slots, byte[] target)
        {
            for (int i = 0; i < target.Length * 8; i++)
            {
                if (!slots.MoveNext())
                    return false;

                var (x, y, channel) = slots.Current;
                if (ReadBit(image[x, y], channel))
                    target[i / 8] |= (byte)(1 << (i % 8));
            }

            return true;
        }

        private static void WriteBit(ref Rgba32 pixel, int channel, bool bit)
        {
            switch (channel)
            {
                case 0: pixel.R = SetLsb(pixel.R, bit); break;
                case 1: pixel.G = SetLsb(pixel.G, bit); break;
                default: pixel.B = SetLsb(pixel.B, bit); break;
            }
        }

        private static bool ReadBit(Rgba32 pixel, int channel)
        {
            byte value = channel switch
            {
                0 => pixel.R,
                1 => pixel.G,
                _ => pixel.B,
            };
            return (value & 1) == 1;
        }

        private static byte SetLsb(byte value, bool bit) => (byte)(bit ? (value | 1) : (value & 0xFE));
    }
}
