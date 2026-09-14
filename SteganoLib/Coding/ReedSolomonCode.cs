using System;
using System.Collections.Generic;

using static SteganoLib.Coding.GaloisField256;

namespace SteganoLib.Coding
{
    /// <summary>
    /// Systematic Reed-Solomon code over GF(2^8): every block of up to
    /// <see cref="DataSize"/> bytes gets <see cref="ParitySymbols"/> parity bytes and any
    /// <see cref="MaxCorrectableErrors"/> corrupted bytes in the block are repaired.
    /// A stream is cut into full blocks with a shortened last block, and by default the
    /// bytes are interleaved across blocks so that a burst of damage is shared out among
    /// them instead of overwhelming one. Decoding uses Berlekamp-Massey, a Chien search
    /// and Forney's formula.
    /// </summary>
    public sealed class ReedSolomonCode : IErrorCorrectionCode
    {
        public const int BlockSize = 255;

        private readonly byte[] _generator; // lowest degree first

        /// <param name="paritySymbols">Parity bytes per block, 2 to 254; corrects half as many errors.</param>
        public ReedSolomonCode(int paritySymbols = 32)
        {
            if (paritySymbols < 2 || paritySymbols > BlockSize - 1)
                throw new ArgumentOutOfRangeException(nameof(paritySymbols), "Parity symbols must be between 2 and 254.");

            ParitySymbols = paritySymbols;
            _generator = new byte[] { 1 };
            for (int i = 0; i < paritySymbols; i++)
                _generator = MultiplyPolynomials(_generator, new[] { Power(i), (byte)1 }); // (x - a^i)
        }

        public int ParitySymbols { get; }

        /// <summary>Payload bytes per full block.</summary>
        public int DataSize => BlockSize - ParitySymbols;

        public int MaxCorrectableErrors => ParitySymbols / 2;

        /// <summary>Spread each block's bytes across the stream so bursts hit several blocks lightly. Default on.</summary>
        public bool Interleave { get; set; } = true;

        public long EncodedLength(long dataLength)
        {
            if (dataLength < 0) throw new ArgumentOutOfRangeException(nameof(dataLength));
            long blocks = (dataLength + DataSize - 1) / DataSize;
            return dataLength + blocks * ParitySymbols;
        }

        public long MaxDataLength(long encodedCapacity)
        {
            if (encodedCapacity < 0)
                return 0;
            long fullBlocks = encodedCapacity / BlockSize;
            long rest = encodedCapacity % BlockSize;
            return fullBlocks * DataSize + Math.Max(0, rest - ParitySymbols);
        }

        public byte[] Encode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var encoded = new byte[EncodedLength(data.Length)];
            int read = 0, written = 0;
            while (read < data.Length)
            {
                int length = Math.Min(DataSize, data.Length - read);
                EncodeBlock(data.AsSpan(read, length), encoded.AsSpan(written, length + ParitySymbols));
                read += length;
                written += length + ParitySymbols;
            }
            return Interleave ? Interleaver.Forward(encoded, BlockSize) : encoded;
        }

        public bool TryDecode(byte[] encoded, out byte[] data, out int correctedSymbols)
        {
            if (encoded == null) throw new ArgumentNullException(nameof(encoded));

            data = null;
            correctedSymbols = 0;
            if (encoded.Length == 0)
            {
                data = Array.Empty<byte>();
                return true;
            }

            int lastBlock = encoded.Length % BlockSize == 0 ? BlockSize : encoded.Length % BlockSize;
            if (lastBlock <= ParitySymbols)
                return false;

            var stream = Interleave ? Interleaver.Inverse(encoded, BlockSize) : (byte[])encoded.Clone();
            var result = new byte[encoded.Length - (encoded.Length + BlockSize - 1) / BlockSize * ParitySymbols];
            int read = 0, written = 0;
            while (read < stream.Length)
            {
                int length = Math.Min(BlockSize, stream.Length - read);
                var block = stream.AsSpan(read, length);
                if (!TryDecodeBlock(block, out int corrected))
                    return false;
                correctedSymbols += corrected;
                block.Slice(0, length - ParitySymbols).CopyTo(result.AsSpan(written));
                read += length;
                written += length - ParitySymbols;
            }
            data = result;
            return true;
        }

        /// <summary>Write <paramref name="data"/> followed by its parity into <paramref name="block"/>.</summary>
        public void EncodeBlock(ReadOnlySpan<byte> data, Span<byte> block)
        {
            if (data.Length < 1 || data.Length > DataSize)
                throw new ArgumentException($"A block holds 1 to {DataSize} data bytes.", nameof(data));
            if (block.Length != data.Length + ParitySymbols)
                throw new ArgumentException("Block must be data length plus parity.", nameof(block));

            data.CopyTo(block);
            var parity = block.Slice(data.Length);
            parity.Clear();

            // Polynomial long division of data * x^parity by the monic generator; the remainder is the parity.
            int top = ParitySymbols;
            for (int i = 0; i < data.Length; i++)
            {
                byte feedback = block[i]; // current leading coefficient of the running remainder
                if (feedback == 0)
                    continue;
                for (int j = 1; j <= top; j++)
                    block[i + j] ^= Multiply(_generator[top - j], feedback);
            }
            data.CopyTo(block); // the division scribbled over the data bytes; put them back
        }

        /// <summary>Repair <paramref name="block"/> in place. False when there are more errors than parity allows.</summary>
        public bool TryDecodeBlock(Span<byte> block, out int correctedErrors)
        {
            correctedErrors = 0;
            if (block.Length <= ParitySymbols || block.Length > BlockSize)
                throw new ArgumentException("Block length must exceed the parity count and not exceed 255.", nameof(block));

            var syndromes = Syndromes(block);
            if (AllZero(syndromes))
                return true;

            var locator = BerlekampMassey(syndromes, out int errors);
            if (locator == null)
                return false;

            var positions = ChienSearch(locator, block.Length);
            if (positions.Count != errors)
                return false;

            var evaluator = ErrorEvaluator(syndromes, locator);
            var derivative = FormalDerivative(locator);
            foreach (int index in positions)
            {
                int degree = block.Length - 1 - index;
                byte x = Power(degree);
                byte xInverse = Power(-degree);
                byte denominator = Evaluate(derivative, xInverse);
                if (denominator == 0)
                    return false;
                byte magnitude = Divide(Multiply(x, Evaluate(evaluator, xInverse)), denominator);
                block[index] ^= magnitude;
            }

            if (!AllZero(Syndromes(block)))
                return false;

            correctedErrors = positions.Count;
            return true;
        }

        private byte[] Syndromes(ReadOnlySpan<byte> block)
        {
            var syndromes = new byte[ParitySymbols];
            for (int i = 0; i < ParitySymbols; i++)
            {
                byte alpha = Power(i);
                byte value = 0;
                for (int j = 0; j < block.Length; j++)
                    value = (byte)(Multiply(value, alpha) ^ block[j]);
                syndromes[i] = value;
            }
            return syndromes;
        }

        /// <summary>Error locator polynomial, lowest degree first, or null when the error count exceeds the bound.</summary>
        private byte[] BerlekampMassey(byte[] syndromes, out int errors)
        {
            var lambda = new List<byte> { 1 };
            var previous = new List<byte> { 1 };
            int length = 0, shift = 1;
            byte previousDiscrepancy = 1;

            for (int n = 0; n < syndromes.Length; n++)
            {
                byte discrepancy = syndromes[n];
                for (int i = 1; i <= length && i < lambda.Count; i++)
                    discrepancy ^= Multiply(lambda[i], syndromes[n - i]);

                if (discrepancy == 0)
                {
                    shift++;
                    continue;
                }

                var candidate = new List<byte>(lambda);
                byte scale = Divide(discrepancy, previousDiscrepancy);
                while (candidate.Count < previous.Count + shift)
                    candidate.Add(0);
                for (int i = 0; i < previous.Count; i++)
                    candidate[i + shift] ^= Multiply(scale, previous[i]);

                if (2 * length <= n)
                {
                    previous = lambda;
                    length = n + 1 - length;
                    previousDiscrepancy = discrepancy;
                    shift = 1;
                }
                else
                {
                    shift++;
                }
                lambda = candidate;
            }

            while (lambda.Count > 1 && lambda[^1] == 0)
                lambda.RemoveAt(lambda.Count - 1);

            errors = length;
            if (lambda.Count - 1 != length || length > MaxCorrectableErrors)
                return null;
            return lambda.ToArray();
        }

        /// <summary>Indices in the block whose locator inverse is a root of lambda.</summary>
        private static List<int> ChienSearch(byte[] locator, int blockLength)
        {
            var positions = new List<int>();
            for (int index = 0; index < blockLength; index++)
            {
                int degree = blockLength - 1 - index;
                if (Evaluate(locator, Power(-degree)) == 0)
                    positions.Add(index);
            }
            return positions;
        }

        /// <summary>Omega(x) = S(x) Lambda(x) mod x^parity.</summary>
        private byte[] ErrorEvaluator(byte[] syndromes, byte[] locator)
        {
            var product = MultiplyPolynomials(syndromes, locator);
            var omega = new byte[ParitySymbols];
            Array.Copy(product, omega, Math.Min(product.Length, ParitySymbols));
            return omega;
        }

        private static byte[] FormalDerivative(byte[] poly)
        {
            var derivative = new byte[Math.Max(1, poly.Length - 1)];
            for (int i = 1; i < poly.Length; i += 2)
                derivative[i - 1] = poly[i]; // even multiples vanish in characteristic 2
            return derivative;
        }

        private static bool AllZero(byte[] values)
        {
            foreach (byte v in values)
            {
                if (v != 0)
                    return false;
            }
            return true;
        }

        /// <summary>Block interleaver: rows are blocks of <c>blockSize</c> (last may be short), output is read column by column.</summary>
        internal static class Interleaver
        {
            public static byte[] Forward(byte[] stream, int blockSize)
            {
                var output = new byte[stream.Length];
                int k = 0;
                foreach (int index in Order(stream.Length, blockSize))
                    output[k++] = stream[index];
                return output;
            }

            public static byte[] Inverse(byte[] stream, int blockSize)
            {
                var output = new byte[stream.Length];
                int k = 0;
                foreach (int index in Order(stream.Length, blockSize))
                    output[index] = stream[k++];
                return output;
            }

            private static IEnumerable<int> Order(int length, int blockSize)
            {
                int rows = (length + blockSize - 1) / blockSize;
                for (int column = 0; column < blockSize; column++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        int index = row * blockSize + column;
                        if (index < length)
                            yield return index;
                    }
                }
            }
        }
    }
}
