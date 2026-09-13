using System;
using System.Collections.Generic;

using SixLabors.ImageSharp;
using SteganoLib.Crypto;

namespace SteganoLib.Selection
{
    /// <summary>
    /// Legacy selector: draws columns and rows from two seeded <see cref="PRNG"/>
    /// instances and skips repeats. Kept so images produced by earlier versions can
    /// still be read. The 32-bit seeds are trivial to brute force, so prefer
    /// <see cref="KeyedPermutationSelector"/> for new material.
    /// </summary>
    public sealed class PrngPixelSelector : IPixelSelector
    {
        private readonly string _prngName;
        private readonly int _rowSeed;
        private readonly int _columnSeed;

        public PrngPixelSelector(int rowSeed, int columnSeed, string prngName = "Random")
        {
            if (string.IsNullOrEmpty(prngName))
                throw new ArgumentException("PRNG name must not be empty.", nameof(prngName));

            _prngName = prngName;
            _rowSeed = rowSeed;
            _columnSeed = columnSeed;
        }

        public IEnumerable<Point> Pixels(int width, int height)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height));

            var rows = new PRNG { Name = _prngName };
            rows.Initialize(_rowSeed);
            var columns = new PRNG { Name = _prngName };
            columns.Initialize(_columnSeed);

            return Enumerate(rows, columns, width, height);
        }

        private static IEnumerable<Point> Enumerate(PRNG rows, PRNG columns, int width, int height)
        {
            long total = (long)width * height;
            var used = new HashSet<Point>();

            while (used.Count < total)
            {
                var p = new Point(columns.Next(width), rows.Next(height));
                if (used.Add(p))
                    yield return p;
            }
        }
    }
}
