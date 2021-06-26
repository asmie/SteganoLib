using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SteganoLib.Test
{
    public class PRNGTest
    {


        public static IEnumerable<object[]> PRNGIntegerDefaultTab =>
        new List<object[]>
        {
            new object[] { 123, new int[] { 2114319875, 1949518561, 1596751841, 1742987178, 1586516133 } },
            new object[] { 2345, new int[] { 1747203526, 1898340343, 1921697988 } },
        };

        [Theory]
        [MemberData(nameof(PRNGIntegerDefaultTab))]
        public void PRNGGenerateInts(int seed, int[] expected)
        {
            var rand = new Crypto.PRNG { };
            rand.Name = "Random";

            rand.Initialize(seed);

            for (var i = 0; i < expected.Length; i++)
            {
                var rnd = rand.Next();
                Assert.Equal(expected[i], rnd);
            }
        }

        public static IEnumerable<object[]> PRNGIntegerWithMaxTab =>
        new List<object[]>
        {
            new object[] { 982760, 500, new int[] { 214, 448, 102, 366, 165 } },
            new object[] { 1114, 10000, new int[] { 7080, 6856, 4406, 3335 } },
            new object[] { 1, 1, new int[] { 0, 0, 0, 0 } },
        };

        [Theory]
        [MemberData(nameof(PRNGIntegerWithMaxTab))]
        public void PRNGGenerateIntsWithMax(int seed, int max, int[] expected)
        {
            var rand = new Crypto.PRNG { };
            rand.Name = "Random";

            rand.Initialize(seed);

            for (var i = 0; i < expected.Length; i++)
            {
                var rnd = rand.Next(max);
                Assert.Equal(expected[i], rnd);
            }
        }
    }
}
