using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;

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
    /// <para>
    /// With <see cref="LsbEmbeddingMode.Match"/> (the default) a channel whose LSB has
    /// to change is moved up or down by one at random instead of having the bit
    /// overwritten. Extraction is identical; the histogram artefacts that the
    /// chi-square attack looks for are not produced.
    /// </para>
    /// </summary>
    public class LSB : IStegAlgorithm<Image<Rgba32>>
    {
        private const int HeaderSize = 4;

        private readonly IPixelSelector _selector;
        private ColorChannels _channels = ColorChannels.All;
        private int _bitsPerPixel = 1;

        public LSB(IPixelSelector selector)
        {
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, Image<Rgba32> image)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            long capacity = Capacity(image);
            if (data.Length > capacity)
                throw new CapacityExceededException(data.Length, capacity);

            byte[] framed = new byte[HeaderSize + data.Length];
            framed[0] = (byte)(data.Length >> 24);
            framed[1] = (byte)(data.Length >> 16);
            framed[2] = (byte)(data.Length >> 8);
            framed[3] = (byte)data.Length;
            Array.Copy(data, 0, framed, HeaderSize, data.Length);

            var bits = new BitArray(framed);
            var directions = EmbeddingMode == LsbEmbeddingMode.Match
                ? new BitArray(RandomNumberGenerator.GetBytes(framed.Length))
                : null;
            using var slots = Slots(image.Width, image.Height).GetEnumerator();

            for (int i = 0; i < bits.Length; i++)
            {
                if (!slots.MoveNext())
                    throw new CapacityExceededException(data.Length, capacity);

                var (x, y, channel) = slots.Current;
                var pixel = image[x, y];
                WriteBit(ref pixel, channel, bits[i], directions != null && directions[i]);
                image[x, y] = pixel;
            }
        }

        /// <inheritdoc />
        /// <returns>The payload, or an empty array if the length header is not plausible for this image.</returns>
        public byte[] ExtractBytes(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            using var slots = Slots(image.Width, image.Height).GetEnumerator();

            byte[] header = new byte[HeaderSize];
            if (!ReadBytes(image, slots, header))
                return Array.Empty<byte>();

            int dataLength = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
            if (dataLength < 0 || dataLength > Capacity(image))
                return Array.Empty<byte>();

            byte[] result = new byte[dataLength];
            if (!ReadBytes(image, slots, result))
                return Array.Empty<byte>();

            return result;
        }

        /// <inheritdoc />
        public long Capacity(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            long pixels = (long)image.Width * image.Height;
            int channels = ChannelCount;
            int perPixel = Math.Min(_bitsPerPixel, channels);

            // A cycle spreads one bit per enabled channel over ceil(channels / perPixel) pixels.
            long pixelsPerCycle = (channels + perPixel - 1) / perPixel;
            long fullCycles = pixels / pixelsPerCycle;
            long leftoverPixels = pixels % pixelsPerCycle;
            long totalBits = fullCycles * channels + Math.Min(leftoverPixels * perPixel, channels);

            return Math.Max(0, totalBits / 8 - HeaderSize);
        }

        /// <summary>Channels that may carry bits. Default <see cref="ColorChannels.All"/>.</summary>
        public ColorChannels Channels
        {
            get => _channels;
            set
            {
                if ((value & ColorChannels.All) == ColorChannels.None)
                    throw new ArgumentException("At least one channel must be enabled.", nameof(value));
                if ((value & ~ColorChannels.All) != 0)
                    throw new ArgumentException("Unknown channel flag.", nameof(value));
                _channels = value;
            }
        }

        /// <summary>
        /// Bits written into one pixel before moving to the next one. Default <c>1</c>.
        /// Values above the number of enabled channels behave like the channel count.
        /// </summary>
        public int BitsPerPixel
        {
            get => _bitsPerPixel;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be at least 1.");
                _bitsPerPixel = value;
            }
        }

        /// <summary>How a channel value is changed when its LSB does not match. Default <see cref="LsbEmbeddingMode.Match"/>.</summary>
        public LsbEmbeddingMode EmbeddingMode { get; set; } = LsbEmbeddingMode.Match;

        /// <summary>Selector that decides the pixel order.</summary>
        public IPixelSelector PixelSelector => _selector;

        private int ChannelCount => BitOperations.PopCount((uint)_channels);

        // One slot per bit: a pixel and the channel index (0 = R, 1 = G, 2 = B).
        // Channels rotate across pixels so a cycle of C bits is spread over ceil(C / M) pixels.
        private IEnumerable<(int X, int Y, int Channel)> Slots(int width, int height)
        {
            var channels = new List<int>(3);
            if (_channels.HasFlag(ColorChannels.Red)) channels.Add(0);
            if (_channels.HasFlag(ColorChannels.Green)) channels.Add(1);
            if (_channels.HasFlag(ColorChannels.Blue)) channels.Add(2);

            int perPixel = Math.Min(_bitsPerPixel, channels.Count);
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

        private void WriteBit(ref Rgba32 pixel, int channel, bool bit, bool up)
        {
            switch (channel)
            {
                case 0: pixel.R = Adjust(pixel.R, bit, up); break;
                case 1: pixel.G = Adjust(pixel.G, bit, up); break;
                default: pixel.B = Adjust(pixel.B, bit, up); break;
            }
        }

        private byte Adjust(byte value, bool bit, bool up)
        {
            if (((value & 1) == 1) == bit)
                return value;

            if (EmbeddingMode == LsbEmbeddingMode.Replace)
                return (byte)(bit ? (value | 1) : (value & 0xFE));

            if (value == 0) return 1;
            if (value == 255) return 254;
            return (byte)(up ? value + 1 : value - 1);
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
    }
}
