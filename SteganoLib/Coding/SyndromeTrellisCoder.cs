using System;

namespace SteganoLib.Coding
{
    /// <summary>
    /// Syndrome-trellis codes (Filler, Judas, Fridrich 2011). Given cover bits, a cost
    /// for flipping each one and a message, <see cref="Embed"/> finds the stego bits
    /// with the same syndrome as the message and near-minimal total cost using a
    /// Viterbi search. Infinite cost marks an element that must not change.
    /// <see cref="Extract"/> is a plain parity-check product and needs no costs.
    /// <para>
    /// The parity-check matrix is built from an h x w submatrix placed along the
    /// diagonal; w = cover length / message length is the inverse relative payload.
    /// Time and memory grow with 2^h, so heights above 10 are slow on large covers.
    /// </para>
    /// </summary>
    public sealed class SyndromeTrellisCoder
    {
        public const int MinHeight = 2;
        public const int MaxHeight = 12;

        public SyndromeTrellisCoder(int constraintHeight = 8)
        {
            if (constraintHeight < MinHeight || constraintHeight > MaxHeight)
                throw new ArgumentOutOfRangeException(nameof(constraintHeight), $"Height must be between {MinHeight} and {MaxHeight}.");

            ConstraintHeight = constraintHeight;
        }

        /// <summary>Submatrix height h. The trellis has 2^h states.</summary>
        public int ConstraintHeight { get; }

        /// <summary>
        /// Find stego bits carrying <paramref name="message"/>. Cover length must be a
        /// multiple of message length. Returns the total cost, or infinity when every
        /// solution needs a change to an element with infinite cost.
        /// </summary>
        public double Embed(ReadOnlySpan<bool> cover, ReadOnlySpan<double> costs, ReadOnlySpan<bool> message, Span<bool> stego)
        {
            int n = cover.Length;
            int m = message.Length;
            if (m == 0)
            {
                cover.CopyTo(stego);
                return 0;
            }
            if (n < m || n % m != 0)
                throw new ArgumentException("Cover length must be a positive multiple of the message length.", nameof(cover));
            if (costs.Length != n)
                throw new ArgumentException("One cost per cover bit is required.", nameof(costs));
            if (stego.Length != n)
                throw new ArgumentException("Stego buffer must match the cover length.", nameof(stego));
            for (int i = 0; i < n; i++)
            {
                if (costs[i] < 0 || double.IsNaN(costs[i]))
                    throw new ArgumentException("Costs must be non-negative.", nameof(costs));
            }

            int w = n / m;
            int h = ConstraintHeight;
            int states = 1 << h;
            var columns = SubmatrixColumns(h, w);

            var weight = new double[states];
            var next = new double[states];
            Array.Fill(weight, double.PositiveInfinity);
            weight[0] = 0;

            var path = new PathBits((long)n * states);

            int index = 0;
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < w; j++)
                {
                    int column = columns[j];
                    bool x = cover[index];
                    double rho = costs[index];
                    double costIfZero = x ? rho : 0;
                    double costIfOne = x ? 0 : rho;
                    long pathBase = (long)index * states;

                    for (int k = 0; k < states; k++)
                    {
                        double w0 = weight[k] + costIfZero;
                        double w1 = weight[k ^ column] + costIfOne;
                        if (w1 < w0)
                        {
                            next[k] = w1;
                            path.Set(pathBase + k);
                        }
                        else
                        {
                            next[k] = w0;
                        }
                    }

                    (weight, next) = (next, weight);
                    index++;
                }

                // Keep the states whose pending syndrome bit equals the message bit, then shift.
                int bit = message[i] ? 1 : 0;
                int half = states >> 1;
                for (int k = 0; k < half; k++)
                    next[k] = weight[2 * k + bit];
                for (int k = half; k < states; k++)
                    next[k] = double.PositiveInfinity;
                (weight, next) = (next, weight);
            }

            int state = 0;
            double best = double.PositiveInfinity;
            for (int k = 0; k < states; k++)
            {
                if (weight[k] < best)
                {
                    best = weight[k];
                    state = k;
                }
            }

            if (double.IsPositiveInfinity(best))
                return best;

            index = n - 1;
            for (int i = m - 1; i >= 0; i--)
            {
                state = 2 * state + (message[i] ? 1 : 0);
                for (int j = w - 1; j >= 0; j--)
                {
                    bool y = path.Get((long)index * states + state);
                    stego[index] = y;
                    if (y)
                        state ^= columns[j];
                    index--;
                }
            }

            return best;
        }

        /// <summary>Recover <paramref name="message"/> from <paramref name="stego"/>; stego length must be a multiple of the message length.</summary>
        public void Extract(ReadOnlySpan<bool> stego, Span<bool> message)
        {
            int n = stego.Length;
            int m = message.Length;
            if (m == 0)
                return;
            if (n < m || n % m != 0)
                throw new ArgumentException("Stego length must be a positive multiple of the message length.", nameof(stego));

            int w = n / m;
            var columns = SubmatrixColumns(ConstraintHeight, w);

            int state = 0;
            int index = 0;
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < w; j++)
                {
                    if (stego[index])
                        state ^= columns[j];
                    index++;
                }
                message[i] = (state & 1) == 1;
                state >>= 1;
            }
        }

        /// <summary>
        /// Columns of the h x w submatrix as h-bit integers (row r is bit r). First and
        /// last rows are all ones, which the authors found gives the best codes; the
        /// rest is a fixed pseudo-random pattern so both sides derive the same matrix.
        /// </summary>
        public static int[] SubmatrixColumns(int height, int width)
        {
            if (height < MinHeight || height > MaxHeight)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));

            var columns = new int[width];
            ulong state = 0x9E3779B97F4A7C15UL ^ ((ulong)height << 32) ^ (ulong)width;
            int mask = (1 << height) - 1;
            int fixedRows = 1 | (1 << (height - 1));

            for (int j = 0; j < width; j++)
            {
                state = SplitMix(ref state);
                columns[j] = ((int)(state & (ulong)mask) | fixedRows) & mask;
            }
            return columns;
        }

        private static ulong SplitMix(ref ulong seed)
        {
            seed += 0x9E3779B97F4A7C15UL;
            ulong z = seed;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private sealed class PathBits
        {
            private readonly ulong[] _bits;

            public PathBits(long count)
            {
                _bits = new ulong[(count + 63) / 64];
            }

            public void Set(long index) => _bits[index >> 6] |= 1UL << (int)(index & 63);

            public bool Get(long index) => (_bits[index >> 6] & (1UL << (int)(index & 63))) != 0;
        }
    }
}
