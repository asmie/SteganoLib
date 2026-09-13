using System;
using System.Collections.Generic;
using System.Linq;
using SteganoLib.Crypto;
using Xunit;

namespace SteganoLib.Test
{
    public class FeistelPermutationTests
    {
        private static byte[] Key(byte seed)
        {
            var key = new byte[FeistelPermutation.KeySize];
            for (int i = 0; i < key.Length; i++)
                key[i] = (byte)(seed + i * 7);
            return key;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(17)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(257)]
        [InlineData(10_000)]
        [InlineData(65_537)]
        public void Permute_IsBijection(long domain)
        {
            var permutation = new FeistelPermutation(Key(1), domain);
            var seen = new HashSet<long>();

            for (long i = 0; i < domain; i++)
            {
                long p = permutation.Permute(i);
                Assert.InRange(p, 0, domain - 1);
                Assert.True(seen.Add(p), $"Duplicate value {p} for index {i}");
            }

            Assert.Equal(domain, seen.Count);
        }

        [Fact]
        public void Permute_SameKey_IsDeterministic()
        {
            var a = new FeistelPermutation(Key(3), 1000);
            var b = new FeistelPermutation(Key(3), 1000);

            for (long i = 0; i < 1000; i++)
                Assert.Equal(a.Permute(i), b.Permute(i));
        }

        [Fact]
        public void Permute_DifferentKeys_ProduceDifferentOrders()
        {
            var a = new FeistelPermutation(Key(3), 1000);
            var b = new FeistelPermutation(Key(4), 1000);

            int same = Enumerable.Range(0, 1000).Count(i => a.Permute(i) == b.Permute(i));
            Assert.InRange(same, 0, 50);
        }

        [Fact]
        public void Permute_IsNotIdentity()
        {
            var permutation = new FeistelPermutation(Key(5), 1000);
            int fixedPoints = Enumerable.Range(0, 1000).Count(i => permutation.Permute(i) == i);
            Assert.InRange(fixedPoints, 0, 50);
        }

        [Fact]
        public void Permute_OutOfRange_Throws()
        {
            var permutation = new FeistelPermutation(Key(1), 10);
            Assert.Throws<ArgumentOutOfRangeException>(() => permutation.Permute(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => permutation.Permute(10));
        }

        [Fact]
        public void Constructor_ShortKey_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FeistelPermutation(new byte[15], 10));
        }

        [Fact]
        public void Constructor_EmptyDomain_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FeistelPermutation(Key(1), 0));
        }
    }
}
