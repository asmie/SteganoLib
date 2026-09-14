using System;
using System.Collections.Generic;
using System.Text;

using SteganoLib.Algorithms;
using SteganoLib.Crypto;

namespace SteganoLib.Text
{
    /// <summary>
    /// Hides bits as trailing whitespace: <see cref="BitsPerLine"/> characters, space
    /// for 0 and tab for 1, appended before each line break in keyed line order.
    /// Existing trailing whitespace is removed first. Suits source code and other
    /// plain text where trailing blanks go unnoticed.
    /// <para>
    /// Destroyed by editors and formatters that trim trailing whitespace, and by
    /// tab-to-space conversion.
    /// </para>
    /// </summary>
    public sealed class WhitespaceCoding : IStegAlgorithm<TextCarrier>
    {
        private const string Purpose = "SteganoLib/whitespace-line-permutation/v1";

        private readonly byte[] _keyMaterial;
        private int _bitsPerLine = 8;

        public WhitespaceCoding(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        /// <summary>Whitespace characters appended per line, 1 to 64. Default 8.</summary>
        public int BitsPerLine
        {
            get => _bitsPerLine;
            set
            {
                if (value < 1 || value > 64)
                    throw new ArgumentOutOfRangeException(nameof(value), "Must be between 1 and 64.");
                _bitsPerLine = value;
            }
        }

        /// <inheritdoc />
        public void EmbedBytes(byte[] data, TextCarrier carrier)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            var lines = SplitLines(carrier.Text);
            var groups = GapCoding.Distribute(data, lines.Count, _bitsPerLine, _keyMaterial);

            var builder = new StringBuilder(carrier.Text.Length + data.Length * 8 + 64);
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append(lines[i].Content.TrimEnd(' ', '\t'));
                if (groups[i] != null)
                {
                    foreach (bool bit in groups[i])
                        builder.Append(bit ? '\t' : ' ');
                }
                builder.Append(lines[i].Terminator);
            }
            carrier.Text = builder.ToString();
        }

        /// <inheritdoc />
        public byte[] ExtractBytes(TextCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            var lines = SplitLines(carrier.Text);
            var gapBits = new bool[lines.Count][];
            for (int i = 0; i < lines.Count; i++)
            {
                string content = lines[i].Content;
                int end = content.Length;
                while (end > 0 && (content[end - 1] == ' ' || content[end - 1] == '\t'))
                    end--;

                var bits = new bool[content.Length - end];
                for (int b = 0; b < bits.Length; b++)
                    bits[b] = content[end + b] == '\t';
                gapBits[i] = bits;
            }

            return GapCoding.Collect(gapBits, _bitsPerLine, _keyMaterial);
        }

        /// <inheritdoc />
        public long Capacity(TextCarrier carrier)
        {
            if (carrier == null)
                throw new ArgumentNullException(nameof(carrier));

            return GapCoding.Capacity(SplitLines(carrier.Text).Count, _bitsPerLine);
        }

        // Every line that ends with a line break is a slot; a final unterminated line is left alone
        // so a file's end-of-file convention is preserved.
        private static List<(string Content, string Terminator)> SplitLines(string text)
        {
            var lines = new List<(string, string)>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    bool crlf = i > start && text[i - 1] == '\r';
                    int contentEnd = crlf ? i - 1 : i;
                    lines.Add((text.Substring(start, contentEnd - start), crlf ? "\r\n" : "\n"));
                    start = i + 1;
                }
            }
            if (start < text.Length)
                lines.Add((text.Substring(start), string.Empty));
            return lines;
        }
    }
}
