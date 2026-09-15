using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Audio;
using SteganoLib.Coding;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Selection;
using Xunit;

namespace SteganoLib.Test
{
    public class AlgorithmConfigurationTests
    {
        private static readonly byte[] Payload = { 0x5A, 0xF3 };

        private sealed class Pixels : IContentAwarePixelSelector
        {
            public Action OnCount { get; set; }
            public Action OnEnumeration { get; set; }
            public Action OnStableBits { get; set; }
            public Point? Invalid { get; set; }
            public int StableHighBits
            {
                get
                {
                    OnStableBits?.Invoke();
                    return 6;
                }
            }
            public long Count(int width, int height)
            {
                OnCount?.Invoke();
                return (long)width * height;
            }
            public long Count(Image<Rgba32> image) => Count(image.Width, image.Height);
            public IEnumerable<Point> PixelsOf(int width, int height)
            {
                OnEnumeration?.Invoke();
                if (Invalid is Point invalid) yield return invalid;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        yield return new Point(x, y);
            }
            IEnumerable<Point> IPixelSelector.Pixels(int width, int height) => PixelsOf(width, height);
            IEnumerable<Point> IContentAwarePixelSelector.Pixels(Image<Rgba32> image) => PixelsOf(image.Width, image.Height);
        }

        private sealed class Samples : ISampleSelector
        {
            public Action OnCount { get; set; }
            public Action OnEnumeration { get; set; }
            public long? Invalid { get; set; }
            public long Count(long count)
            {
                OnCount?.Invoke();
                return count;
            }
            public IEnumerable<long> Indices(long count)
            {
                OnEnumeration?.Invoke();
                if (Invalid is long invalid) yield return invalid;
                for (long i = 0; i < count; i++) yield return i;
            }
        }

        private sealed class Cost : IPixelCostModel, ISampleCostModel, ICoefficientCostModel
        {
            private readonly Action _callback;
            public int Calls { get; private set; }
            public Cost(Action callback = null) => _callback = callback;
            private double Value()
            {
                Calls++;
                _callback?.Invoke();
                return 1;
            }
            double IPixelCostModel.Cost(Image<Rgba32> image, int x, int y, int channel) => Value();
            double ISampleCostModel.Cost(PcmAudio audio, long index) => Value();
            double ICoefficientCostModel.Cost(short coefficient, int zigzagIndex) => Value();
        }

        private static Cost UnexpectedCost() => new(() => throw new InvalidOperationException("Replacement cost model was used."));
        private static LSB ImageAlgorithm(Pixels selector) => new(selector)
        {
            BitsPerPixel = 3,
            EmbeddingMode = LsbEmbeddingMode.Replace,
            MaxTrellisWidth = 3,
        };
        private static AudioLsb AudioAlgorithm(Samples selector) => new(selector) { BitsPerSample = 2, MaxTrellisWidth = 3 };

        private static void Change(LSB algorithm)
        {
            algorithm.Channels = ColorChannels.Red;
            algorithm.BitsPerPixel = 1;
            algorithm.EmbeddingMode = LsbEmbeddingMode.Match;
            algorithm.TrellisCoder = null;
            algorithm.MaxTrellisWidth = 1;
            algorithm.CostModel = UnexpectedCost();
        }

        private static void Change(AudioLsb algorithm)
        {
            algorithm.BitsPerSample = 1;
            algorithm.TrellisCoder = null;
            algorithm.MaxTrellisWidth = 1;
            algorithm.CostModel = UnexpectedCost();
        }

        [Theory]
        [InlineData("count", false)]
        [InlineData("enumeration", false)]
        [InlineData("stable", false)]
        [InlineData("count", true)]
        [InlineData("enumeration", true)]
        [InlineData("stable", true)]
        public void ImageEmbeddingUsesInitialSettingsThroughSelectorCallbacks(string callback, bool trellis)
        {
            var selector = new Pixels();
            var algorithm = ImageAlgorithm(selector);
            var reference = ImageAlgorithm(new Pixels());
            algorithm.CostModel = reference.CostModel = new Cost();
            if (trellis) algorithm.TrellisCoder = reference.TrellisCoder = new SyndromeTrellisCoder(2);
            Action change = () => Change(algorithm);
            if (callback == "count") selector.OnCount = change;
            if (callback == "enumeration") selector.OnEnumeration = change;
            if (callback == "stable") selector.OnStableBits = change;
            using var image = new Image<Rgba32>(16, 16, new Rgba32(122, 122, 122));
            using var expected = image.Clone();
            reference.EmbedBytes(Payload, expected);
            algorithm.EmbedBytes(Payload, image);
            Assert.Equal(Payload, reference.ExtractBytes(image));
            AssertImagesEqual(expected, image);
            Assert.Equal(ColorChannels.Red, algorithm.Channels); // Changed settings remain available for later operations.
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void AudioEmbeddingUsesInitialSettingsThroughSelectorCallbacks(bool enumeration, bool trellis)
        {
            var selector = new Samples();
            var algorithm = AudioAlgorithm(selector);
            var reference = AudioAlgorithm(new Samples());
            algorithm.CostModel = reference.CostModel = new Cost();
            if (trellis) algorithm.TrellisCoder = reference.TrellisCoder = new SyndromeTrellisCoder(2);
            if (enumeration) selector.OnEnumeration = () => Change(algorithm);
            else selector.OnCount = () => Change(algorithm);
            var audio = PcmAudioTests.Synthetic(256, 1, 16);
            var expected = audio.Clone();
            reference.EmbedBytes(Payload, expected);
            algorithm.EmbedBytes(Payload, audio);
            Assert.Equal(Payload, reference.ExtractBytes(audio));
            Assert.Equal(expected.Samples, audio.Samples);
            Assert.Equal(1, algorithm.BitsPerSample);
        }

        [Fact]
        public void CapacityUsesSettingsFromBeforeCountCallbacks()
        {
            var pixels = new Pixels();
            var imageAlgorithm = ImageAlgorithm(pixels);
            pixels.OnCount = () => Change(imageAlgorithm);
            using var image = new Image<Rgba32>(16, 16);
            Assert.Equal(90, imageAlgorithm.Capacity(image));
            Assert.Equal(26, imageAlgorithm.Capacity(image));

            var samples = new Samples();
            var audioAlgorithm = AudioAlgorithm(samples);
            samples.OnCount = () => Change(audioAlgorithm);
            var audio = PcmAudioTests.Synthetic(256, 1, 16);
            Assert.Equal(58, audioAlgorithm.Capacity(audio));
            Assert.Equal(26, audioAlgorithm.Capacity(audio));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExtractionUsesInitialSettingsThroughCountCallbacks(bool audio)
        {
            if (audio)
            {
                var carrier = PcmAudioTests.Synthetic(256, 1, 16);
                AudioAlgorithm(new Samples()).EmbedBytes(Payload, carrier);
                var selector = new Samples();
                var algorithm = AudioAlgorithm(selector);
                selector.OnCount = () => Change(algorithm);
                Assert.Equal(Payload, algorithm.ExtractBytes(carrier));
            }
            else
            {
                using var carrier = new Image<Rgba32>(16, 16);
                ImageAlgorithm(new Pixels()).EmbedBytes(Payload, carrier);
                var selector = new Pixels();
                var algorithm = ImageAlgorithm(selector);
                selector.OnCount = () => Change(algorithm);
                Assert.Equal(Payload, algorithm.ExtractBytes(carrier));
            }
        }

        [Fact]
        public void ImageTrellisRetainsItsOriginalCostModelAndSettings()
        {
            var algorithm = ImageAlgorithm(new Pixels());
            algorithm.TrellisCoder = new SyndromeTrellisCoder(2);
            var cost = new Cost(() => Change(algorithm));
            algorithm.CostModel = cost;
            using var image = new Image<Rgba32>(16, 16);
            algorithm.EmbedBytes(Payload, image);
            Assert.Equal(Payload, ImageAlgorithm(new Pixels()).ExtractBytes(image));
            Assert.Equal(48, cost.Calls);
        }

        [Fact]
        public void AudioTrellisRetainsItsOriginalCostModelAndSettings()
        {
            var algorithm = AudioAlgorithm(new Samples());
            algorithm.TrellisCoder = new SyndromeTrellisCoder(2);
            var cost = new Cost(() => Change(algorithm));
            algorithm.CostModel = cost;
            var audio = PcmAudioTests.Synthetic(256, 1, 16);
            algorithm.EmbedBytes(Payload, audio);
            Assert.Equal(Payload, AudioAlgorithm(new Samples()).ExtractBytes(audio));
            Assert.Equal(48, cost.Calls);
        }

        [Fact]
        public void F5TrellisRetainsItsCoderAndCostModelAfterCallbacks()
        {
            var key = StegoKey.FromBytes(new byte[] { 42 });
            var algorithm = new F5(key) { TrellisCoder = new SyndromeTrellisCoder(2), MaxTrellisWidth = 3 };
            var cost = new Cost(() =>
            {
                algorithm.TrellisCoder = null;
                algorithm.MaxTrellisWidth = 1;
                algorithm.CostModel = UnexpectedCost();
            });
            algorithm.CostModel = cost;
            var image = JpegImage.Load(JpegImageTests.SampleJpeg(16, 16));
            foreach (var component in image.Components)
                for (int i = 0; i < component.Coefficients.Length; i++)
                    if (i % 64 != 0) component.Coefficients[i] = 4;
            algorithm.EmbedBytes(Payload, image);
            Assert.Equal(Payload, new F5(key).ExtractBytes(image));
            Assert.Equal(48, cost.Calls);
        }

        [Theory]
        [InlineData(16, 0)]
        [InlineData(-1, 1)]
        [InlineData(0, -1)]
        [InlineData(0, 16)]
        [InlineData(int.MaxValue, int.MaxValue)]
        public void InvalidPixelCoordinatesCannotAliasValidPixels(int x, int y)
        {
            using var image = new Image<Rgba32>(16, 16);
            using var before = image.Clone();
            var algorithm = ImageAlgorithm(new Pixels { Invalid = new Point(x, y) });
            Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(Payload, image));
            Assert.Throws<InvalidOperationException>(() => algorithm.ExtractBytes(image));
            AssertImagesEqual(before, image);
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(256L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void InvalidSampleIndicesAreRejectedBeforeAccess(long index)
        {
            var audio = PcmAudioTests.Synthetic(256, 1, 16);
            var before = audio.Clone();
            var algorithm = AudioAlgorithm(new Samples { Invalid = index });
            Assert.Throws<InvalidOperationException>(() => algorithm.EmbedBytes(Payload, audio));
            Assert.Throws<InvalidOperationException>(() => algorithm.ExtractBytes(audio));
            Assert.Equal(before.Samples, audio.Samples);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        [InlineData(int.MaxValue)]
        public void InvalidEmbeddingModePreservesPreviousConfiguration(int value)
        {
            var algorithm = ImageAlgorithm(new Pixels());
            Assert.Throws<ArgumentOutOfRangeException>(() => algorithm.EmbeddingMode = (LsbEmbeddingMode)value);
            Assert.Equal(LsbEmbeddingMode.Replace, algorithm.EmbeddingMode);
        }

        private static void AssertImagesEqual(Image<Rgba32> expected, Image<Rgba32> actual)
        {
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert.Equal(expected[x, y], actual[x, y]);
        }
    }
}
