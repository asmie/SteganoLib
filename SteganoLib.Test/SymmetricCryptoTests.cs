using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SteganoLib.Test
{

    public class SymmetricCryptoTests
    {

       public static IEnumerable<object[]> Aes128CorrectDataCBCPaddingPKCS =>
       new List<object[]>
       {
            new object[] { new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 }, "1234567890123456", new byte[] { 0xe7, 0x31, 0x91, 0x72, 0x0a, 0xe8, 0x14, 0xa1, 0x7f, 0x80, 0x13, 0xe1, 0x2a, 0x09, 0x9a, 0x87, 0xbe, 0x37, 0x65, 0x46, 0x38, 0xc4, 0xdc, 0x23, 0xde, 0x38, 0xf5, 0x54, 0x39, 0x3a, 0xea, 0xe9 } },
            new object[] { new byte[] { 0xAB, 0xEF, 0x00, 0x12, 0x98, 0x50, 0x11, 0x01, 0x9D, 0x10, 0x2F, 0x13, 0x34, 0x15 }, "1234567890123456", new byte[] { 0x88, 0xc3, 0x7e, 0x84, 0x3b, 0x62, 0x45, 0xf4, 0xb9, 0xfe, 0x14, 0xce, 0x27, 0xef, 0xc9, 0xb5 } },
            new object[] { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "1234567890123456", new byte[] { 0xd8, 0xb5, 0x98, 0x48, 0xc7, 0x67, 0x0c, 0x94, 0xb2, 0x9b, 0x54, 0xd2, 0x37, 0x9e, 0x2e, 0x7a, 0xc5, 0xeb, 0xc9, 0xeb, 0x9d, 0xee, 0x53, 0x8f, 0x0a, 0xd9, 0x69, 0x59, 0x5d, 0x7a, 0x74, 0x0c } },
            new object[] { new byte[] { 0x23, 0xC4, 0xEE, 0x07, 0x74, 0x25, 0xA2, 0xAC, 0x52, 0x00, 0xE0, 0x0E }, "cB34hy0@egzx73E!", new byte[] { 0x5d, 0xc8, 0xf8, 0x18, 0x02, 0x95, 0x16, 0x52, 0x19, 0x79, 0x66, 0x3e, 0xee, 0xc5, 0x70, 0xf4 } },
       };

        [Theory]
        [MemberData(nameof(Aes128CorrectDataCBCPaddingPKCS))]
        public void AES128EncryptMemoryTestData(byte[] input, string key, byte[] expected)
        {
            var aes = new Crypto.SymmetricCrypto { };

            aes.Key = Encoding.UTF8.GetBytes(key);
            aes.KeyType = Crypto.SymmetricCrypto.KeyTypes.Plain;
            aes.Algorithm = "AES";
            aes.Mode = System.Security.Cryptography.CipherMode.CBC;

            var output = aes.EncryptMemory(input);
            Assert.Equal(output.Length, expected.Length);
            Assert.Equal<byte[]>(output, expected);
        }
    }
}
