#nullable enable

using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Jpeg;
using SteganoLib.Video;
using Xunit;

namespace SteganoLib.Test
{
    public class AviOwnershipTests
    {
        private static AviVideo Empty(bool mjpeg) => mjpeg ? AviVideo.CreateMjpeg(16, 16) : AviVideo.CreateRgb(16, 16);

        private static byte[] FrameBytes(bool mjpeg) => mjpeg ? JpegImageTests.SampleJpeg(16, 16) : new byte[16 * 16 * 3];

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void RawFrameWrites_CopyCallerBuffer(bool mjpeg, bool replace)
        {
            var video = Empty(mjpeg);
            byte[] input = FrameBytes(mjpeg);
            byte[] expected = (byte[])input.Clone();
            if (replace)
            {
                video.AddFrame(FrameBytes(mjpeg));
                video.SetFrameData(0, input);
            }
            else
                video.AddFrame(input);
            byte[] before = video.ToArray();

            Array.Fill(input, (byte)42);

            Assert.Equal(before, video.ToArray());
            Assert.Equal(expected, video.GetFrameData(0));
            Assert.Equal(expected, AviVideo.Load(video.ToArray()).GetFrameData(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RawFrameReads_ReturnIndependentBuffers(bool mjpeg)
        {
            var video = Empty(mjpeg);
            video.AddFrame(FrameBytes(mjpeg));
            byte[] before = video.ToArray();
            byte[] first = video.GetFrameData(0);
            byte[] second = video.GetFrameData(0);
            Assert.NotSame(first, second);

            first[0] ^= 255;

            Assert.Equal(before, video.ToArray());
            Assert.Equal(second, video.GetFrameData(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Load_DoesNotRetainSourceContainer(bool mjpeg)
        {
            var original = Empty(mjpeg);
            original.AddFrame(FrameBytes(mjpeg));
            byte[] input = original.ToArray();
            var loaded = AviVideo.Load(input);
            byte[] before = loaded.ToArray();

            Array.Fill(input, (byte)0);

            Assert.Equal(before, loaded.ToArray());
            Assert.Equal(original.GetFrameData(0), loaded.GetFrameData(0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReusingRawInputDoesNotCoupleFrames_ExplicitReplacementStillWorks(bool mjpeg)
        {
            var video = Empty(mjpeg);
            byte[] input = FrameBytes(mjpeg);
            byte[] expected = (byte[])input.Clone();
            video.AddFrame(input);
            video.AddFrame(input);
            byte[] replacement = mjpeg ? JpegImageTests.SampleJpeg(16, 16, seed: 9) : new byte[input.Length];
            if (!mjpeg) Array.Fill(replacement, (byte)99);

            video.SetFrameData(0, replacement);
            input[0] ^= 255;

            Assert.Equal(replacement, video.GetFrameData(0));
            Assert.Equal(expected, video.GetFrameData(1));
        }

        [Theory]
        [InlineData(false, 8, 16)]
        [InlineData(false, 16, 8)]
        [InlineData(true, 8, 16)]
        [InlineData(true, 16, 8)]
        public void TypedJpeg_RejectsWrongDimensionsBeforeChangingVideo(bool replace, int width, int height)
        {
            var video = Empty(true);
            video.AddFrame(FrameBytes(true));
            byte[] before = video.ToArray();
            var image = JpegImage.Load(JpegImageTests.SampleJpeg(width, height));

            var error = Assert.Throws<ArgumentException>(() =>
            {
                if (replace) video.EncodeJpegFrame(0, image);
                else video.AddFrame(image);
            });

            Assert.Equal("image", error.ParamName);
            Assert.Equal(1, video.FrameCount);
            Assert.Equal(before, video.ToArray());
            var valid = JpegImage.Load(FrameBytes(true));
            if (replace) video.EncodeJpegFrame(0, valid);
            else video.AddFrame(valid);
            Assert.Equal(replace ? 1 : 2, AviVideo.Load(video.ToArray()).FrameCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TypedJpeg_RejectsRgbContainerBeforeChangingVideo(bool replace)
        {
            var video = Empty(false);
            video.AddFrame(FrameBytes(false));
            byte[] before = video.ToArray();
            var image = JpegImage.Load(FrameBytes(true));

            Assert.Throws<InvalidOperationException>(() =>
            {
                if (replace) video.EncodeJpegFrame(0, image);
                else video.AddFrame(image);
            });

            Assert.Equal(before, video.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TypedJpeg_NullInputLeavesVideoIntact(bool replace)
        {
            var video = Empty(true);
            video.AddFrame(FrameBytes(true));
            byte[] before = video.ToArray();

            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                if (replace) video.EncodeJpegFrame(0, null!);
                else video.AddFrame((JpegImage)null!);
            });

            Assert.Equal("image", error.ParamName);
            Assert.Equal(before, video.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TypedReplacement_ValidatesIndexBeforeEncoding(bool mjpeg)
        {
            var video = Empty(mjpeg);
            byte[] before = video.ToArray();

            var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                if (mjpeg) video.EncodeJpegFrame(0, null!);
                else video.EncodeFrame(0, null!);
            });

            Assert.Equal("index", error.ParamName);
            Assert.Equal(before, video.ToArray());
        }

        [Fact]
        public void TypedJpegFrames_AreIndependentOfInputAndDecodedImages()
        {
            var video = Empty(true);
            var image = JpegImage.Load(FrameBytes(true));
            video.AddFrame(image);
            byte[] first = video.GetFrameData(0);
            image.Components[0].Coefficients[0]++;
            video.AddFrame(image);
            byte[] second = video.GetFrameData(1);
            Assert.NotEqual(first, second);
            Assert.Equal(first, video.GetFrameData(0));

            video.EncodeJpegFrame(0, image);
            image.Components[0].Coefficients[0]++;
            var decoded = video.DecodeJpegFrame(0);
            decoded.Components[0].Coefficients[0]++;

            Assert.Equal(second, video.GetFrameData(0));
            Assert.Equal(second, video.GetFrameData(1));
            Assert.Equal(2, AviVideo.Load(video.ToArray()).FrameCount);
        }

        [Fact]
        public void TypedRgbFrames_AreIndependentOfInputAndDecodedImages()
        {
            var video = Empty(false);
            using var image = new Image<Rgba32>(16, 16, new Rgba32(10, 20, 30));
            video.AddFrame(image);
            byte[] first = video.GetFrameData(0);
            image[0, 0] = new Rgba32(40, 50, 60);
            video.AddFrame(image);
            byte[] second = video.GetFrameData(1);
            Assert.NotEqual(first, second);
            Assert.Equal(first, video.GetFrameData(0));

            video.EncodeFrame(0, image);
            image[0, 0] = new Rgba32(70, 80, 90);
            using var decoded = video.DecodeFrame(0);
            decoded[0, 0] = new Rgba32(100, 110, 120);

            Assert.Equal(second, video.GetFrameData(0));
            Assert.Equal(second, video.GetFrameData(1));
        }

        [Fact]
        public void MjpegSequence_FailedCallbackDoesNotReplaceStoredFrame()
        {
            var video = Empty(true);
            video.AddFrame(FrameBytes(true));
            byte[] before = video.ToArray();
            var failure = new InvalidOperationException("Callback failed.");

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => video.JpegFrames.Modify(0, frame =>
            {
                frame.Components[0].Coefficients[0]++;
                throw failure;
            })));

            Assert.Equal(before, video.ToArray());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RawMjpeg_PreservesBytesAndValidatesContentsOnDecode(bool wrongDimensions)
        {
            var video = Empty(true);
            byte[] input = wrongDimensions ? JpegImageTests.SampleJpeg(8, 8) : new byte[] { 255, 216, 255 };
            video.AddFrame(input);
            var loaded = AviVideo.Load(video.ToArray());

            Assert.Equal(input, loaded.GetFrameData(0));
            Assert.Throws<InvalidDataException>(() => loaded.DecodeJpegFrame(0));
        }
    }
}
