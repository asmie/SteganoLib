using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Coding;
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
    /// must use an equivalent selector. A 6-byte header (trellis height, trellis
    /// width, big-endian length) is written in front of the payload.
    /// </para>
    /// <para>
    /// With <see cref="LsbEmbeddingMode.Match"/> (the default) a channel whose LSB has
    /// to change is moved up or down by one at random instead of having the bit
    /// overwritten. Extraction is identical; the histogram artefacts that the
    /// chi-square attack looks for are not produced.
    /// </para>
    /// <para>
    /// Set <see cref="TrellisCoder"/> to embed the payload with syndrome-trellis codes:
    /// the coder picks which slots to change so the total <see cref="CostModel"/> cost
    /// is small. The header is still written plainly so the receiver can size the code.
    /// </para>
    /// </summary>
    public class LSB : IStegAlgorithm<Image<Rgba32>>
    {
        private const int HeaderSize = 6;
        private const int HeaderBits = HeaderSize * 8;

        private readonly IPixelSelector _selector;
        private ColorChannels _channels = ColorChannels.All;
        private int _bitsPerPixel = 1;
        private int _maxTrellisWidth = 64;
        private IPixelCostModel _costModel = new TextureCostModel();

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

            int stableShift = StableShift();
            var directions = EmbeddingMode == LsbEmbeddingMode.Match
                ? new BitArray(RandomNumberGenerator.GetBytes(HeaderSize + data.Length))
                : null;

            if (TrellisCoder == null)
            {
                using var slots = Slots(image).GetEnumerator();
                Write(image, slots, new BitArray(Header(0, 0, data.Length)), directions, 0, stableShift, capacity, data.Length);
                Write(image, slots, new BitArray(data), directions, HeaderBits, stableShift, capacity, data.Length);
                return;
            }

            EmbedWithTrellis(data, image, directions, stableShift, capacity);
        }

        /// <inheritdoc />
        /// <returns>The payload, or an empty array if the header is not plausible for this image.</returns>
        public byte[] ExtractBytes(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            using var slots = Slots(image).GetEnumerator();

            var header = new byte[HeaderSize];
            if (!ReadBytes(image, slots, header))
                return Array.Empty<byte>();

            int height = header[0];
            int width = header[1];
            int dataLength = (header[2] << 24) | (header[3] << 16) | (header[4] << 8) | header[5];
            if (dataLength < 0 || dataLength > Capacity(image))
                return Array.Empty<byte>();

            var result = new byte[dataLength];
            if (height == 0)
            {
                if (width != 0 || !ReadBytes(image, slots, result))
                    return Array.Empty<byte>();
                return result;
            }

            if (height < SyndromeTrellisCoder.MinHeight || height > SyndromeTrellisCoder.MaxHeight || width < 1)
                return Array.Empty<byte>();

            long messageBits = (long)dataLength * 8;
            long coverBits = messageBits * width;
            if (coverBits > TotalSlots(image) - HeaderBits)
                return Array.Empty<byte>();

            var stego = new bool[coverBits];
            for (long i = 0; i < coverBits; i++)
            {
                if (!slots.MoveNext())
                    return Array.Empty<byte>();
                var (x, y, channel) = slots.Current;
                stego[i] = ReadBit(image[x, y], channel);
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

        /// <inheritdoc />
        public long Capacity(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            return Math.Max(0, TotalSlots(image) / 8 - HeaderSize);
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

        /// <summary>Syndrome-trellis coder for the payload; <c>null</c> (default) writes bits directly.</summary>
        public SyndromeTrellisCoder TrellisCoder { get; set; }

        /// <summary>Cost of changing a slot, consulted only when <see cref="TrellisCoder"/> is set. Default <see cref="TextureCostModel"/>.</summary>
        public IPixelCostModel CostModel
        {
            get => _costModel;
            set => _costModel = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Upper bound on cover bits per message bit for the trellis code (1 to 255).
        /// More cover bits give fewer changes but cost time and memory. Default 64.
        /// </summary>
        public int MaxTrellisWidth
        {
            get => _maxTrellisWidth;
            set
            {
                if (value < 1 || value > 255)
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be between 1 and 255.");
                _maxTrellisWidth = value;
            }
        }

        private void EmbedWithTrellis(byte[] data, Image<Rgba32> image, BitArray directions, int stableShift, long capacity)
        {
            long messageBits = (long)data.Length * 8;
            long available = TotalSlots(image) - HeaderBits;
            int width = messageBits == 0 ? 1 : (int)Math.Min(_maxTrellisWidth, available / messageBits);
            if (width < 1)
                throw new CapacityExceededException(data.Length, capacity);

            using var slots = Slots(image).GetEnumerator();
            var headerSlots = Take(slots, HeaderBits);
            var coverSlots = Take(slots, messageBits * width);
            if (headerSlots.Count != HeaderBits || coverSlots.Count != messageBits * width)
                throw new CapacityExceededException(data.Length, capacity);

            // Costs come from the untouched cover, before the header is written.
            var cover = new bool[coverSlots.Count];
            var costs = new double[coverSlots.Count];
            for (int i = 0; i < coverSlots.Count; i++)
            {
                var (x, y, channel) = coverSlots[i];
                cover[i] = ReadBit(image[x, y], channel);
                costs[i] = _costModel.Cost(image, x, y, channel);
            }

            var message = new bool[messageBits];
            var payload = new BitArray(data);
            for (int i = 0; i < message.Length; i++)
                message[i] = payload[i];

            var stego = new bool[cover.Length];
            double distortion = TrellisCoder.Embed(cover, costs, message, stego);
            if (double.IsPositiveInfinity(distortion))
                throw new CapacityExceededException(data.Length, capacity);

            var header = new BitArray(Header(TrellisCoder.ConstraintHeight, width, data.Length));
            for (int i = 0; i < HeaderBits; i++)
            {
                var (x, y, channel) = headerSlots[i];
                var pixel = image[x, y];
                WriteBit(ref pixel, channel, header[i], directions != null && directions[i], stableShift);
                image[x, y] = pixel;
            }

            for (int i = 0; i < stego.Length; i++)
            {
                if (stego[i] == cover[i])
                    continue;
                var (x, y, channel) = coverSlots[i];
                var pixel = image[x, y];
                WriteBit(ref pixel, channel, stego[i], directions != null && directions[(HeaderBits + i) % directions.Length], stableShift);
                image[x, y] = pixel;
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

        private static List<(int X, int Y, int Channel)> Take(IEnumerator<(int X, int Y, int Channel)> slots, long count)
        {
            var list = new List<(int X, int Y, int Channel)>((int)Math.Min(count, int.MaxValue));
            for (long i = 0; i < count && slots.MoveNext(); i++)
                list.Add(slots.Current);
            return list;
        }

        private void Write(Image<Rgba32> image, IEnumerator<(int X, int Y, int Channel)> slots, BitArray bits, BitArray directions, int directionOffset, int stableShift, long capacity, int dataLength)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                if (!slots.MoveNext())
                    throw new CapacityExceededException(dataLength, capacity);

                var (x, y, channel) = slots.Current;
                var pixel = image[x, y];
                WriteBit(ref pixel, channel, bits[i], directions != null && directions[directionOffset + i], stableShift);
                image[x, y] = pixel;
            }
        }

        private int ChannelCount => BitOperations.PopCount((uint)_channels);

        // Number of bit slots the selector and channel settings expose for this image.
        private long TotalSlots(Image<Rgba32> image)
        {
            long pixels = _selector is IContentAwarePixelSelector aware
                ? aware.Pixels(image).LongCount()
                : (long)image.Width * image.Height;
            int channels = ChannelCount;
            int perPixel = Math.Min(_bitsPerPixel, channels);

            // A cycle spreads one bit per enabled channel over ceil(channels / perPixel) pixels.
            long pixelsPerCycle = (channels + perPixel - 1) / perPixel;
            long fullCycles = pixels / pixelsPerCycle;
            long leftoverPixels = pixels % pixelsPerCycle;
            return fullCycles * channels + Math.Min(leftoverPixels * perPixel, channels);
        }

        // Bits below this position may change; a content-aware selector reads the ones above it.
        private int StableShift()
        {
            if (_selector is not IContentAwarePixelSelector aware)
                return 8;

            if (aware.StableHighBits < 1 || aware.StableHighBits > 7)
                throw new InvalidOperationException("StableHighBits must be between 1 and 7.");

            return 8 - aware.StableHighBits;
        }

        private IEnumerable<Point> PixelsOf(Image<Rgba32> image)
        {
            return _selector is IContentAwarePixelSelector aware
                ? aware.Pixels(image)
                : _selector.Pixels(image.Width, image.Height);
        }

        // One slot per bit: a pixel and the channel index (0 = R, 1 = G, 2 = B).
        // Channels rotate across pixels so a cycle of C bits is spread over ceil(C / M) pixels.
        private IEnumerable<(int X, int Y, int Channel)> Slots(Image<Rgba32> image)
        {
            var channels = new List<int>(3);
            if (_channels.HasFlag(ColorChannels.Red)) channels.Add(0);
            if (_channels.HasFlag(ColorChannels.Green)) channels.Add(1);
            if (_channels.HasFlag(ColorChannels.Blue)) channels.Add(2);

            int perPixel = Math.Min(_bitsPerPixel, channels.Count);
            int next = 0;

            foreach (var p in PixelsOf(image))
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

        private void WriteBit(ref Rgba32 pixel, int channel, bool bit, bool up, int stableShift)
        {
            switch (channel)
            {
                case 0: pixel.R = Adjust(pixel.R, bit, up, stableShift); break;
                case 1: pixel.G = Adjust(pixel.G, bit, up, stableShift); break;
                default: pixel.B = Adjust(pixel.B, bit, up, stableShift); break;
            }
        }

        // Moves by one in the requested direction unless that would leave the range,
        // or change the high bits a content-aware selector depends on.
        private byte Adjust(byte value, bool bit, bool up, int stableShift)
        {
            if (((value & 1) == 1) == bit)
                return value;

            if (EmbeddingMode == LsbEmbeddingMode.Replace)
                return (byte)(bit ? (value | 1) : (value & 0xFE));

            bool canUp = value < 255 && ((value + 1) >> stableShift) == (value >> stableShift);
            bool canDown = value > 0 && ((value - 1) >> stableShift) == (value >> stableShift);

            if (canUp && canDown)
                return (byte)(up ? value + 1 : value - 1);
            return (byte)(canUp ? value + 1 : value - 1);
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
