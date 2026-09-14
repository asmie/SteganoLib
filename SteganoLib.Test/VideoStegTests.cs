using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Algorithms;
using SteganoLib.Crypto;
using SteganoLib.Jpeg;
using SteganoLib.Payload;
using SteganoLib.Selection;
using SteganoLib.Video;
using Xunit;

namespace SteganoLib.Test
{
    public class VideoStegTests
    {
        private static readonly StegoKey Key = StegoKey.FromBytes(new byte[] { 0x11 });
        private static readonly StegoKey OtherKey = StegoKey.FromBytes(new byte[] { 0x12 });

        private static byte[] Random(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        private static Algorithms.LSB Lsb() => new(new KeyedPermutationSelector(Key));

        private static AviVideo RgbVideo(int frames = 6, int width = 64, int height = 48, int bitCount = 24)
        {
            var video = AviVideo.CreateRgb(width, height, bitCount, 30, 1);
            for (int i = 0; i < frames; i++)
            {
                using var picture = JpegImageTests.TestPicture(width, height, 100 + i);
                if (bitCount == 32)
                {
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            picture[x, y] = picture[x, y] with { A = (byte)((x * 7 + y * 3) & 0xFF) };
                }
                video.AddFrame(picture);
            }
            return video;
        }

        private static AviVideo MjpegVideo(int frames = 4, int width = 128, int height = 96)
        {
            var video = AviVideo.CreateMjpeg(width, height, 24000, 1001);
            for (int i = 0; i < frames; i++)
            {
                using var picture = JpegImageTests.TestPicture(width, height, 200 + i);
                video.AddFrame(JpegImageTests.EncodeWithImageSharp(picture, JpegEncodingColor.YCbCrRatio420, 85));
            }
            return video;
        }

        private static void AssertSamePixels(Image<Rgba32> expected, Image<Rgba32> actual)
        {
            Assert.Equal(expected.Width, actual.Width);
            Assert.Equal(expected.Height, actual.Height);
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert.True(expected[x, y] == actual[x, y], $"Pixel ({x},{y}) differs: {expected[x, y]} vs {actual[x, y]}");
        }

        /// <summary>Walk a RIFF body and return (id, body offset, size) of every chunk.</summary>
        private static List<(string Id, int Offset, int Size)> Chunks(byte[] data, int start, int end)
        {
            var list = new List<(string, int, int)>();
            int pos = start;
            while (pos + 8 <= end)
            {
                string id = Encoding.ASCII.GetString(data, pos, 4);
                int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));
                list.Add((id, pos + 8, size));
                pos += 8 + size + (size & 1);
            }
            return list;
        }

        private static (string Id, int Offset, int Size) TopLevel(byte[] avi, string id, string listType = null)
        {
            foreach (var chunk in Chunks(avi, 12, avi.Length))
            {
                if (chunk.Id != id)
                    continue;
                if (listType == null || Encoding.ASCII.GetString(avi, chunk.Offset, 4) == listType)
                    return chunk;
            }
            throw new InvalidOperationException($"{id} {listType} not found");
        }

        // ---------- AVI container ----------

        [Theory]
        [InlineData(24)]
        [InlineData(32)]
        public void Avi_Rgb_SaveLoad_RoundTrip(int bitCount)
        {
            var video = RgbVideo(5, 37, 23, bitCount);
            var bytes = video.ToArray();

            Assert.True(AviVideo.IsAvi(bytes));
            var loaded = AviVideo.Load(bytes);
            Assert.Equal(5, loaded.FrameCount);
            Assert.Equal(37, loaded.Width);
            Assert.Equal(23, loaded.Height);
            Assert.Equal(bitCount, loaded.BitCount);
            Assert.Equal(AviCodec.Rgb, loaded.Codec);
            Assert.Equal(30.0, loaded.FrameRate);
            Assert.False(loaded.TopDown);
            Assert.Equal((37 * bitCount / 8 + 3) & ~3, loaded.Stride);
            for (int i = 0; i < 5; i++)
            {
                using var expected = video.DecodeFrame(i);
                using var actual = loaded.DecodeFrame(i);
                AssertSamePixels(expected, actual);
                Assert.Equal(video.GetFrameData(i), loaded.GetFrameData(i));
            }
            Assert.Equal(bytes, loaded.ToArray());
        }

        [Fact]
        public void Avi_Rgb_FramesAreBottomUpBgr()
        {
            var video = AviVideo.CreateRgb(2, 2);
            using var picture = new Image<Rgba32>(2, 2);
            picture[0, 0] = new Rgba32(1, 2, 3);
            picture[1, 0] = new Rgba32(4, 5, 6);
            picture[0, 1] = new Rgba32(7, 8, 9);
            picture[1, 1] = new Rgba32(10, 11, 12);
            video.AddFrame(picture);

            // Bottom row first, each pixel B G R, rows padded to 4 bytes (6 -> 8).
            Assert.Equal(new byte[] { 9, 8, 7, 12, 11, 10, 0, 0, 3, 2, 1, 6, 5, 4, 0, 0 }, video.GetFrameData(0));
            using var back = video.DecodeFrame(0);
            AssertSamePixels(picture, back);
        }

        [Fact]
        public void Avi_Mjpeg_SaveLoad_RoundTrip()
        {
            var video = MjpegVideo();
            var bytes = video.ToArray();

            var loaded = AviVideo.Load(bytes);
            Assert.Equal(AviCodec.Mjpeg, loaded.Codec);
            Assert.Equal(4, loaded.FrameCount);
            Assert.Equal(24000.0 / 1001, loaded.FrameRate, 6);
            Assert.Equal(TimeSpan.FromSeconds(4 * 1001 / 24000.0).TotalSeconds, loaded.Duration.TotalSeconds, 6);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(video.GetFrameData(i), loaded.GetFrameData(i));
                var jpeg = loaded.DecodeJpegFrame(i);
                Assert.Equal(128, jpeg.Width);
                Assert.Equal(96, jpeg.Height);
            }
            Assert.Equal(bytes, loaded.ToArray());
            Assert.Throws<InvalidOperationException>(() => loaded.RgbFrames);
            Assert.Throws<InvalidOperationException>(() => loaded.DecodeFrame(0));
        }

        [Fact]
        public void Avi_Index_PointsAtEveryChunk()
        {
            var avi = RgbVideo(3, 16, 8).ToArray();

            var movi = TopLevel(avi, "LIST", "movi");
            var idx1 = TopLevel(avi, "idx1");
            var chunks = Chunks(avi, movi.Offset + 4, movi.Offset + movi.Size);
            Assert.Equal(3, chunks.Count);
            Assert.Equal(16 * chunks.Count, idx1.Size);

            for (int i = 0; i < chunks.Count; i++)
            {
                int entry = idx1.Offset + 16 * i;
                string id = Encoding.ASCII.GetString(avi, entry, 4);
                int offset = BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(entry + 8));
                int size = BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(entry + 12));

                Assert.Equal("00db", id);
                Assert.Equal(chunks[i].Id, Encoding.ASCII.GetString(avi, movi.Offset + offset, 4));
                Assert.Equal(chunks[i].Size, size);
            }

            var hdrl = TopLevel(avi, "LIST", "hdrl");
            var avih = Chunks(avi, hdrl.Offset + 4, hdrl.Offset + hdrl.Size).First(c => c.Id == "avih");
            Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(avih.Offset + 16)));
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(avih.Offset + 24)));
            Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(avih.Offset + 32)));
            Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(avih.Offset + 36)));
            Assert.Equal(avi.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(avi.AsSpan(4)));
        }

        /// <summary>A hand-built AVI: JUNK, an INFO list, an audio stream before the video stream, top-down 2x2 frames, and interleaved audio chunks.</summary>
        private static byte[] HandBuiltAvi()
        {
            var strhAudio = new byte[56];
            Encoding.ASCII.GetBytes("auds").CopyTo(strhAudio, 0);
            var strfAudio = new byte[] { 1, 0, 1, 0, 0x40, 0x1F, 0, 0, 0x40, 0x1F, 0, 0, 1, 0, 8, 0 };
            var strhVideo = new byte[56];
            Encoding.ASCII.GetBytes("vids").CopyTo(strhVideo, 0);
            Encoding.ASCII.GetBytes("DIB ").CopyTo(strhVideo, 4);
            BinaryPrimitives.WriteInt32LittleEndian(strhVideo.AsSpan(20), 1);
            BinaryPrimitives.WriteInt32LittleEndian(strhVideo.AsSpan(24), 10);
            var strfVideo = new byte[40];
            BinaryPrimitives.WriteInt32LittleEndian(strfVideo, 40);
            BinaryPrimitives.WriteInt32LittleEndian(strfVideo.AsSpan(4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(strfVideo.AsSpan(8), -2); // top-down
            BinaryPrimitives.WriteUInt16LittleEndian(strfVideo.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(strfVideo.AsSpan(14), 24);

            byte[] Chunk(string id, byte[] body)
            {
                var result = new byte[8 + body.Length + (body.Length & 1)];
                Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), body.Length);
                body.CopyTo(result, 8);
                return result;
            }
            byte[] List(string type, params byte[][] parts) => Chunk("LIST", Encoding.ASCII.GetBytes(type).Concat(parts.SelectMany(p => p)).ToArray());

            var avih = new byte[56];
            BinaryPrimitives.WriteInt32LittleEndian(avih.AsSpan(24), 2);
            var hdrl = List("hdrl",
                Chunk("avih", avih),
                List("strl", Chunk("strh", strhAudio), Chunk("strf", strfAudio), Chunk("strn", Encoding.ASCII.GetBytes("audio\0"))),
                List("strl", Chunk("strh", strhVideo), Chunk("strf", strfVideo)),
                Chunk("JUNK", new byte[7]));

            // Two 2x2 top-down frames; row 0 first. Stride 8.
            var frame0 = new byte[] { 3, 2, 1, 6, 5, 4, 0, 0, 9, 8, 7, 12, 11, 10, 0, 0 };
            var frame1 = frame0.Select(b => (byte)(b + 100)).ToArray();
            var movi = List("movi",
                Chunk("00wb", new byte[] { 0x80, 0x81, 0x82 }),
                Chunk("01dc", frame0),
                Chunk("00wb", new byte[] { 0x83, 0x84 }),
                List("rec ", Chunk("01db", frame1), Chunk("00wb", new byte[] { 0x85 })));

            var body = new byte[0]
                .Concat(Encoding.ASCII.GetBytes("AVI "))
                .Concat(hdrl)
                .Concat(Chunk("JUNK", new byte[12]))
                .Concat(List("INFO", Chunk("ISFT", Encoding.ASCII.GetBytes("handmade\0"))))
                .Concat(movi)
                .ToArray();
            return Chunk("RIFF", body);
        }

        [Fact]
        public void Avi_PreservesOtherStreams_ReadsTopDownAndSecondStream()
        {
            var video = AviVideo.Load(HandBuiltAvi());

            Assert.Equal(1, video.VideoStreamIndex);
            Assert.True(video.TopDown);
            Assert.Equal(2, video.FrameCount);
            Assert.Equal(10.0, video.FrameRate);
            using (var frame = video.DecodeFrame(0))
            {
                Assert.Equal(new Rgba32(1, 2, 3), frame[0, 0]);
                Assert.Equal(new Rgba32(4, 5, 6), frame[1, 0]);
                Assert.Equal(new Rgba32(7, 8, 9), frame[0, 1]);
                Assert.Equal(new Rgba32(10, 11, 12), frame[1, 1]);
            }

            // Modify a frame and write back: audio chunks, order, extra headers all survive.
            using (var frame = video.DecodeFrame(1))
            {
                frame[0, 0] = new Rgba32(255, 255, 255);
                video.EncodeFrame(1, frame);
            }
            var saved = video.ToArray();

            var movi = TopLevel(saved, "LIST", "movi");
            var chunks = Chunks(saved, movi.Offset + 4, movi.Offset + movi.Size);
            Assert.Equal(new[] { "00wb", "01dc", "00wb", "01db", "00wb" }, chunks.Select(c => c.Id));
            Assert.Equal(new byte[] { 0x80, 0x81, 0x82 }, saved.AsSpan(chunks[0].Offset, 3).ToArray());
            Assert.Equal(new byte[] { 0x85 }, saved.AsSpan(chunks[4].Offset, 1).ToArray());
            Assert.Equal(new byte[] { 255, 255, 255 }, saved.AsSpan(chunks[3].Offset, 3).ToArray());

            var hdrl = TopLevel(saved, "LIST", "hdrl");
            var headerChunks = Chunks(saved, hdrl.Offset + 4, hdrl.Offset + hdrl.Size);
            Assert.Equal(new[] { "avih", "LIST", "LIST", "JUNK" }, headerChunks.Select(c => c.Id));
            var audioStrl = Chunks(saved, headerChunks[1].Offset + 4, headerChunks[1].Offset + headerChunks[1].Size);
            Assert.Equal(new[] { "strh", "strf", "strn" }, audioStrl.Select(c => c.Id));
            Assert.Equal("auds", Encoding.ASCII.GetString(saved, audioStrl[0].Offset, 4));

            Assert.Contains(Chunks(saved, 12, saved.Length), c => c.Id == "LIST" && Encoding.ASCII.GetString(saved, c.Offset, 4) == "INFO");

            var reloaded = AviVideo.Load(saved);
            Assert.True(reloaded.TopDown);
            Assert.Equal(saved, reloaded.ToArray());
            using var reread = reloaded.DecodeFrame(1);
            Assert.Equal(new Rgba32(255, 255, 255), reread[0, 0]);
        }

        [Fact]
        public void Avi_Rejects_BadInput()
        {
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(PcmAudioTests.Synthetic(10).ToArray()));
            Assert.Throws<InvalidDataException>(() => AviVideo.Load(Encoding.ASCII.GetBytes("RIFF\0\0\0\0AVI ")));

            var xvid = HandBuiltAvi();
            int strf = Encoding.ASCII.GetBytes("XVID").Length;
            var hdrl = TopLevel(xvid, "LIST", "hdrl");
            var videoStrl = Chunks(xvid, hdrl.Offset + 4, hdrl.Offset + hdrl.Size)[2];
            var strfChunk = Chunks(xvid, videoStrl.Offset + 4, videoStrl.Offset + videoStrl.Size).First(c => c.Id == "strf");
            Encoding.ASCII.GetBytes("XVID").CopyTo(xvid, strfChunk.Offset + 16);
            var error = Assert.Throws<NotSupportedException>(() => AviVideo.Load(xvid));
            Assert.Contains("XVID", error.Message);
            Assert.Equal(4, strf);

            var video = RgbVideo(1, 8, 8);
            Assert.Throws<ArgumentException>(() => video.AddFrame(new byte[10]));
            Assert.Throws<ArgumentException>(() => video.AddFrame(new Image<Rgba32>(4, 4)));
            Assert.Throws<ArgumentOutOfRangeException>(() => video.GetFrameData(1));
            Assert.Throws<ArgumentException>(() => MjpegVideo(1).AddFrame(new byte[] { 1, 2, 3, 4 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => AviVideo.CreateRgb(8, 8, 16));
        }

        // ---------- video coding ----------

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2000)]
        public void Coding_Rgb_RoundTrip_ThroughFile(int length)
        {
            var data = Random(length, length + 3);
            var coder = new VideoCoding<Image<Rgba32>>(Lsb());
            var video = RgbVideo();

            coder.EmbedBytes(data, video.RgbFrames);
            var reloaded = AviVideo.Load(video.ToArray());

            Assert.Equal(data, coder.ExtractBytes(reloaded.RgbFrames));
        }

        [Fact]
        public void Coding_Capacity_IsSumOfFramesMinusHeader()
        {
            var video = RgbVideo(6, 64, 48);
            var lsb = Lsb();
            var coder = new VideoCoding<Image<Rgba32>>(lsb);

            long perFrame = 64 * 48 / 8 - 6; // one bit per pixel, six-byte header
            using var one = video.DecodeFrame(0);
            Assert.Equal(perFrame, lsb.Capacity(one));
            Assert.Equal(6 * perFrame - 4, coder.Capacity(video.RgbFrames));

            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(new byte[6 * perFrame - 3], video.RgbFrames));
            coder.EmbedBytes(Random((int)(6 * perFrame - 4), 9), video.RgbFrames);
            Assert.Equal(Random((int)(6 * perFrame - 4), 9), coder.ExtractBytes(video.RgbFrames));
        }

        [Fact]
        public void Coding_TinyFrames_CannotHoldHeader()
        {
            var video = RgbVideo(2, 2, 2);
            var coder = new VideoCoding<Image<Rgba32>>(Lsb());

            Assert.Equal(0, coder.Capacity(video.RgbFrames));
            Assert.Throws<CapacityExceededException>(() => coder.EmbedBytes(Array.Empty<byte>(), video.RgbFrames));
        }

        [Fact]
        public void Coding_Spread_TouchesEveryFrame_Sequential_LeavesTailUntouched()
        {
            var cover = RgbVideo(6);
            var data = Random(1500, 21);

            var spread = AviVideo.Load(cover.ToArray());
            new VideoCoding<Image<Rgba32>>(Lsb()) { Spread = true }.EmbedBytes(data, spread.RgbFrames);
            for (int i = 0; i < 6; i++)
                Assert.NotEqual(cover.GetFrameData(i), spread.GetFrameData(i));

            var sequential = AviVideo.Load(cover.ToArray());
            new VideoCoding<Image<Rgba32>>(Lsb()) { Spread = false }.EmbedBytes(data, sequential.RgbFrames);
            // 1504 payload bytes over frames of 378 capacity: frames 0 to 3 change, the last two are byte-identical.
            for (int i = 0; i < 4; i++)
                Assert.NotEqual(cover.GetFrameData(i), sequential.GetFrameData(i));
            for (int i = 4; i < 6; i++)
                Assert.Equal(cover.GetFrameData(i), sequential.GetFrameData(i));

            Assert.Equal(data, new VideoCoding<Image<Rgba32>>(Lsb()).ExtractBytes(spread.RgbFrames));
            Assert.Equal(data, new VideoCoding<Image<Rgba32>>(Lsb()).ExtractBytes(sequential.RgbFrames));
        }

        [Fact]
        public void Coding_Spread_IsProportionalToFrameCapacity()
        {
            var frames = new List<Image<Rgba32>>
            {
                JpegImageTests.TestPicture(64, 48, 1),   // 378 bytes
                JpegImageTests.TestPicture(32, 24, 2),   // 90 bytes
                JpegImageTests.TestPicture(64, 48, 3),   // 378 bytes
            };
            try
            {
                var lsb = Lsb();
                var data = Random(600, 5);
                new VideoCoding<Image<Rgba32>>(lsb).EmbedBytes(data, new FrameList<Image<Rgba32>>(frames));

                int[] pieces = frames.Select(f => lsb.ExtractBytes(f).Length).ToArray();
                Assert.Equal(604, pieces.Sum());
                Assert.InRange(pieces[1], 60, 68);            // 90/846 of 604 is about 64
                Assert.InRange(pieces[0] - pieces[2], -1, 1);  // equal capacity, equal share
                Assert.Equal(data, new VideoCoding<Image<Rgba32>>(lsb).ExtractBytes(new FrameList<Image<Rgba32>>(frames)));
            }
            finally
            {
                foreach (var frame in frames) frame.Dispose();
            }
        }

        [Fact]
        public void Coding_Mjpeg_WithF5_RoundTrip_FramesStayValidJpegs()
        {
            var data = Random(600, 33);
            var coder = new VideoCoding<JpegImage>(new F5(Key));
            var video = MjpegVideo();
            long capacity = coder.Capacity(video.JpegFrames);
            Assert.True(capacity >= data.Length, $"capacity {capacity}");

            coder.EmbedBytes(data, video.JpegFrames);
            var reloaded = AviVideo.Load(video.ToArray());

            Assert.Equal(data, new VideoCoding<JpegImage>(new F5(Key)).ExtractBytes(reloaded.JpegFrames));
            for (int i = 0; i < reloaded.FrameCount; i++)
            {
                using var decoded = Image.Load<Rgba32>(reloaded.GetFrameData(i));
                Assert.Equal(128, decoded.Width);
            }
        }

        [Fact]
        public void Coding_Pipeline_WrongKeyAndCleanCover()
        {
            var data = Random(200, 8);
            var pipeline = new StegoPipeline<IFrameSequence<Image<Rgba32>>>(new VideoCoding<Image<Rgba32>>(Lsb()));
            var video = RgbVideo();
            var cover = AviVideo.Load(video.ToArray());

            pipeline.Embed(data, video.RgbFrames, Key);

            var result = pipeline.Extract(video.RgbFrames, Key);
            Assert.Equal(ExtractionStatus.Success, result.Status);
            Assert.Equal(data, result.Data);

            var wrong = new StegoPipeline<IFrameSequence<Image<Rgba32>>>(new VideoCoding<Image<Rgba32>>(new Algorithms.LSB(new KeyedPermutationSelector(OtherKey))));
            Assert.False(wrong.Extract(video.RgbFrames, OtherKey).IsSuccess);
            Assert.False(pipeline.Extract(cover.RgbFrames, Key).IsSuccess);
        }

        [Fact]
        public void Coding_ArgumentChecks()
        {
            Assert.Throws<ArgumentNullException>(() => new VideoCoding<Image<Rgba32>>(null));
            var coder = new VideoCoding<Image<Rgba32>>(Lsb());
            Assert.Throws<ArgumentNullException>(() => coder.EmbedBytes(null, RgbVideo(1).RgbFrames));
            Assert.Throws<ArgumentNullException>(() => coder.EmbedBytes(new byte[1], null));
            Assert.Throws<ArgumentNullException>(() => coder.ExtractBytes(null));
            Assert.Throws<ArgumentNullException>(() => new FrameList<Image<Rgba32>>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameList<Image<Rgba32>>(new List<Image<Rgba32>>()).Read(0, f => 1));
        }

        // ---------- image sequences ----------

        [Fact]
        public void ImageSequence_RoundTrip_InPlace()
        {
            string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var originals = new List<byte[]>();
                for (int i = 0; i < 4; i++)
                {
                    using var picture = JpegImageTests.TestPicture(48, 32, 300 + i);
                    string path = Path.Combine(dir, $"frame_{i:D3}.png");
                    picture.SaveAsPng(path);
                    originals.Add(File.ReadAllBytes(path));
                }
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignored");

                var data = Random(300, 44);
                var sequence = ImageSequence.FromDirectory(dir);
                Assert.Equal(4, sequence.Count);
                Assert.Equal(Enumerable.Range(0, 4).Select(i => Path.Combine(dir, $"frame_{i:D3}.png")), sequence.Paths);

                new VideoCoding<Image<Rgba32>>(Lsb()) { Spread = false }.EmbedBytes(data, sequence);

                Assert.Equal(data, new VideoCoding<Image<Rgba32>>(Lsb()).ExtractBytes(ImageSequence.FromDirectory(dir)));
                Assert.NotEqual(originals[0], File.ReadAllBytes(sequence.Paths[0]));
                Assert.Equal(originals[3], File.ReadAllBytes(sequence.Paths[3])); // 304 bytes fit in the first frame

                Assert.Throws<NotSupportedException>(() => new ImageSequence(new[] { Path.Combine(dir, "a.jpg") }));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // ---------- file helpers ----------

        [Fact]
        public void FileHelpers_RgbAndMjpeg()
        {
            string input = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
            string output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
            try
            {
                var data = Random(120, 55);

                RgbVideo().Save(input);
                var rgb = new VideoCoding<Image<Rgba32>>(Lsb());
                rgb.EmbedBytes(data, input, output);
                Assert.Equal(data, rgb.ExtractBytes(output));
                var rgbPipeline = new StegoPipeline<IFrameSequence<Image<Rgba32>>>(rgb);
                rgbPipeline.Embed(data, input, output, Key);
                Assert.Equal(data, rgbPipeline.Extract(output, Key).Data);

                MjpegVideo().Save(input);
                var mjpeg = new VideoCoding<JpegImage>(new F5(Key));
                mjpeg.EmbedBytes(data, input, output);
                Assert.Equal(data, mjpeg.ExtractBytes(output));
                var mjpegPipeline = new StegoPipeline<IFrameSequence<JpegImage>>(mjpeg);
                mjpegPipeline.Embed(data, input, output, Key);
                Assert.Equal(data, mjpegPipeline.Extract(output, Key).Data);
            }
            finally
            {
                File.Delete(input);
                File.Delete(output);
            }
        }
    }
}
