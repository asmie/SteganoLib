using System;
using System.Collections.Generic;

using SteganoLib.Crypto;

namespace SteganoLib.Selection
{
    /// <summary>Visits every index exactly once in an order derived from a <see cref="StegoKey"/>.</summary>
    public sealed class KeyedSampleSelector : ISampleSelector
    {
        private const string Purpose = "SteganoLib/sample-permutation/v1";

        private readonly byte[] _keyMaterial;

        public KeyedSampleSelector(StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            _keyMaterial = key.Derive(Purpose, FeistelPermutation.KeySize);
        }

        public IEnumerable<long> Indices(long count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            return Enumerate(count);
        }

        private IEnumerable<long> Enumerate(long count)
        {
            if (count == 0)
                yield break;

            var permutation = new FeistelPermutation(_keyMaterial, count);
            for (long i = 0; i < count; i++)
                yield return permutation.Permute(i);
        }
    }
}
