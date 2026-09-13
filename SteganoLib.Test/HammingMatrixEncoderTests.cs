using System;
using SteganoLib.Coding;
using Xunit;

namespace SteganoLib.Test
{
    public class HammingMatrixEncoderTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 3)]
        [InlineData(3, 7)]
        [InlineData(7, 127)]
        public void N_IsTwoToTheKMinusOne(int k, int n)
        {
            Assert.Equal(n, new HammingMatrixEncoder(k).N);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(16)]
        public void InvalidK_Throws(int k)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HammingMatrixEncoder(k));
        }

        [Fact]
        public void Syndrome_XorsSetPositions()
        {
            var code = new HammingMatrixEncoder(3);
            var word = new bool[7];
            word[0] = true; // position 1
            word[4] = true; // position 5

            Assert.Equal(1 ^ 5, code.Syndrome(word));
        }

        [Fact]
        public void PositionToFlip_ZeroWhenWordAlreadyCarriesMessage()
        {
            var code = new HammingMatrixEncoder(3);
            var word = new bool[7];
            word[2] = true; // syndrome 3

            Assert.Equal(0, code.PositionToFlip(word, 3));
        }

        [Fact]
        public void PositionToFlip_FlippingItYieldsTheMessage()
        {
            var random = new Random(3);
            for (int k = 1; k <= 7; k++)
            {
                var code = new HammingMatrixEncoder(k);
                for (int trial = 0; trial < 200; trial++)
                {
                    var word = new bool[code.N];
                    for (int i = 0; i < word.Length; i++)
                        word[i] = random.Next(2) == 1;
                    int message = random.Next(code.N + 1);

                    int position = code.PositionToFlip(word, message);
                    if (position != 0)
                        word[position - 1] = !word[position - 1];

                    Assert.Equal(message, code.Syndrome(word));
                }
            }
        }

        [Fact]
        public void WrongWordLength_Throws()
        {
            Assert.Throws<ArgumentException>(() => new HammingMatrixEncoder(2).Syndrome(new bool[4]));
        }

        [Fact]
        public void MessageOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HammingMatrixEncoder(2).PositionToFlip(new bool[3], 4));
        }

        [Fact]
        public void Rates()
        {
            var code = new HammingMatrixEncoder(3);
            Assert.Equal(3.0 / 7.0, code.Rate, 10);
            Assert.Equal(7.0 / 8.0 / 3.0, code.ChangesPerBit, 10);
        }
    }
}
