using System;

namespace SteganoLib.Jpeg
{
    /// <summary>
    /// One colour component with its quantised DCT coefficients. Blocks are stored
    /// row-major on the padded MCU grid; each block holds 64 coefficients in zigzag
    /// order, index 0 being the DC term.
    /// </summary>
    public sealed class JpegComponent
    {
        public const int BlockSize = 64;

        internal JpegComponent(byte id, int horizontalSampling, int verticalSampling, int quantizationTableId, int blocksPerLine, int blocksPerColumn)
        {
            Id = id;
            HorizontalSampling = horizontalSampling;
            VerticalSampling = verticalSampling;
            QuantizationTableId = quantizationTableId;
            BlocksPerLine = blocksPerLine;
            BlocksPerColumn = blocksPerColumn;
            Coefficients = new short[checked(blocksPerLine * blocksPerColumn * BlockSize)];
        }

        private JpegComponent(JpegComponent other)
        {
            Id = other.Id;
            HorizontalSampling = other.HorizontalSampling;
            VerticalSampling = other.VerticalSampling;
            QuantizationTableId = other.QuantizationTableId;
            BlocksPerLine = other.BlocksPerLine;
            BlocksPerColumn = other.BlocksPerColumn;
            Coefficients = (short[])other.Coefficients.Clone();
        }

        public byte Id { get; }

        public int HorizontalSampling { get; }

        public int VerticalSampling { get; }

        public int QuantizationTableId { get; }

        /// <summary>Blocks per row including padding up to the MCU boundary.</summary>
        public int BlocksPerLine { get; }

        /// <summary>Block rows including padding up to the MCU boundary.</summary>
        public int BlocksPerColumn { get; }

        public int BlockCount => BlocksPerLine * BlocksPerColumn;

        /// <summary>All coefficients, <see cref="BlockCount"/> blocks of 64 in zigzag order.</summary>
        public short[] Coefficients { get; }

        public Span<short> Block(int row, int column)
        {
            if (row < 0 || row >= BlocksPerColumn)
                throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= BlocksPerLine)
                throw new ArgumentOutOfRangeException(nameof(column));

            return Coefficients.AsSpan((row * BlocksPerLine + column) * BlockSize, BlockSize);
        }

        public Span<short> Block(int index)
        {
            if (index < 0 || index >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            return Coefficients.AsSpan(index * BlockSize, BlockSize);
        }

        internal JpegComponent Clone() => new(this);
    }
}
