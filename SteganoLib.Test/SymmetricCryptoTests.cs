using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SteganoLib.Test
{
    public class SymmetricCryptoTests
    {
        private static readonly byte[] PlainKey = Encoding.UTF8.GetBytes("1234567890123456");

        private static Crypto.SymmetricCrypto WithPlainKey(byte[] key = null)
        {
            return new Crypto.SymmetricCrypto
            {
                Key = key ?? PlainKey,
                KeyType = Crypto.SymmetricCrypto.KeyTypes.Plain,
            };
        }

        private static Crypto.SymmetricCrypto WithPassphrase(string passphrase)
        {
            return new Crypto.SymmetricCrypto
            {
                Key = Encoding.UTF8.GetBytes(passphrase),
                KeyType = Crypto.SymmetricCrypto.KeyTypes.RFC2898Derived,
                Iterations = 1000,
            };
        }

        [Theory]
        [InlineData(1)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(1000)]
        public void PlainKey_RoundTrip(int length)
        {
            var plaintext = new byte[length];
            new Random(length).NextBytes(plaintext);

            var encrypted = WithPlainKey().EncryptMemory(plaintext);
            var decrypted = WithPlainKey().DecryptMemory(encrypted);

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void PlainKey_OutputLayout()
        {
            var plaintext = new byte[10];

            var encrypted = WithPlainKey().EncryptMemory(plaintext);

            // version + flags + IV(16) + one padded block(16) + tag(32)
            Assert.Equal(1 + 1 + 16 + 16 + 32, encrypted.Length);
            Assert.Equal(1, encrypted[0]);
            Assert.Equal(0, encrypted[1]);
        }

        [Fact]
        public void DerivedKey_OutputLayoutIncludesSalt()
        {
            var encrypted = WithPassphrase("pw").EncryptMemory(new byte[10]);

            Assert.Equal(1 + 1 + 16 + 16 + 16 + 32, encrypted.Length);
            Assert.Equal(0x01, encrypted[1]);
        }

        [Fact]
        public void Encrypt_SameInputTwice_DiffersBecauseOfRandomIv()
        {
            var crypto = WithPlainKey();
            var plaintext = Encoding.UTF8.GetBytes("same input");

            Assert.NotEqual(crypto.EncryptMemory(plaintext), crypto.EncryptMemory(plaintext));
        }

        [Fact]
        public void Text_RoundTrip()
        {
            const string text = "Round-trip text test";

            var decrypted = WithPlainKey().DecryptText(WithPlainKey().EncryptText(text));

            Assert.Equal(text, decrypted);
        }

        [Fact]
        public void DerivedKey_RoundTrip()
        {
            var plaintext = Encoding.UTF8.GetBytes("derived key test");

            var encrypted = WithPassphrase("my secret passphrase").EncryptMemory(plaintext);
            var decrypted = WithPassphrase("my secret passphrase").DecryptMemory(encrypted);

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void DerivedKey_WrongPassphrase_Throws()
        {
            var encrypted = WithPassphrase("correct").EncryptMemory(new byte[] { 1, 2, 3 });

            Assert.Throws<CryptographicException>(() => WithPassphrase("wrong").DecryptMemory(encrypted));
        }

        [Fact]
        public void PlainKey_WrongKey_Throws()
        {
            var encrypted = WithPlainKey().EncryptMemory(new byte[] { 1, 2, 3 });

            Assert.Throws<CryptographicException>(() => WithPlainKey(Encoding.UTF8.GetBytes("6543210987654321")).DecryptMemory(encrypted));
        }

        [Theory]
        [InlineData(0)]   // version
        [InlineData(2)]   // IV
        [InlineData(20)]  // ciphertext
        [InlineData(-1)]  // tag
        public void TamperedByte_Throws(int index)
        {
            var encrypted = WithPlainKey().EncryptMemory(new byte[] { 1, 2, 3 });
            if (index < 0) index = encrypted.Length + index;
            encrypted[index] ^= 0x80;

            Assert.Throws<CryptographicException>(() => WithPlainKey().DecryptMemory(encrypted));
        }

        [Fact]
        public void Truncated_Throws()
        {
            var encrypted = WithPlainKey().EncryptMemory(new byte[] { 1, 2, 3 });
            var truncated = encrypted.AsSpan(0, encrypted.Length - 1).ToArray();

            Assert.Throws<CryptographicException>(() => WithPlainKey().DecryptMemory(truncated));
            Assert.Throws<CryptographicException>(() => WithPlainKey().DecryptMemory(new byte[5]));
        }

        [Fact]
        public void KeyTypeMismatch_Throws()
        {
            var encrypted = WithPassphrase("pw").EncryptMemory(new byte[] { 1 });

            Assert.Throws<CryptographicException>(() => WithPlainKey().DecryptMemory(encrypted));
        }

        [Fact]
        public void EcbMode_Rejected()
        {
            var crypto = WithPlainKey();
            crypto.Mode = CipherMode.ECB;

            Assert.Throws<InvalidOperationException>(() => crypto.EncryptMemory(new byte[] { 1 }));
        }

        [Fact]
        public void NoKey_ThrowsInvalidOperation()
        {
            var crypto = new Crypto.SymmetricCrypto();

            Assert.Throws<InvalidOperationException>(() => crypto.EncryptMemory(new byte[] { 1 }));
            Assert.Throws<InvalidOperationException>(() => crypto.DecryptMemory(new byte[] { 1 }));
        }

        [Fact]
        public void UnknownAlgorithm_ThrowsInvalidOperation()
        {
            var crypto = WithPlainKey();
            crypto.Algorithm = "DoesNotExist";

            Assert.Throws<InvalidOperationException>(() => crypto.EncryptMemory(new byte[] { 1 }));
        }

        [Fact]
        public void NullInput_ThrowsArgumentNull()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => WithPlainKey().EncryptMemory(null));
            Assert.Equal("plain", ex.ParamName);
            Assert.Throws<ArgumentNullException>(() => WithPlainKey().EncryptText(null));
        }

        [Fact]
        public void EmptyInput_ThrowsArgument()
        {
            Assert.Equal("plain", Assert.Throws<ArgumentException>(() => WithPlainKey().EncryptMemory(Array.Empty<byte>())).ParamName);
            Assert.Equal("encrypted", Assert.Throws<ArgumentException>(() => WithPlainKey().DecryptMemory(Array.Empty<byte>())).ParamName);
        }

        [Fact]
        public void Iterations_BelowOne_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Crypto.SymmetricCrypto { Iterations = 0 });
        }

        [Fact]
        public void RegisterAlgorithm_InvalidType_ReturnsFalse()
        {
            Assert.False(Crypto.SymmetricCrypto.RegisterAlgorithm("Bad_" + Guid.NewGuid().ToString("N"), typeof(string)));
        }

        [Fact]
        public void RegisterAlgorithm_DuplicateName_SecondReturnsFalse()
        {
            var name = "Aes_" + Guid.NewGuid().ToString("N");

            Assert.True(Crypto.SymmetricCrypto.RegisterAlgorithm(name, typeof(Aes)));
            Assert.False(Crypto.SymmetricCrypto.RegisterAlgorithm(name, typeof(Aes)));
        }
    }
}
