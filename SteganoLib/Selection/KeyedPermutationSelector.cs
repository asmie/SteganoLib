using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SteganoLib.Crypto;

namespace SteganoLib.Selection
{
    /// <summary>
    /// Visits every pixel exactly once in an order derived from a <see cref="StegoKey"/>.
    /// Without the key the order is not recoverable; with it, generation is O(1) per pixel.
    /// </summary>
    public sealed class KeyedPermutationSelector : IPixelSelector
    {
        private const string Purpose = "SteganoLib/pixel-permutation/v1";

        private readonly byte[] _keyMaterial;

        public KeyedPermutationSelector(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        public IEnumerable<Point> Pixels(int width, int height)
        {
            return Enumerate(width, Count(width, height));
        }

        /// <summary>Every pixel is selected; no traversal is needed to count them.</summary>
        public long Count(int width, int height)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height));

            return (long)width * height;
        }

        private IEnumerable<Point> Enumerate(int width, long count)
        {
            var permutation = new FeistelPermutation(_keyMaterial, count);
            for (long i = 0; i < count; i++)
            {
                long index = permutation.Permute(i);
                long y = index / width; // one division; the remainder follows from it
                yield return new Point((int)(index - y * width), (int)y);
            }
        }
    }
}
