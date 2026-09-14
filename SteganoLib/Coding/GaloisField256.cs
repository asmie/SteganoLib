using System;

namespace SteganoLib.Coding
{
    /// <summary>GF(2^8) with the primitive polynomial x^8 + x^4 + x^3 + x^2 + 1 (0x11D) and generator 2.</summary>
    internal static class GaloisField256
    {
        private const int Primitive = 0x11D;

        private static readonly byte[] Exp = new byte[512];
        private static readonly byte[] Log = new byte[256];

        static GaloisField256()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;
                x <<= 1;
                if ((x & 0x100) != 0)
                    x ^= Primitive;
            }
            for (int i = 255; i < 512; i++)
                Exp[i] = Exp[i - 255];
        }

        public static byte Multiply(byte a, byte b)
        {
            if (a == 0 || b == 0)
                return 0;
            return Exp[Log[a] + Log[b]];
        }

        public static byte Divide(byte a, byte b)
        {
            if (b == 0)
                throw new DivideByZeroException();
            if (a == 0)
                return 0;
            return Exp[Log[a] + 255 - Log[b]];
        }

        public static byte Inverse(byte a)
        {
            if (a == 0)
                throw new DivideByZeroException();
            return Exp[255 - Log[a]];
        }

        /// <summary>2 raised to <paramref name="power"/>, any integer power.</summary>
        public static byte Power(int power)
        {
            int p = power % 255;
            if (p < 0)
                p += 255;
            return Exp[p];
        }

        /// <summary>Discrete logarithm base 2 of a non-zero element.</summary>
        public static int LogOf(byte a)
        {
            if (a == 0)
                throw new ArgumentOutOfRangeException(nameof(a));
            return Log[a];
        }

        /// <summary>Evaluate a polynomial stored lowest degree first.</summary>
        public static byte Evaluate(ReadOnlySpan<byte> poly, byte x)
        {
            byte result = 0;
            for (int i = poly.Length - 1; i >= 0; i--)
                result = (byte)(Multiply(result, x) ^ poly[i]);
            return result;
        }

        /// <summary>Product of two polynomials stored lowest degree first.</summary>
        public static byte[] MultiplyPolynomials(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            var result = new byte[a.Length + b.Length - 1];
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == 0)
                    continue;
                for (int j = 0; j < b.Length; j++)
                    result[i + j] ^= Multiply(a[i], b[j]);
            }
            return result;
        }
    }
}
