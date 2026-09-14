using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using SteganoLib.Coding;

namespace SteganoLib.Sharing
{
    /// <summary>
    /// Shamir's threshold scheme over GF(2^8), applied byte by byte: every secret byte is
    /// the constant term of a random polynomial of degree threshold - 1, and share i holds
    /// the polynomial's values at x = i. Any <see cref="Threshold"/> shares recover the
    /// secret by Lagrange interpolation at zero; fewer reveal nothing about it.
    /// </summary>
    public sealed class ShamirSecretSharing
    {
        public ShamirSecretSharing(int threshold, int shareCount)
        {
            if (shareCount < 1 || shareCount > 255)
                throw new ArgumentOutOfRangeException(nameof(shareCount), "Share count must be 1 to 255.");
            if (threshold < 1 || threshold > shareCount)
                throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be 1 to the share count.");

            Threshold = threshold;
            ShareCount = shareCount;
        }

        public int Threshold { get; }

        public int ShareCount { get; }

        /// <summary>Split <paramref name="secret"/> into <see cref="ShareCount"/> shares with indices 1 to <see cref="ShareCount"/>.</summary>
        public IReadOnlyList<Share> Split(byte[] secret)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));

            var shares = new byte[ShareCount][];
            for (int i = 0; i < ShareCount; i++)
                shares[i] = new byte[secret.Length];

            var coefficients = new byte[Threshold];
            for (int b = 0; b < secret.Length; b++)
            {
                coefficients[0] = secret[b];
                if (Threshold > 1)
                    RandomNumberGenerator.Fill(coefficients.AsSpan(1));

                for (int i = 0; i < ShareCount; i++)
                    shares[i][b] = GaloisField256.Evaluate(coefficients, (byte)(i + 1));
            }

            var result = new List<Share>(ShareCount);
            for (int i = 0; i < ShareCount; i++)
                result.Add(new Share(Threshold, i + 1, shares[i]));
            return result;
        }

        /// <summary>
        /// Recover the secret from at least the threshold number of shares of one split.
        /// Extra shares are used too; a share from a different split or a corrupted share
        /// produces wrong bytes without warning, so authenticate the secret separately.
        /// </summary>
        /// <exception cref="ArgumentException">Too few shares, or shares that do not belong together.</exception>
        public static byte[] Combine(IReadOnlyList<Share> shares)
        {
            if (shares == null) throw new ArgumentNullException(nameof(shares));
            if (shares.Count == 0) throw new ArgumentException("At least one share is required.", nameof(shares));

            int threshold = shares[0].Threshold;
            int length = shares[0].Data.Length;
            var seen = new HashSet<int>();
            foreach (var share in shares)
            {
                if (share == null)
                    throw new ArgumentException("Shares must not be null.", nameof(shares));
                if (share.Threshold != threshold)
                    throw new ArgumentException("Shares carry different thresholds.", nameof(shares));
                if (share.Data.Length != length)
                    throw new ArgumentException("Shares differ in length.", nameof(shares));
                if (!seen.Add(share.Index))
                    throw new ArgumentException($"Share index {share.Index} appears twice.", nameof(shares));
            }
            if (shares.Count < threshold)
                throw new ArgumentException($"{threshold} shares are needed, {shares.Count} given.", nameof(shares));

            // Lagrange basis at x = 0: l_i = prod_{j != i} x_j / (x_j - x_i); subtraction is xor here.
            var basis = new byte[shares.Count];
            for (int i = 0; i < shares.Count; i++)
            {
                byte numerator = 1, denominator = 1;
                byte xi = (byte)shares[i].Index;
                for (int j = 0; j < shares.Count; j++)
                {
                    if (j == i) continue;
                    byte xj = (byte)shares[j].Index;
                    numerator = GaloisField256.Multiply(numerator, xj);
                    denominator = GaloisField256.Multiply(denominator, (byte)(xj ^ xi));
                }
                basis[i] = GaloisField256.Divide(numerator, denominator);
            }

            var secret = new byte[length];
            for (int b = 0; b < length; b++)
            {
                byte value = 0;
                for (int i = 0; i < shares.Count; i++)
                    value ^= GaloisField256.Multiply(basis[i], shares[i].Data[b]);
                secret[b] = value;
            }
            return secret;
        }
    }
}
