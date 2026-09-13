using System;
using SteganoLib.Crypto;
using Xunit;

namespace SteganoLib.Test
{
    public class StegoKeyTests
    {
        [Fact]
        public void Derive_SamePurpose_SameOutput()
        {
            var a = StegoKey.FromBytes(new byte[] { 1, 2, 3 });
            var b = StegoKey.FromBytes(new byte[] { 1, 2, 3 });

            Assert.Equal(a.Derive("x", 32), b.Derive("x", 32));
        }

        [Fact]
        public void Derive_DifferentPurpose_DifferentOutput()
        {
            var key = StegoKey.FromBytes(new byte[] { 1, 2, 3 });

            Assert.NotEqual(key.Derive("x", 32), key.Derive("y", 32));
        }

        [Fact]
        public void FromPassphrase_IsDeterministic()
        {
            var a = StegoKey.FromPassphrase("pw", iterations: 10);
            var b = StegoKey.FromPassphrase("pw", iterations: 10);

            Assert.Equal(a.Derive("x", 16), b.Derive("x", 16));
        }

        [Fact]
        public void FromPassphrase_SaltChangesKey()
        {
            var a = StegoKey.FromPassphrase("pw", salt: new byte[] { 1 }, iterations: 10);
            var b = StegoKey.FromPassphrase("pw", salt: new byte[] { 2 }, iterations: 10);

            Assert.NotEqual(a.Derive("x", 16), b.Derive("x", 16));
        }

        [Fact]
        public void CreateRandom_KeysDiffer()
        {
            Assert.NotEqual(StegoKey.CreateRandom().Derive("x", 16), StegoKey.CreateRandom().Derive("x", 16));
        }

        [Fact]
        public void InvalidInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => StegoKey.FromBytes(Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => StegoKey.FromPassphrase(""));
            Assert.Throws<ArgumentOutOfRangeException>(() => StegoKey.FromPassphrase("pw", iterations: 0));
            Assert.Throws<ArgumentException>(() => StegoKey.FromBytes(new byte[] { 1 }).Derive("", 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => StegoKey.FromBytes(new byte[] { 1 }).Derive("x", 0));
        }
    }
}
