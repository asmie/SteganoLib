#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Selection;
using SteganoLib.Video;
using Xunit;

namespace SteganoLib.Test
{
    public class VideoContractTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void FrameList_AllAccessPathsValidateIndexBeforeCallbacks(int index)
        {
            var frames = new FrameList<Frame>(new[] { new Frame() });
            bool invoked = false;

            Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => frames[index]).ParamName);
            Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => frames.Read(index, _ => invoked = true)).ParamName);
            Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => frames.Modify(index, _ => invoked = true)).ParamName);
            Assert.False(invoked);
        }

        [Fact]
        public void FrameList_LiveViewRejectsNullFramesThroughEveryAccessPath()
        {
            var input = new[] { new Frame() };
            var frames = new FrameList<Frame>(input);
            Assert.Same(input[0], frames[0]);
            input[0] = null!;
            bool invoked = false;

            Assert.Throws<InvalidOperationException>(() => frames[0]);
            Assert.Throws<InvalidOperationException>(() => frames.Read(0, _ => invoked = true));
            Assert.Throws<InvalidOperationException>(() => frames.Modify(0, _ => invoked = true));
            Assert.False(invoked);
        }

        [Fact]
        public void ImageSequence_CopiesPathListAndPreventsMutationThroughItsView()
        {
            var input = new[] { "first.png" };
            var sequence = new ImageSequence(input);
            input[0] = "replacement.jpg";

            Assert.Equal("first.png", sequence.Paths[0]);
            var list = Assert.IsAssignableFrom<IList<string>>(sequence.Paths);
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list[0] = "replacement.jpg");
            Assert.Throws<NotSupportedException>(() => list.Clear());
            Assert.Equal(1, sequence.Count);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Coding_CapturesSpreadBeforeCapacityCallbacks(bool spread, bool embed)
        {
            var inner = new FrameAlgorithm();
            var coder = new VideoCoding<Frame>(inner) { Spread = spread };
            inner.OnCapacity = () => coder.Spread = !spread;
            var frames = new FrameList<Frame>(new[] { new Frame(), new Frame() });
            byte[] data = { 10, 20, 30, 40 };

            if (embed)
            {
                coder.EmbedBytes(data, frames);
                Assert.Equal(data, coder.ExtractBytes(frames));
                Assert.Equal(spread ? 4 : 0, frames[1].Payload.Length);
            }
            else
                Assert.True(coder.IsPossibleToEmbed(data.Length, frames));

            Assert.Equal(spread ? new long[] { 4, 4 } : new long[] { 8 }, inner.PieceLengths);
            Assert.Equal(!spread, coder.Spread);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Coding_HugeAggregateCapacityStillEmbedsSmallPayloads(bool spread)
        {
            var inner = new FrameAlgorithm();
            var coder = new VideoCoding<Frame>(inner) { Spread = spread };
            var frames = new FrameList<Frame>(new[]
            {
                new Frame { Available = long.MaxValue }, new Frame { Available = long.MaxValue }
            });
            byte[] data = { 10, 20, 30, 40 };

            Assert.Equal(long.MaxValue, coder.Capacity(frames));
            Assert.True(coder.IsPossibleToEmbed(data.Length, frames));
            coder.EmbedBytes(data, frames);

            Assert.Equal(data, coder.ExtractBytes(frames));
            Assert.Equal(spread ? 4 : 8, frames[0].Payload.Length);
            Assert.Equal(spread ? 4 : 0, frames[1].Payload.Length);
        }

        [Fact]
        public void Coding_ProportionalFeasibilityUsesExactArithmeticAtLongBoundary()
        {
            var inner = new FrameAlgorithm();
            var coder = new VideoCoding<Frame>(inner);
            var frames = new FrameList<Frame>(new[]
            {
                new Frame { Available = long.MaxValue }, new Frame { Available = long.MaxValue }
            });

            Assert.True(coder.IsPossibleToEmbed(long.MaxValue - 4, frames));

            Assert.Equal(new[] { long.MaxValue / 2 + 1, long.MaxValue / 2 }, inner.PieceLengths);
            Assert.False(coder.IsPossibleToEmbed(long.MaxValue - 3, frames));
        }

        [Theory]
        [InlineData(0L, long.MaxValue - 4)]
        [InlineData(2L, long.MaxValue - 2)]
        [InlineData(4L, long.MaxValue)]
        public void Coding_SubtractsHeaderBeforeClampingCapacity(long extra, long expected)
        {
            var frames = new FrameList<Frame>(new[]
            {
                new Frame { Available = long.MaxValue }, new Frame { Available = extra }
            });

            Assert.Equal(expected, new VideoCoding<Frame>(new FrameAlgorithm()).Capacity(frames));
        }

        [Fact]
        public void Coding_ProportionalRemainderGoesToFirstFramesWithRoom()
        {
            var coder = new VideoCoding<Frame>(new FrameAlgorithm());
            var frames = new FrameList<Frame>(new[]
            {
                new Frame { Available = 3 }, new Frame { Available = 7 }, new Frame { Available = 11 }
            });
            byte[] data = { 10, 20, 30, 40, 50 };

            coder.EmbedBytes(data, frames);

            Assert.Equal(new[] { 2, 3, 4 }, Enumerable.Range(0, frames.Count).Select(i => frames[i].Payload.Length));
            Assert.Equal(data, coder.ExtractBytes(frames));
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        public void Coding_RejectsNegativeCapacityBeforeAnyEmbedding(long capacity)
        {
            var coder = new VideoCoding<Frame>(new FrameAlgorithm());
            var first = new Frame();
            var frames = new FrameList<Frame>(new[] { first, new Frame { Available = capacity } });

            Assert.Throws<InvalidOperationException>(() => coder.Capacity(frames));
            Assert.Throws<InvalidOperationException>(() => coder.IsPossibleToEmbed(0, frames));
            Assert.Throws<InvalidOperationException>(() => coder.EmbedBytes(Array.Empty<byte>(), frames));
            Assert.Empty(first.Payload);
        }

        [Fact]
        public void Coding_RejectsNegativeFrameCountBeforeReadingOrModifying()
        {
            var coder = new VideoCoding<Frame>(new FrameAlgorithm());
            var frames = new NegativeCountSequence();
            Assert.Throws<InvalidOperationException>(() => coder.Capacity(frames));
            Assert.Throws<InvalidOperationException>(() => coder.IsPossibleToEmbed(0, frames));
            Assert.Throws<InvalidOperationException>(() => coder.EmbedBytes(Array.Empty<byte>(), frames));
            Assert.Throws<InvalidOperationException>(() => coder.ExtractBytes(frames));
        }

        [Fact]
        public void Coding_RejectsNullExtractedPayload()
        {
            var coder = new VideoCoding<Frame>(new FrameAlgorithm { ReturnNull = true });
            Assert.Throws<InvalidOperationException>(() => coder.ExtractBytes(new FrameList<Frame>(new[] { new Frame() })));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Coding_FailedEmbeddingKeepsEarlierChangesAndLeavesLaterFramesUntouched(bool spread)
        {
            var failure = new InvalidOperationException("Embedding failed.");
            var frames = new FrameList<Frame>(new[] { new Frame(), new Frame(), new Frame() });
            var coder = new VideoCoding<Frame>(new FrameAlgorithm { FailOn = frames[1], Failure = failure }) { Spread = spread };

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => coder.EmbedBytes(new byte[13], frames)));

            Assert.NotEmpty(frames[0].Payload);
            Assert.NotEmpty(frames[1].Payload); // FrameList callbacks operate directly on the supplied objects.
            Assert.Empty(frames[2].Payload);
        }

        [Fact]
        public void AviSequence_FailedCallbackDoesNotReplaceStoredFrame()
        {
            var video = AviVideo.CreateRgb(2, 2);
            using var original = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30));
            video.AddFrame(original);
            var before = video.ToArray();
            var failure = new InvalidOperationException("Callback failed.");

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => video.RgbFrames.Modify(0, frame =>
            {
                frame[0, 0] = new Rgba32(99, 88, 77);
                throw failure;
            })));

            Assert.Equal(before, video.ToArray());
        }

        [Theory]
        [InlineData(0, "data")]
        [InlineData(1, "data")]
        [InlineData(2, "data")]
        [InlineData(3, "data")]
        [InlineData(4, "key")]
        [InlineData(5, "key")]
        [InlineData(6, "key")]
        [InlineData(7, "key")]
        public void FileHelpers_RejectNullPayloadOrKeyBeforeOpeningInput(int operation, string parameter)
        {
            var key = StegoKey.FromBytes(new byte[] { 1 });
            var rgb = new VideoCoding<Image<Rgba32>>(new Algorithms.LSB(new KeyedPermutationSelector(key)));
            var jpeg = new VideoCoding<JpegImage>(new F5(key));
            var rgbPipeline = new StegoPipeline<IFrameSequence<Image<Rgba32>>>(rgb);
            var jpegPipeline = new StegoPipeline<IFrameSequence<JpegImage>>(jpeg);
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string input = Path.Combine(directory, "missing.avi");
            string output = Path.Combine(directory, "output.avi");
            Action[] actions =
            {
                () => rgb.EmbedBytes(null!, input, output),
                () => jpeg.EmbedBytes(null!, input, output),
                () => rgbPipeline.Embed(null!, input, output, key),
                () => jpegPipeline.Embed(null!, input, output, key),
                () => rgbPipeline.Embed(Array.Empty<byte>(), input, output, null!),
                () => jpegPipeline.Embed(Array.Empty<byte>(), input, output, null!),
                () => rgbPipeline.Extract(input, null!),
                () => jpegPipeline.Extract(input, null!)
            };

            Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(actions[operation]).ParamName);
            Assert.False(Directory.Exists(directory));
        }

        private sealed class Frame
        {
            public long Available { get; init; } = 8;
            public byte[] Payload { get; set; } = Array.Empty<byte>();
        }

        private sealed class FrameAlgorithm : IStegAlgorithm<Frame>
        {
            public Action? OnCapacity { get; set; }
            public bool ReturnNull { get; init; }
            public Frame? FailOn { get; init; }
            public InvalidOperationException? Failure { get; init; }
            public List<long> PieceLengths { get; } = new();

            public long Capacity(Frame frame)
            {
                OnCapacity?.Invoke();
                return frame.Available;
            }

            public bool IsPossibleToEmbed(long length, Frame frame)
            {
                PieceLengths.Add(length);
                return length >= 0 && length <= frame.Available;
            }

            public void EmbedBytes(byte[] data, Frame frame)
            {
                frame.Payload = (byte[])data.Clone();
                if (ReferenceEquals(frame, FailOn))
                    throw Failure!;
            }

            public byte[] ExtractBytes(Frame frame) => ReturnNull ? null! : (byte[])frame.Payload.Clone();
        }

        private sealed class NegativeCountSequence : IFrameSequence<Frame>
        {
            public int Count => -1;
            public TResult Read<TResult>(int index, Func<Frame, TResult> reader) => throw new Exception("Must not read frames.");
            public void Modify(int index, Action<Frame> action) => throw new Exception("Must not modify frames.");
        }
    }
}
