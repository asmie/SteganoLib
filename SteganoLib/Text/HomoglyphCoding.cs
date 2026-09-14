using System;
using System.Collections.Generic;

using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;

namespace SteganoLib.Text
{
    /// <summary>
    /// Hides bits by swapping Latin letters for Cyrillic letters that look the same
    /// (a, c, e, o, p, x, y and others). Each such letter is one slot: Latin is 0,
    /// Cyrillic is 1. Slots are visited in keyed order with the same 6-byte header as
    /// the LSB family, so an optional <see cref="TrellisCoder"/> keeps the number of
    /// swapped letters low. The text length never changes.
    /// <para>
    /// Destroyed by Unicode NFKC normalisation, spell checkers and transliteration.
    /// Swapped letters also show up in fonts that render Cyrillic differently.
    /// </para>
    /// </summary>
    public sealed class HomoglyphCoding : IStegAlgorithm<TextCarrier>
    {
        private const string Purpose = "SteganoLib/homoglyph-permutation/v1";

        // Latin letter -> Cyrillic twin. Only pairs that are pixel-identical in common fonts.
        private static readonly Dictionary<char, char> ToCyrillic = new()
        {
            ['a'] = 'а',
            ['c'] = 'с',
            ['e'] = 'е',
            ['i'] = 'і',
            ['j'] = 'ј',
            ['o'] = 'о',
            ['p'] = 'р',
            ['s'] = 'ѕ',
            ['x'] = 'х',
            ['y'] = 'у',
            ['A'] = 'А',
            ['B'] = 'В',
            ['C'] = 'С',
            ['E'] = 'Е',
            ['H'] = 'Н',
            ['I'] = 'І',
            ['J'] = 'Ј',
            ['K'] = 'К',
            ['M'] = 'М',
            ['O'] = 'О',
            ['P'] = 'Р',
            ['S'] = 'Ѕ',
            ['T'] = 'Т',
            ['X'] = 'Х',
        };

        private static readonly Dictionary<char, char> ToLatin = Invert(ToCyrillic);

        private readonly byte[] _keyMaterial;
        private int _maxTrellisWidth = 64;

        public HomoglyphCoding(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        /// <summary>Syndrome-trellis coder for the payload; <c>null</c> (default) writes bits directly.</summary>
        public SyndromeTrellisCoder TrellisCoder { get; set; }

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

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, TextCarrier carrier)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            var chars = carrier.Text.ToCharArray();
            SlotEmbedding.Embed(new Carrier(this, chars), data, TrellisCoder, _maxTrellisWidth);
            carrier.Text = new string(chars);
        }

        /// <inheritdoc />
        public byte[] ExtractBytes(TextCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return SlotEmbedding.Extract(new Carrier(this, carrier.Text.ToCharArray()));
        }

        /// <inheritdoc />
        public long Capacity(TextCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return SlotEmbedding.Capacity(SlotPositions(carrier.Text.ToCharArray()).Count);
        }

        /// <summary>Whether <paramref name="c"/> is a Latin letter with a Cyrillic twin, or such a twin.</summary>
        public static bool IsSlot(char c) => ToCyrillic.ContainsKey(c) || ToLatin.ContainsKey(c);

        /// <summary>Replace Cyrillic twins with their Latin letters.</summary>
        public static string Normalize(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (ToLatin.TryGetValue(chars[i], out var latin))
                    chars[i] = latin;
            }
            return new string(chars);
        }

        private static List<int> SlotPositions(char[] chars)
        {
            var positions = new List<int>();
            for (int i = 0; i < chars.Length; i++)
            {
                if (IsSlot(chars[i]))
                    positions.Add(i);
            }
            return positions;
        }

        private static Dictionary<char, char> Invert(Dictionary<char, char> map)
        {
            var inverse = new Dictionary<char, char>(map.Count);
            foreach (var pair in map)
                inverse[pair.Value] = pair.Key;
            return inverse;
        }

        private sealed class Carrier : SlotCarrier<int>
        {
            private readonly HomoglyphCoding _owner;
            private readonly char[] _chars;
            private readonly List<int> _positions;

            public Carrier(HomoglyphCoding owner, char[] chars)
            {
                _owner = owner;
                _chars = chars;
                _positions = SlotPositions(chars);
            }

            public override long TotalSlots() => _positions.Count;

            public override IEnumerable<int> Slots()
            {
                if (_positions.Count == 0)
                    yield break;

                var permutation = new FeistelPermutation(_owner._keyMaterial, _positions.Count);
                for (int i = 0; i < _positions.Count; i++)
                    yield return _positions[(int)permutation.Permute(i)];
            }

            public override bool Read(int slot) => ToLatin.ContainsKey(_chars[slot]);

            public override void Write(int slot, bool bit, bool up)
            {
                char c = _chars[slot];
                if (bit && ToCyrillic.TryGetValue(c, out var cyrillic))
                    _chars[slot] = cyrillic;
                else if (!bit && ToLatin.TryGetValue(c, out var latin))
                    _chars[slot] = latin;
            }

            public override double Cost(int slot) => 1.0;
        }
    }
}
