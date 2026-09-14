using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Selection;

namespace SteganoLib.Benchmarks
{
    [MemoryDiagnoser]
    public class LsbBenchmarks
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 1, 2, 3 });

        private Image<Rgba32> _work;
        private Image<Rgba32> _stego;
        private byte[] _payload;
        private byte[] _smallPayload;
        private LSB _lsb;
        private LSB _trellis;

        [Params(512, 1024)]
        public int Width { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            int height = Width * 3 / 4;
            _work = Covers.Picture(Width, height);
            _lsb = new LSB(new KeyedPermutationSelector(Key));
            _trellis = new LSB(new AdaptivePixelSelector(new KeyedPermutationSelector(Key))) { TrellisCoder = new SyndromeTrellisCoder(7) };

            _payload = Covers.Payload((int)(_lsb.Capacity(_work) / 2));
            _smallPayload = Covers.Payload((int)(_trellis.Capacity(_work) / 16));

            _stego = _work.Clone();
            _lsb.EmbedBytes(_payload, _stego);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _work.Dispose();
            _stego.Dispose();
        }

        [Benchmark(Description = "LSB matching embed, half capacity")]
        public void Embed() => _lsb.EmbedBytes(_payload, _work);

        [Benchmark(Description = "LSB extract")]
        public byte[] Extract() => _lsb.ExtractBytes(_stego);

        [Benchmark(Description = "Capacity")]
        public long Capacity() => _lsb.Capacity(_work);

        [Benchmark(Description = "Adaptive LSB with trellis, 1/16 capacity")]
        public void EmbedAdaptiveTrellis() => _trellis.EmbedBytes(_smallPayload, _work);
    }
}
