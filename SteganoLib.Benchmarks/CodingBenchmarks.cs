using System;

using BenchmarkDotNet.Attributes;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Payload;

namespace SteganoLib.Benchmarks
{
    [MemoryDiagnoser]
    public class CodingBenchmarks
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 7, 8, 9 });

        private byte[] _data;
        private byte[] _encoded;
        private byte[] _damaged;
        private ReedSolomonCode _code;
        private bool[] _cover;
        private double[] _costs;
        private bool[] _message;
        private SyndromeTrellisCoder _stc;
        private PayloadEnvelope _envelope;
        private byte[] _sealed;

        [GlobalSetup]
        public void Setup()
        {
            _data = Covers.Payload(64 * 1024);
            _code = new ReedSolomonCode(32);
            _encoded = _code.Encode(_data);
            _damaged = (byte[])_encoded.Clone();
            var random = new Random(5);
            for (int block = 0; block * 255 < _damaged.Length; block++)
            {
                for (int i = 0; i < 8; i++)
                {
                    int index = Math.Min(_damaged.Length - 1, block * 255 + random.Next(255));
                    _damaged[index] ^= (byte)random.Next(1, 256);
                }
            }

            _cover = new bool[200_000];
            _costs = new double[_cover.Length];
            for (int i = 0; i < _cover.Length; i++)
            {
                _cover[i] = random.Next(2) == 1;
                _costs[i] = 1 + random.NextDouble() * 4;
            }
            _message = new bool[_cover.Length / 8];
            for (int i = 0; i < _message.Length; i++)
                _message[i] = random.Next(2) == 1;
            _stc = new SyndromeTrellisCoder(7);

            _envelope = new PayloadEnvelope();
            _sealed = _envelope.Seal(_data, Key);
        }

        [Benchmark(Description = "Reed-Solomon encode 64 KB")]
        public byte[] ReedSolomonEncode() => _code.Encode(_data);

        [Benchmark(Description = "Reed-Solomon decode, clean")]
        public bool ReedSolomonDecodeClean() => _code.TryDecode(_encoded, out _, out _);

        [Benchmark(Description = "Reed-Solomon decode, 8 errors per block")]
        public bool ReedSolomonDecodeDamaged() => _code.TryDecode(_damaged, out _, out _);

        [Benchmark(Description = "STC embed 25k bits into 200k, height 7")]
        public double TrellisEmbed()
        {
            var stego = new bool[_cover.Length];
            return _stc.Embed(_cover, _costs, _message, stego);
        }

        [Benchmark(Description = "Envelope seal 64 KB (AES-GCM)")]
        public byte[] EnvelopeSeal() => _envelope.Seal(_data, Key);

        [Benchmark(Description = "Envelope open 64 KB")]
        public ExtractResult EnvelopeOpen() => _envelope.Open(_sealed, Key);
    }
}
