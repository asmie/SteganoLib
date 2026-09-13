using System;
using System.Numerics;

namespace SteganoLib.Audio
{
    /// <summary>In-place iterative radix-2 FFT for power-of-two lengths.</summary>
    internal static class Fft
    {
        public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

        public static void Forward(Span<Complex> data) => Transform(data, inverse: false);

        public static void Inverse(Span<Complex> data) => Transform(data, inverse: true);

        private static void Transform(Span<Complex> data, bool inverse)
        {
            int n = data.Length;
            if (!IsPowerOfTwo(n))
                throw new ArgumentException("Length must be a power of two.", nameof(data));

            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i < j)
                    (data[i], data[j]) = (data[j], data[i]);
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = 2 * Math.PI / length * (inverse ? 1 : -1);
                var step = new Complex(Math.Cos(angle), Math.Sin(angle));
                for (int start = 0; start < n; start += length)
                {
                    var w = Complex.One;
                    int half = length / 2;
                    for (int k = 0; k < half; k++)
                    {
                        var u = data[start + k];
                        var v = data[start + k + half] * w;
                        data[start + k] = u + v;
                        data[start + k + half] = u - v;
                        w *= step;
                    }
                }
            }

            if (inverse)
            {
                for (int i = 0; i < n; i++)
                    data[i] /= n;
            }
        }
    }
}
