using System;
using System.Collections.Generic;
using System.Linq;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class AudioEmbeddingFailureTests
    {
        public enum Failure { None, ShortSequence, MoveNext, InvalidIndex, Dispose }

        private sealed class Selector : ISampleSelector
        {
            public Failure Fail { get; set; }
            public int FailAfter { get; set; }
            public Action Visit { get; set; }
            public bool Disposed { get; private set; }
            public Exception Error { get; } = new InvalidOperationException("Selector failed.");

            public long Count(long count) => count;

            public IEnumerable<long> Indices(long count)
            {
                try
                {
                    for (long i = 0; i < count; i++)
                    {
                        Visit?.Invoke();
                        if (i == FailAfter)
                        {
                            if (Fail == Failure.ShortSequence) yield break;
                            if (Fail == Failure.MoveNext) throw Error;
                            if (Fail == Failure.InvalidIndex) yield return count;
                        }
                        yield return i;
                    }
                }
                finally
                {
                    Disposed = true;
                    if (Fail == Failure.Dispose) throw Error;
                }
            }
        }

        private sealed class ThrowingCost : ISampleCostModel
        {
            private int _calls;
            public double Cost(PcmAudio audio, long index) => ++_calls == 3
                ? throw new InvalidOperationException("Cost failed.") : 1;
        }

        public static IEnumerable<object[]> PartialFailures()
        {
            foreach (int bits in new[] { 1, 2, 3, 4 })
                foreach (bool inPayload in new[] { false, true })
                    foreach (var failure in new[] { Failure.ShortSequence, Failure.MoveNext, Failure.InvalidIndex })
                        yield return new object[] { bits, inPayload, failure };
        }

        private static PcmAudio Cover() => new(44100, 2, 16, Enumerable.Repeat(15, 256).ToArray());

        [Theory]
        [MemberData(nameof(PartialFailures))]
        public void FailedDirectEmbeddingPreservesSamplesAndAllowsRetry(int bits, bool inPayload, Failure failure)
        {
            var audio = Cover();
            int[] samples = audio.Samples;
            byte[] before = audio.ToArray();
            byte[] payload = { 0xA5, 0x5A, 0x81 };
            var selector = new Selector { Fail = failure, FailAfter = (inPayload ? 56 : 16) / bits };
            var algorithm = new AudioLsb(selector) { BitsPerSample = bits };

            if (failure == Failure.ShortSequence)
                Assert.Throws<CapacityExceededException>(() => algorithm.EmbedBytes(payload, audio));
            else if (failure == Failure.MoveNext)
                Assert.Same(selector.Error, Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(payload, audio)));
            else
                Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(payload, audio));

            Assert.True(selector.Disposed);
            Assert.Same(samples, audio.Samples);
            Assert.Equal(before, audio.ToArray());

            selector.Fail = Failure.None;
            algorithm.EmbedBytes(payload, audio);
            Assert.Same(samples, audio.Samples);
            Assert.Equal(payload, algorithm.ExtractBytes(audio));
            Assert.NotEqual(before, audio.ToArray());
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(4, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        public void DisposeFailureAfterEmbeddingPreservesSamples(int bits, bool trellis)
        {
            var audio = Cover();
            byte[] before = audio.ToArray();
            var selector = new Selector { Fail = Failure.Dispose };
            var algorithm = new AudioLsb(selector) { BitsPerSample = bits, MaxTrellisWidth = 2 };
            if (trellis) algorithm.TrellisCoder = new SyndromeTrellisCoder(2);

            Assert.Same(selector.Error, Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(new byte[] { 0xA5 }, audio)));
            Assert.True(selector.Disposed);
            Assert.Equal(before, audio.ToArray());

            selector.Fail = Failure.None;
            algorithm.EmbedBytes(new byte[] { 0xA5 }, audio);
            Assert.Equal(new byte[] { 0xA5 }, algorithm.ExtractBytes(audio));
        }

        [Fact]
        public void PipelinePreservesSamplesWhenSelectorFailsAfterEnvelopeEmbedding()
        {
            var audio = new PcmAudio(44100, 1, 16, Enumerable.Repeat(15, 1024).ToArray());
            byte[] before = audio.ToArray();
            var selector = new Selector { Fail = Failure.Dispose };
            var pipeline = new StegoPipeline<PcmAudio>(new AudioLsb(selector));
            var key = StegoKey.FromBytes(new byte[] { 42 });
            Assert.Same(selector.Error, Assert.Throws<InvalidOperationException>(() => pipeline.Embed(new byte[] { 1, 2, 3 }, audio, key)));
            Assert.Equal(before, audio.ToArray());
        }

        [Fact]
        public void CostFailurePreservesSamples()
        {
            var audio = Cover();
            byte[] before = audio.ToArray();
            var algorithm = new AudioLsb(new Selector())
            {
                TrellisCoder = new SyndromeTrellisCoder(2),
                CostModel = new ThrowingCost(),
                MaxTrellisWidth = 2,
            };
            Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(new byte[] { 0xA5 }, audio));
            Assert.Equal(before, audio.ToArray());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void CallbacksSeeUntouchedSamplesUntilEmbeddingCompletes(int bits)
        {
            var audio = Cover();
            int[] original = (int[])audio.Samples.Clone();
            var selector = new Selector { Visit = () => Assert.Equal(original, audio.Samples) };
            var algorithm = new AudioLsb(selector) { BitsPerSample = bits };
            byte[] payload = { 0xA5, 0x5A };
            algorithm.EmbedBytes(payload, audio);
            Assert.NotEqual(original, audio.Samples);
            selector.Visit = null;
            Assert.Equal(payload, algorithm.ExtractBytes(audio));
        }

        [Fact]
        public void EmptyPayloadStillPreservesSamplesOnHeaderFailure()
        {
            var audio = Cover();
            byte[] before = audio.ToArray();
            var algorithm = new AudioLsb(new Selector { Fail = Failure.MoveNext, FailAfter = 32 });
            Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(Array.Empty<byte>(), audio));
            Assert.Equal(before, audio.ToArray());
        }
    }
}
