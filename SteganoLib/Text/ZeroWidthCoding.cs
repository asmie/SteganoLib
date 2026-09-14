using System;
using System.Collections.Generic;
using System.Text;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;

namespace SteganoLib.Text
{
    /// <summary>
    /// Hides bits as invisible characters at the end of words: U+200B (zero width
    /// space) for 0 and U+200C (zero width non-joiner) for 1, <see cref="BitsPerGap"/>
    /// of them before each whitespace run, in keyed gap order. The visible text does
    /// not change. Any zero-width characters already present are removed first.
    /// <para>
    /// Destroyed by anything that strips or normalises invisible characters: many
    /// chat clients, form sanitisers and copy-paste into plain-text editors.
    /// </para>
    /// </summary>
    public sealed class ZeroWidthCoding : IStegAlgorithm<TextCarrier>
    {
        public const char Zero = '​';
        public const char One = '‌';

        private const string Purpose = "SteganoLib/zero-width-gap-permutation/v1";

        private readonly byte[] _keyMaterial;
        private int _bitsPerGap = 8;

        public ZeroWidthCoding(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        /// <summary>Invisible characters inserted per word gap, 1 to 64. Default 8.</summary>
        public int BitsPerGap
        {
            get => _bitsPerGap;
            set
            {
                if (value < 1 || value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be between 1 and 64.");
                _bitsPerGap = value;
            }
        }

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, TextCarrier carrier)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            string text = Strip(carrier.Text);
            var gaps = GapPositions(text);
            var groups = GapCoding.Distribute(data, gaps.Count, _bitsPerGap, _keyMaterial);

            var builder = new StringBuilder(text.Length + data.Length * 8 + 64);
            int last = 0;
            for (int g = 0; g < gaps.Count; g++)
            {
                builder.Append(text, last, gaps[g] - last);
                if (groups[g] != null)
                {
                    foreach (bool bit in groups[g])
                        builder.Append(bit ? One : Zero);
                }
                last = gaps[g];
            }
            builder.Append(text, last, text.Length - last);
            carrier.Text = builder.ToString();
        }

        /// <inheritdoc />
        public byte[] ExtractBytes(TextCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return GapCoding.Collect(GapBits(carrier.Text), _bitsPerGap, _keyMaterial);
        }

        /// <inheritdoc />
        public long Capacity(TextCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return GapCoding.Capacity(GapPositions(Strip(carrier.Text)).Count, _bitsPerGap);
        }

        /// <summary>Remove the zero-width characters this method uses.</summary>
        public static string Strip(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            return text.Replace(Zero.ToString(), string.Empty).Replace(One.ToString(), string.Empty);
        }

        // Index of the first whitespace character of every run that follows a word.
        private static List<int> GapPositions(string text)
        {
            var gaps = new List<int>();
            bool inWord = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == Zero || c == One)
                    continue;

                if (char.IsWhiteSpace(c))
                {
                    if (inWord)
                        gaps.Add(i);
                    inWord = false;
                }
                else
                {
                    inWord = true;
                }
            }
            return gaps;
        }

        private static bool[][] GapBits(string text)
        {
            var gaps = new List<bool[]>();
            var pending = new List<bool>();
            bool inWord = false;
            foreach (char c in text)
            {
                if (c == Zero || c == One)
                {
                    pending.Add(c == One);
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (inWord)
                        gaps.Add(pending.ToArray());
                    pending.Clear();
                    inWord = false;
                }
                else
                {
                    pending.Clear();
                    inWord = true;
                }
            }
            return gaps.ToArray();
        }
    }
}
