using BenchmarkDotNet.Attributes;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;

namespace SteganoLib.Benchmarks
{
    [MemoryDiagnoser]
    public class JpegBenchmarks
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 4, 5, 6 });

        private byte[] _file;
        private JpegImage _image;
        private JpegImage _stego;
        private byte[] _payload;
        private F5 _f5;

        [GlobalSetup]
        public void Setup()
        {
            _file = Covers.Jpeg(1024, 768);
            _image = JpegImage.Load(_file);
            _f5 = new F5(Key);
            _payload = Covers.Payload((int)(_f5.Capacity(_image) / 4));
            _stego = _image.Clone();
            _f5.EmbedBytes(_payload, _stego);
        }

        [Benchmark(Description = "Decode coefficients")]
        public JpegImage Decode() => JpegImage.Load(_file);

        [Benchmark(Description = "Encode coefficients")]
        public byte[] Encode() => _image.ToArray();

        [Benchmark(Description = "F5 embed, quarter capacity")]
        public void F5Embed()
        {
            var work = _image.Clone();
            _f5.EmbedBytes(_payload, work);
        }

        [Benchmark(Description = "F5 extract")]
        public byte[] F5Extract() => _f5.ExtractBytes(_stego);
    }
}
