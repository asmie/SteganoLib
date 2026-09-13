using System;
using System.Collections.Generic;

using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Selection;

namespace SteganoLib.Algorithms
{
    /// <summary>
    /// LSB steganography for PCM audio. Samples are visited in the order given by an
    /// <see cref="ISampleSelector"/>; each carries <see cref="BitsPerSample"/> low bits.
    /// With one bit per sample a mismatching sample moves by plus or minus one at random
    /// (matching); with more bits the low bits are replaced. The same 6-byte header and
    /// optional <see cref="TrellisCoder"/> as the image <see cref="LSB"/> apply.
    /// </summary>
    public sealed class AudioLsb : IStegAlgorithm<PcmAudio>
    {
        private readonly ISampleSelector _selector;
        private int _bitsPerSample = 1;
        private int _maxTrellisWidth = 64;
        private ISampleCostModel _costModel = new AmplitudeCostModel();

        public AudioLsb(ISampleSelector selector)
        {
            _selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, PcmAudio audio)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));

            SlotEmbedding.Embed(new Carrier(this, audio), data, TrellisCoder, _maxTrellisWidth);
        }

        /// <inheritdoc />
        public byte[] ExtractBytes(PcmAudio audio)
        {
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));

            return SlotEmbedding.Extract(new Carrier(this, audio));
        }

        /// <inheritdoc />
        public long Capacity(PcmAudio audio)
        {
            if (audio == null)
                throw new ArgumentNullException(nameof(audio));

            return SlotEmbedding.Capacity((long)audio.Samples.Length * _bitsPerSample);
        }

        /// <summary>Low bits used in each sample, 1 to 4. Default 1.</summary>
        public int BitsPerSample
        {
            get => _bitsPerSample;
            set
            {
                if (value < 1 || value > 4)
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be between 1 and 4.");
                _bitsPerSample = value;
            }
        }

        public ISampleSelector SampleSelector => _selector;

        /// <summary>Syndrome-trellis coder for the payload; <c>null</c> (default) writes bits directly.</summary>
        public SyndromeTrellisCoder TrellisCoder { get; set; }

        /// <summary>Cost of changing a sample, consulted only when <see cref="TrellisCoder"/> is set. Default <see cref="AmplitudeCostModel"/>.</summary>
        public ISampleCostModel CostModel
        {
            get => _costModel;
            set => _costModel = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Upper bound on cover bits per message bit for the trellis code (1 to 255). Default 64.</summary>
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

        private sealed class Carrier : SlotCarrier<(long Index, int Bit)>
        {
            private readonly AudioLsb _owner;
            private readonly PcmAudio _audio;

            public Carrier(AudioLsb owner, PcmAudio audio)
            {
                _owner = owner;
                _audio = audio;
            }

            public override long TotalSlots() => (long)_audio.Samples.Length * _owner._bitsPerSample;

            public override IEnumerable<(long Index, int Bit)> Slots()
            {
                int bits = _owner._bitsPerSample;
                foreach (long index in _owner._selector.Indices(_audio.Samples.Length))
                {
                    for (int b = 0; b < bits; b++)
                        yield return (index, b);
                }
            }

            public override bool Read((long Index, int Bit) slot) => ((_audio.Samples[slot.Index] >> slot.Bit) & 1) == 1;

            public override void Write((long Index, int Bit) slot, bool bit, bool up)
            {
                int value = _audio.Samples[slot.Index];
                if (((value >> slot.Bit) & 1) == (bit ? 1 : 0))
                    return;

                if (_owner._bitsPerSample == 1)
                {
                    bool canUp = value < _audio.MaxValue;
                    bool canDown = value > _audio.MinValue;
                    if (canUp && canDown)
                        value += up ? 1 : -1;
                    else
                        value += canUp ? 1 : -1;
                }
                else
                {
                    value ^= 1 << slot.Bit;
                }

                _audio.Samples[slot.Index] = value;
            }

            public override double Cost((long Index, int Bit) slot) => _owner._costModel.Cost(_audio, slot.Index);
        }
    }
}
