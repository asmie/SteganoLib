using System;
using System.Numerics;
using SteganoLib.Audio;
using Xunit;

namespace SteganoLib.Test
{
    public class FftTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(8)]
        [InlineData(64)]
        [InlineData(256)]
        public void Forward_MatchesDirectDft(int n)
        {
            var random = new Random(n);
            var input = new Complex[n];
            for (int i = 0; i < n; i++)
                input[i] = new Complex(random.NextDouble() - 0.5, random.NextDouble() - 0.5);

            var fft = (Complex[])input.Clone();
            Fft.Forward(fft);

            for (int k = 0; k < n; k++)
            {
                var expected = Complex.Zero;
                for (int t = 0; t < n; t++)
                    expected += input[t] * Complex.FromPolarCoordinates(1, -2 * Math.PI * k * t / n);
                Assert.True((expected - fft[k]).Magnitude < 1e-9, $"bin {k}: {expected} vs {fft[k]}");
            }
        }

        [Fact]
        public void InverseUndoesForward()
        {
            var random = new Random(7);
            var input = new Complex[1024];
            for (int i = 0; i < input.Length; i++)
                input[i] = new Complex(random.Next(-30000, 30000), 0);

            var data = (Complex[])input.Clone();
            Fft.Forward(data);
            Fft.Inverse(data);

            for (int i = 0; i < input.Length; i++)
                Assert.True((input[i] - data[i]).Magnitude < 1e-6);
        }

        [Fact]
        public void NonPowerOfTwo_Throws()
        {
            Assert.Throws<ArgumentException>(() => Fft.Forward(new Complex[12]));
            Assert.False(Fft.IsPowerOfTwo(0));
            Assert.True(Fft.IsPowerOfTwo(1024));
        }
    }
}
