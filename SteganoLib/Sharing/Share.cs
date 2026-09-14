using System;

namespace SteganoLib.Sharing
{
    /// <summary>One share of a split secret: the evaluation point, the threshold it was made for, and the share bytes.</summary>
    public sealed class Share
    {
        public const int HeaderSize = 2;

        public Share(int threshold, int index, byte[] data)
        {
            if (threshold < 1 || threshold > 255) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (index < 1 || index > 255) throw new ArgumentOutOfRangeException(nameof(index), "Share index must be 1 to 255.");

            Threshold = threshold;
            Index = index;
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>Shares needed to recover the secret.</summary>
        public int Threshold { get; }

        /// <summary>Evaluation point, 1 to 255; distinct for every share of one split.</summary>
        public int Index { get; }

        /// <summary>Same length as the secret.</summary>
        public byte[] Data { get; }

        /// <summary>Threshold, index, then the share bytes.</summary>
        public byte[] ToBytes()
        {
            var bytes = new byte[HeaderSize + Data.Length];
            bytes[0] = (byte)Threshold;
            bytes[1] = (byte)Index;
            Data.CopyTo(bytes, HeaderSize);
            return bytes;
        }

        /// <summary>Parse a share; null when the bytes cannot be one.</summary>
        public static Share TryParse(byte[] bytes)
        {
            if (bytes == null || bytes.Length < HeaderSize || bytes[0] < 1 || bytes[1] < 1)
                return null;
            return new Share(bytes[0], bytes[1], bytes.AsSpan(HeaderSize).ToArray());
        }

        public static Share FromBytes(byte[] bytes)
        {
            return TryParse(bytes) ?? throw new ArgumentException("Not a share: too short or zero threshold or index.", nameof(bytes));
        }
    }
}
