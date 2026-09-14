using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SteganoLib.Crypto;
using Xunit;

namespace SteganoLib.Test
{
    public class CryptoContractTests
    {
        private static string UniqueName() => "CryptoContract_" + Guid.NewGuid().ToString("N");
        private static byte[] KeyBytes => Encoding.ASCII.GetBytes("1234567890123456");
        private static byte[] Message => Encoding.UTF8.GetBytes("A message with a partial final block.");

        private static SymmetricCrypto Cipher(bool derived = false) => new()
        {
            Key = KeyBytes,
            KeyType = derived ? SymmetricCrypto.KeyTypes.RFC2898Derived : SymmetricCrypto.KeyTypes.Plain,
            Iterations = 10,
        };

        public abstract class AbstractRandom : Random
        {
            public AbstractRandom(int seed) : base(seed) { }
        }

        public class GenericRandom<T> : Random
        {
            public GenericRandom(int seed) : base(seed) { }
        }

        public class DefaultRandom : Random { }

        public class PrivateRandom : Random
        {
            private PrivateRandom(int seed) : base(seed) { }
        }

        public class WidenedRandom : Random
        {
            public WidenedRandom(long seed) : base((int)seed) { }
        }

        public class AmbiguousRandom : Random
        {
            public AmbiguousRandom(long seed) { }
            public AmbiguousRandom(double seed) { }
        }

        public abstract class AbstractCipher : Aes { }

        public class GenericCipher<T> : TrackingAes { }

        public class ParameterCipher : TrackingAes
        {
            public ParameterCipher(int unused) { }
        }

        public class PrivateCipher : TrackingAes
        {
            private PrivateCipher() { }
        }

        public class TrackingAes : Aes
        {
            private readonly Aes _inner = Aes.Create();
            public bool Disposed { get; private set; }

            public override ICryptoTransform CreateEncryptor(byte[] key, byte[] iv)
            {
                _inner.Mode = Mode;
                _inner.Padding = Padding;
                return _inner.CreateEncryptor(key, iv);
            }

            public override ICryptoTransform CreateDecryptor(byte[] key, byte[] iv)
            {
                _inner.Mode = Mode;
                _inner.Padding = Padding;
                return _inner.CreateDecryptor(key, iv);
            }

            public override void GenerateIV()
            {
                _inner.GenerateIV();
                IV = _inner.IV;
            }

            public override void GenerateKey()
            {
                _inner.GenerateKey();
                Key = _inner.Key;
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        [Theory]
        [InlineData(typeof(string))]
        [InlineData(typeof(AbstractRandom))]
        [InlineData(typeof(GenericRandom<>))]
        [InlineData(typeof(DefaultRandom))]
        [InlineData(typeof(PrivateRandom))]
        [InlineData(typeof(WidenedRandom))]
        [InlineData(typeof(AmbiguousRandom))]
        public void Prng_InvalidTypeDoesNotReserveName(Type type)
        {
            string name = UniqueName();
            Assert.False(PRNG.RegisterPRNG(name, type));
            Assert.True(PRNG.RegisterPRNGFactory(name, seed => new Random(seed)));
        }

        [Theory]
        [InlineData(typeof(string))]
        [InlineData(typeof(SymmetricAlgorithm))]
        [InlineData(typeof(AbstractCipher))]
        [InlineData(typeof(GenericCipher<>))]
        [InlineData(typeof(ParameterCipher))]
        [InlineData(typeof(PrivateCipher))]
        public void Cipher_InvalidTypeDoesNotReserveName(Type type)
        {
            string name = UniqueName();
            Assert.False(SymmetricCrypto.RegisterAlgorithm(name, type));
            Assert.True(SymmetricCrypto.RegisterAlgorithmFactory(name, Aes.Create));
        }

        [Fact]
        public void Registration_RejectsNullArguments()
        {
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => PRNG.RegisterPRNG(null, typeof(Random))).ParamName);
            Assert.Equal("creator", Assert.Throws<ArgumentNullException>(() => PRNG.RegisterPRNG(UniqueName(), null)).ParamName);
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => PRNG.RegisterPRNGFactory(null, seed => new Random(seed))).ParamName);
            Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => PRNG.RegisterPRNGFactory(UniqueName(), null)).ParamName);
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => SymmetricCrypto.RegisterAlgorithm(null, typeof(Aes))).ParamName);
            Assert.Equal("creator", Assert.Throws<ArgumentNullException>(() => SymmetricCrypto.RegisterAlgorithm(UniqueName(), null)).ParamName);
            Assert.Equal("name", Assert.Throws<ArgumentNullException>(() => SymmetricCrypto.RegisterAlgorithmFactory(null, Aes.Create)).ParamName);
            Assert.Equal("factory", Assert.Throws<ArgumentNullException>(() => SymmetricCrypto.RegisterAlgorithmFactory(UniqueName(), null)).ParamName);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" \t")]
        public void Registration_RejectsBlankNames(string name)
        {
            Assert.Throws<ArgumentException>(() => PRNG.RegisterPRNG(name, typeof(Random)));
            Assert.Throws<ArgumentException>(() => PRNG.RegisterPRNGFactory(name, seed => new Random(seed)));
            Assert.Throws<ArgumentException>(() => SymmetricCrypto.RegisterAlgorithm(name, typeof(Aes)));
            Assert.Throws<ArgumentException>(() => SymmetricCrypto.RegisterAlgorithmFactory(name, Aes.Create));
        }

        [Fact]
        public void InvalidPropertyAssignmentPreservesPreviousConfiguration()
        {
            var random = new PRNG();
            var cipher = Cipher();
            Assert.Throws<ArgumentNullException>(() => random.Name = null);
            Assert.Throws<ArgumentNullException>(() => cipher.Algorithm = null);
            Assert.Throws<ArgumentException>(() => random.Name = " ");
            Assert.Throws<ArgumentException>(() => cipher.Algorithm = "");
            Assert.Throws<ArgumentOutOfRangeException>(() => cipher.KeyType = (SymmetricCrypto.KeyTypes)123);
            Assert.Equal("Random", random.Name);
            Assert.Equal("AES", cipher.Algorithm);
            Assert.Equal(SymmetricCrypto.KeyTypes.Plain, cipher.KeyType);
            Assert.Equal(Message, cipher.DecryptMemory(cipher.EncryptMemory(Message)));
        }

        [Fact]
        public void Prng_FactoryIsLazyReceivesSeedAndCreatesSeparateGenerators()
        {
            string name = UniqueName();
            int calls = 0;
            Assert.True(PRNG.RegisterPRNGFactory(name, seed =>
            {
                calls++;
                return new Random(seed + 1);
            }));
            Assert.Equal(0, calls);
            var first = new PRNG { Name = name };
            var second = new PRNG { Name = name };
            first.Initialize(42);
            second.Initialize(42);
            var expected = new Random(43);
            for (int i = 0; i < 5; i++)
            {
                int value = expected.Next();
                Assert.Equal(value, first.Next());
                Assert.Equal(value, second.Next());
            }
            Assert.Equal(2, calls);
        }

        [Theory]
        [InlineData(typeof(Random))]
        [InlineData(typeof(GenericRandom<int>))]
        public void Prng_TypeRegistrationRetainsSeededSequence(Type type)
        {
            string name = UniqueName();
            Assert.True(PRNG.RegisterPRNG(name, type));
            var random = new PRNG { Name = name };
            random.Initialize(-37);
            var expected = new Random(-37);
            for (int i = 0; i < 5; i++)
                Assert.Equal(expected.Next(), random.Next());
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("null")]
        [InlineData("throws")]
        public void Prng_FailedInitializationPreservesPreviousSequence(string failure)
        {
            var random = new PRNG();
            random.Initialize(42);
            var expected = new Random(42);
            Assert.Equal(expected.Next(), random.Next());
            string name = UniqueName();
            var error = new NotSupportedException("Factory failed.");
            if (failure == "null") Assert.True(PRNG.RegisterPRNGFactory(name, seed => null));
            if (failure == "throws") Assert.True(PRNG.RegisterPRNGFactory(name, seed => throw error));
            random.Name = name;
            Assert.Equal(expected.Next(), random.Next()); // Name changes apply only on Initialize.
            if (failure == "throws")
                Assert.Same(error, Assert.Throws<NotSupportedException>(() => random.Initialize(99)));
            else
                Assert.Contains(name, Assert.Throws<InvalidOperationException>(() => random.Initialize(99)).Message);
            Assert.Equal(expected.Next(), random.Next());
            random.Name = "Random";
            random.Initialize(99);
            Assert.Equal(new Random(99).Next(), random.Next());
        }

        [Fact]
        public void Registries_FirstRegistrationWinsAcrossTypeAndFactoryApis()
        {
            string randomName = UniqueName();
            Assert.True(PRNG.RegisterPRNGFactory(randomName, seed => new Random(seed + 1)));
            Assert.False(PRNG.RegisterPRNG(randomName, typeof(Random)));
            Assert.False(PRNG.RegisterPRNGFactory(randomName, seed => throw new Exception("Replaced registration.")));
            var random = new PRNG { Name = randomName };
            random.Initialize(3);
            Assert.Equal(new Random(4).Next(), random.Next());

            string cipherName = UniqueName();
            Assert.True(SymmetricCrypto.RegisterAlgorithm(cipherName, typeof(Aes)));
            Assert.False(SymmetricCrypto.RegisterAlgorithmFactory(cipherName, () => throw new Exception("Replaced registration.")));
            var cipher = Cipher();
            cipher.Algorithm = cipherName;
            Assert.Equal(Message, cipher.DecryptMemory(cipher.EncryptMemory(Message)));
        }

        [Fact]
        public void Registries_ConcurrentRegistrationHasExactlyOneWinner()
        {
            string randomName = UniqueName(), cipherName = UniqueName();
            int randomWins = 0, cipherWins = 0, factoryCalls = 0;
            Parallel.For(0, 32, i =>
            {
                if (PRNG.RegisterPRNGFactory(randomName, seed =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return new Random(seed);
                })) Interlocked.Increment(ref randomWins);
                if (SymmetricCrypto.RegisterAlgorithmFactory(cipherName, () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return Aes.Create();
                })) Interlocked.Increment(ref cipherWins);
            });
            Assert.Equal(1, randomWins);
            Assert.Equal(1, cipherWins);
            Assert.Equal(0, factoryCalls);
            new PRNG { Name = randomName }.Initialize(1);
            var cipher = Cipher();
            cipher.Algorithm = cipherName;
            Assert.Equal(Message, cipher.DecryptMemory(cipher.EncryptMemory(Message)));
            Assert.Equal(3, factoryCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Cipher_AssignmentCopiesKeyOrPassphrase(bool derived)
        {
            byte[] key = KeyBytes;
            var cipher = Cipher(derived);
            cipher.Key = key;
            Array.Fill(key, (byte)0);
            var peer = Cipher(derived);
            Assert.Equal(Message, peer.DecryptMemory(cipher.EncryptMemory(Message)));
            Assert.Equal(Message, cipher.DecryptMemory(peer.EncryptMemory(Message)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Cipher_KeyCanBeClearedAndReplaced(bool empty)
        {
            var cipher = Cipher();
            byte[] encrypted = cipher.EncryptMemory(Message);
            cipher.Key = empty ? Array.Empty<byte>() : null;
            Assert.Throws<InvalidOperationException>(() => cipher.EncryptMemory(Message));
            Assert.Throws<InvalidOperationException>(() => cipher.DecryptMemory(encrypted));
            cipher.Key = KeyBytes;
            Assert.Equal(Message, cipher.DecryptMemory(encrypted));
        }

        [Theory]
        [InlineData(typeof(Aes))]
        [InlineData(typeof(TrackingAes))]
        [InlineData(typeof(GenericCipher<int>))]
        public void Cipher_ConstructibleTypesRemainUsable(Type type)
        {
            string name = UniqueName();
            Assert.True(SymmetricCrypto.RegisterAlgorithm(name, type));
            var cipher = Cipher();
            cipher.Algorithm = name;
            Assert.Equal(Message, Cipher().DecryptMemory(cipher.EncryptMemory(Message)));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Cipher_FactoryFailureIsReportedAndLaterUseCanRecover(bool throws)
        {
            string name = UniqueName();
            var error = new NotSupportedException("Factory failed.");
            Assert.True(SymmetricCrypto.RegisterAlgorithmFactory(name, () => throws ? throw error : null));
            var cipher = Cipher();
            cipher.Algorithm = name;
            foreach (Action operation in new Action[] { () => cipher.EncryptMemory(Message), () => cipher.DecryptMemory(Message) })
            {
                if (throws) Assert.Same(error, Assert.Throws<NotSupportedException>(operation));
                else Assert.Contains(name, Assert.Throws<InvalidOperationException>(operation).Message);
            }
            cipher.Algorithm = "AES";
            Assert.Equal(Message, cipher.DecryptMemory(cipher.EncryptMemory(Message)));
        }

        [Fact]
        public void Cipher_FactoryIsLazyAndInstancesAreDisposedOnSuccessAndFailure()
        {
            string name = UniqueName();
            var instances = new ConcurrentBag<TrackingAes>();
            Assert.True(SymmetricCrypto.RegisterAlgorithmFactory(name, () =>
            {
                var instance = new TrackingAes();
                instances.Add(instance);
                return instance;
            }));
            Assert.Empty(instances);
            var cipher = Cipher();
            cipher.Algorithm = name;
            byte[] encrypted = cipher.EncryptMemory(Message);
            Assert.Equal(Message, cipher.DecryptMemory(encrypted));
            encrypted[^1] ^= 1;
            Assert.Throws<CryptographicException>(() => cipher.DecryptMemory(encrypted));
            cipher.Padding = PaddingMode.None;
            Assert.Throws<CryptographicException>(() => cipher.EncryptMemory(new byte[1]));
            Assert.Equal(4, instances.Count);
            Assert.All(instances, instance => Assert.True(instance.Disposed));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Cipher_OperationUsesSettingsCapturedBeforeFactoryCallback(bool derived, bool decrypt)
        {
            var cipher = Cipher(derived);
            string name = UniqueName();
            Assert.True(SymmetricCrypto.RegisterAlgorithmFactory(name, () =>
            {
                cipher.Key = new byte[32];
                cipher.KeyType = derived ? SymmetricCrypto.KeyTypes.Plain : SymmetricCrypto.KeyTypes.RFC2898Derived;
                cipher.Iterations = 11;
                cipher.HashAlgorithm = HashAlgorithmName.SHA512;
                cipher.Mode = CipherMode.ECB;
                cipher.Padding = PaddingMode.None;
                cipher.Algorithm = "ChangedDuringOperation";
                return Aes.Create();
            }));
            cipher.Algorithm = name;
            var peer = Cipher(derived);
            if (decrypt)
                Assert.Equal(Message, cipher.DecryptMemory(peer.EncryptMemory(Message)));
            else
                Assert.Equal(Message, peer.DecryptMemory(cipher.EncryptMemory(Message)));
        }

        [Fact]
        public void Cipher_ConcurrentOperationsWithStableConfigurationUseSeparateInstances()
        {
            var cipher = Cipher();
            string name = UniqueName();
            var instances = new ConcurrentBag<TrackingAes>();
            Assert.True(SymmetricCrypto.RegisterAlgorithmFactory(name, () =>
            {
                var instance = new TrackingAes();
                instances.Add(instance);
                return instance;
            }));
            cipher.Algorithm = name;
            Parallel.For(0, 16, i =>
            {
                byte[] message = BitConverter.GetBytes(i);
                Assert.Equal(message, cipher.DecryptMemory(cipher.EncryptMemory(message)));
            });
            Assert.Equal(32, instances.Distinct().Count());
            Assert.All(instances, instance => Assert.True(instance.Disposed));
        }
    }
}
