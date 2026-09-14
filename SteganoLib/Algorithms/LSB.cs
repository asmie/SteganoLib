using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

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

            var carrier = new Carrier(this, image);
            SlotEmbedding.Embed(carrier, data, TrellisCoder, _maxTrellisWidth);
            carrier.Commit();
        }

        /// <inheritdoc />
        /// <returns>The payload, or an empty array if the header is not plausible for this image.</returns>
        public byte[] ExtractBytes(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            return SlotEmbedding.Extract(new Carrier(this, image));
        }

        /// <inheritdoc />
        public long Capacity(Image<Rgba32> image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            return SlotEmbedding.Capacity(TotalSlots(image));
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

        private sealed class Carrier : SlotCarrier<(int X, int Y, int Channel)>
        {
            private readonly LSB _owner;
            private readonly Image<Rgba32> _image;
            private readonly int _stableShift;
            private readonly int _width;

            // The hot path: every slot read and write goes to this flat RGBA copy of the
            // image (channel index = byte offset within a pixel), and the rows are written
            // back once in Commit. Going through the image indexer twice per slot cost
            // more than the embedding itself.
            private byte[] _pixels;
            private bool _dirty;

            public Carrier(LSB owner, Image<Rgba32> image)
            {
                _owner = owner;
                _image = image;
                _stableShift = owner.StableShift();
                _width = image.Width;
            }

            public override long TotalSlots() => _owner.TotalSlots(_image);

            public override IEnumerable<(int X, int Y, int Channel)> Slots() => _owner.Slots(_image);

            public override bool Read((int X, int Y, int Channel) slot)
            {
                return (Pixels()[Offset(slot)] & 1) == 1;
            }

            public override void Write((int X, int Y, int Channel) slot, bool bit, bool up)
            {
                var pixels = Pixels();
                int offset = Offset(slot);
                byte adjusted = _owner.Adjust(pixels[offset], bit, up, _stableShift);
                if (adjusted == pixels[offset])
                    return;
                pixels[offset] = adjusted;
                _dirty = true;
            }

            /// <summary>Write changed pixels back to the image.</summary>
            public void Commit()
            {
                if (!_dirty)
                    return;

                var pixels = _pixels;
                int rowBytes = _width * 4;
                _image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                        MemoryMarshal.Cast<byte, Rgba32>(pixels.AsSpan(y * rowBytes, rowBytes)).CopyTo(accessor.GetRowSpan(y));
                });
                _dirty = false;
            }

            private byte[] Pixels()
            {
                if (_pixels == null)
                {
                    _pixels = new byte[_width * _image.Height * 4];
                    _image.CopyPixelDataTo(_pixels);
                }
                return _pixels;
            }

            private int Offset((int X, int Y, int Channel) slot) => (slot.Y * _width + slot.X) * 4 + slot.Channel;

            public override double Cost((int X, int Y, int Channel) slot) => _owner._costModel.Cost(_image, slot.X, slot.Y, slot.Channel);
        }
    }
}
