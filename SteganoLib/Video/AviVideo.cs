using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SteganoLib.Containers;
using SteganoLib.Jpeg;

namespace SteganoLib.Video
{
    public enum AviCodec
    {
        /// <summary>Uncompressed 24- or 32-bit BGR frames (<c>BI_RGB</c>).</summary>
        Rgb,

        /// <summary>Motion JPEG: every frame is a standalone baseline JPEG.</summary>
        Mjpeg,
    }

    /// <summary>
    /// A RIFF AVI file with one uncompressed RGB or Motion JPEG video stream. Frames are
    /// exposed both as raw chunk bytes and decoded, as <see cref="Image{TPixel}"/> for
    /// RGB or <see cref="JpegImage"/> for MJPEG, so the image algorithms apply per frame.
    /// Audio streams, extra headers and chunk order are preserved; the index is rebuilt.
    /// OpenDML files with more than one RIFF chunk are not supported.
    /// </summary>
    public sealed class AviVideo
    {
        private const int MainHeaderSize = 56;
        private const int StreamHeaderSize = 56;
        private const int BitmapInfoSize = 40;
        private const uint FlagHasIndex = 0x10;
        private const uint FlagKeyFrame = 0x10;

        private static readonly Encoding Ascii = Encoding.ASCII;

        private byte[] _avih;
        private int _videoStream;
        private readonly List<List<(string Id, byte[] Data)>> _streams;
        private readonly List<byte[]> _headerExtras;
        private readonly List<byte[]> _topLevelExtras;
        private readonly List<(string Id, byte[] Data)> _movi;
        private readonly List<int> _frames;

        private AviVideo(int width, int height, AviCodec codec, int bitCount, int frameRateNumerator, int frameRateDenominator)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
            if (bitCount != 24 && bitCount != 32) throw new ArgumentOutOfRangeException(nameof(bitCount), "Only 24- and 32-bit RGB frames are supported.");
            if (codec == AviCodec.Rgb && RgbByteLength(width, height, bitCount) > Array.MaxLength)
                throw new ArgumentOutOfRangeException(nameof(width), "RGB dimensions exceed the supported frame length.");
            if (frameRateNumerator < 1) throw new ArgumentOutOfRangeException(nameof(frameRateNumerator));
            if (frameRateDenominator < 1) throw new ArgumentOutOfRangeException(nameof(frameRateDenominator));

            Width = width;
            Height = height;
            Codec = codec;
            BitCount = codec == AviCodec.Rgb ? bitCount : 24;
            FrameRateNumerator = frameRateNumerator;
            FrameRateDenominator = frameRateDenominator;

            _avih = new byte[MainHeaderSize];
            var strh = new byte[StreamHeaderSize];
            Ascii.GetBytes("vids", 0, 4, strh, 0);
            Ascii.GetBytes(codec == AviCodec.Rgb ? "DIB " : "MJPG", 0, 4, strh, 4);
            BinaryPrimitives.WriteUInt32LittleEndian(strh.AsSpan(40), 10000); // quality
            var strf = new byte[BitmapInfoSize];
            BinaryPrimitives.WriteInt32LittleEndian(strf, BitmapInfoSize);
            BinaryPrimitives.WriteUInt16LittleEndian(strf.AsSpan(12), 1); // planes

            _streams = new List<List<(string, byte[])>> { new() { ("strh", strh), ("strf", strf) } };
            _headerExtras = new List<byte[]>();
            _topLevelExtras = new List<byte[]>();
            _movi = new List<(string, byte[])>();
            _frames = new List<int>();
            _videoStream = 0;
        }

        private AviVideo(byte[] data)
        {
            if (!IsAvi(data))
                throw new InvalidDataException("Not a RIFF AVI file.");

            _streams = new List<List<(string, byte[])>>();
            _headerExtras = new List<byte[]>();
            _topLevelExtras = new List<byte[]>();
            _movi = new List<(string, byte[])>();
            _frames = new List<int>();
            _videoStream = -1;

            int riffEnd = RiffReader.ContainerEnd(data, "AVI ", allowTrailing: true);
            if (riffEnd < data.Length && data.Length - riffEnd >= 12 && Tag(data, riffEnd) == "RIFF")
                throw new NotSupportedException("OpenDML AVI files with AVIX extension chunks are not supported.");
            if (riffEnd != data.Length)
                throw new InvalidDataException("Unexpected data after the AVI RIFF container.");

            bool hdrlSeen = false, moviSeen = false;
            foreach (var (id, offset, size) in Chunks(data, 12, riffEnd))
            {
                string listType = id == "LIST" && size >= 4 ? Tag(data, offset) : null;
                if (listType == "hdrl")
                {
                    if (hdrlSeen || moviSeen)
                        throw new InvalidDataException("AVI header list is repeated or follows movie data.");
                    ParseHeaderList(data, offset + 4, offset + size);
                    hdrlSeen = true;
                }
                else if (listType == "movi")
                {
                    if (!hdrlSeen || moviSeen)
                        throw new InvalidDataException("AVI movie data must follow one header list and must not be repeated.");
                    ParseMovieList(data, offset + 4, offset + size);
                    moviSeen = true;
                }
                else if (id != "idx1")
                {
                    _topLevelExtras.Add(RawChunk(data, offset - 8, size));
                }
            }

            if (!hdrlSeen || _avih == null)
                throw new InvalidDataException("AVI file has no main header.");
            if (!moviSeen)
                throw new InvalidDataException("AVI file has no movie data.");
            if (_videoStream < 0)
                throw new NotSupportedException("AVI file has no video stream.");

            var strf = FindChunk(_streams[_videoStream], "strf");
            var strh = FindChunk(_streams[_videoStream], "strh");
            Width = BinaryPrimitives.ReadInt32LittleEndian(strf.AsSpan(4));
            int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(strf.AsSpan(8));
            if (rawHeight == int.MinValue)
                throw new InvalidDataException("AVI height is outside the supported range.");
            Height = Math.Abs(rawHeight);
            TopDown = rawHeight < 0;
            BitCount = BinaryPrimitives.ReadUInt16LittleEndian(strf.AsSpan(14));
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(strf.AsSpan(16));
            string fourcc = Ascii.GetString(strf, 16, 4).ToUpperInvariant();
            FrameRateDenominator = BinaryPrimitives.ReadInt32LittleEndian(strh.AsSpan(20));
            FrameRateNumerator = BinaryPrimitives.ReadInt32LittleEndian(strh.AsSpan(24));
            if (FrameRateDenominator < 1 || FrameRateNumerator < 1)
                throw new InvalidDataException("AVI frame-rate numerator and denominator must be positive.");
            uint bitmapSize = BinaryPrimitives.ReadUInt32LittleEndian(strf);
            if (bitmapSize < BitmapInfoSize || bitmapSize > strf.Length || BinaryPrimitives.ReadUInt16LittleEndian(strf.AsSpan(12)) != 1)
                throw new InvalidDataException("Invalid AVI bitmap header size or plane count.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(_avih.AsSpan(24)) != _streams.Count)
                throw new InvalidDataException("AVI stream count does not match the header lists.");

            if (Width < 1 || Height < 1)
                throw new InvalidDataException("AVI video stream has no dimensions.");

            if (compression == 0 && (BitCount == 24 || BitCount == 32))
                Codec = AviCodec.Rgb;
            else if (fourcc == "MJPG" || fourcc == "DMB1")
                Codec = AviCodec.Mjpeg;
            else
                throw new NotSupportedException($"AVI video codec {(compression == 0 ? BitCount + "-bit RGB" : fourcc)} is not supported; only uncompressed 24/32-bit RGB and MJPEG are.");

            if (Codec == AviCodec.Rgb && RgbByteLength(Width, Height, BitCount) > Array.MaxLength)
                throw new InvalidDataException("AVI RGB dimensions exceed the supported frame length.");
            foreach (int frame in _frames)
            {
                var bytes = _movi[frame].Data;
                if (bytes.Length == 0)
                    throw new NotSupportedException("Zero-length dropped AVI frames are not supported.");
                if (Codec == AviCodec.Rgb && bytes.Length != RgbByteLength(Width, Height, BitCount))
                    throw new InvalidDataException("AVI RGB frame length does not match its dimensions.");
                if (Codec == AviCodec.Mjpeg && !IsJpegSignature(bytes))
                    throw new InvalidDataException("AVI MJPEG frame is missing its JPEG signature.");
            }
        }

        public int Width { get; }

        public int Height { get; }

        public AviCodec Codec { get; }

        /// <summary>Bits per pixel of RGB frames: 24 (BGR) or 32 (BGRA).</summary>
        public int BitCount { get; }

        /// <summary>Whether RGB rows are stored top to bottom (negative height in the header). Default is bottom-up.</summary>
        public bool TopDown { get; }

        public int FrameRateNumerator { get; }

        public int FrameRateDenominator { get; }

        public double FrameRate => (double)FrameRateNumerator / FrameRateDenominator;

        public int FrameCount => _frames.Count;

        public TimeSpan Duration => TimeSpan.FromSeconds(FrameCount / FrameRate);

        /// <summary>Bytes per row of an RGB frame, padded to four bytes.</summary>
        public int Stride => checked((int)(((long)Width * BitCount / 8 + 3) & ~3L));

        /// <summary>Video stream number in the file; frame chunk ids start with it, e.g. <c>00db</c>.</summary>
        public int VideoStreamIndex => _videoStream;

        /// <summary>Frames decoded as images. Only for <see cref="AviCodec.Rgb"/>.</summary>
        public IFrameSequence<Image<Rgba32>> RgbFrames
        {
            get
            {
                RequireCodec(AviCodec.Rgb);
                return new RgbSequence(this);
            }
        }

        /// <summary>Frames opened at the coefficient level. Only for <see cref="AviCodec.Mjpeg"/>.</summary>
        public IFrameSequence<JpegImage> JpegFrames
        {
            get
            {
                RequireCodec(AviCodec.Mjpeg);
                return new JpegSequence(this);
            }
        }

        public static bool IsAvi(ReadOnlySpan<byte> data)
        {
            return data.Length >= 12 && Tag(data, 0) == "RIFF" && Tag(data, 8) == "AVI ";
        }

        /// <summary>An empty uncompressed video; add frames with <see cref="AddFrame(Image{Rgba32})"/>.</summary>
        public static AviVideo CreateRgb(int width, int height, int bitCount = 24, int frameRateNumerator = 25, int frameRateDenominator = 1)
        {
            return new AviVideo(width, height, AviCodec.Rgb, bitCount, frameRateNumerator, frameRateDenominator);
        }

        /// <summary>An empty Motion JPEG video; add frames with <see cref="AddFrame(byte[])"/> or <see cref="AddFrame(JpegImage)"/>.</summary>
        public static AviVideo CreateMjpeg(int width, int height, int frameRateNumerator = 25, int frameRateDenominator = 1)
        {
            return new AviVideo(width, height, AviCodec.Mjpeg, 24, frameRateNumerator, frameRateDenominator);
        }

        /// <exception cref="InvalidDataException">Invalid RIFF boundaries, AVI headers or RGB frame sizes.</exception>
        /// <exception cref="NotSupportedException">Unsupported codecs, multiple video streams, dropped frames, movie list types or OpenDML files.</exception>
        public static AviVideo Load(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return new AviVideo(data);
        }

        public static AviVideo Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Load(File.ReadAllBytes(path));
        }

        public static AviVideo Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Load(buffer.ToArray());
        }

        // ---------- raw frames ----------

        /// <summary>Raw chunk bytes of frame <paramref name="index"/>: a DIB for RGB, a JPEG file for MJPEG.</summary>
        public byte[] GetFrameData(int index) => _movi[FrameChunk(index)].Data;

        public void SetFrameData(int index, byte[] data)
        {
            int chunk = FrameChunk(index);
            _movi[chunk] = (_movi[chunk].Id, ValidateFrame(data));
        }

        public void AddFrame(byte[] data)
        {
            var frame = ValidateFrame(data);
            _frames.Add(_movi.Count);
            _movi.Add(($"{_videoStream:D2}{(Codec == AviCodec.Rgb ? "db" : "dc")}", frame));
        }

        // ---------- RGB frames ----------

        /// <summary>Decode an RGB frame. The 32-bit variant keeps the fourth byte as alpha.</summary>
        public Image<Rgba32> DecodeFrame(int index)
        {
            RequireCodec(AviCodec.Rgb);
            var data = GetFrameData(index);
            int bytesPerPixel = BitCount / 8;
            int stride = Stride;
            var image = new Image<Rgba32>(Width, Height);
            for (int y = 0; y < Height; y++)
            {
                int row = (TopDown ? y : Height - 1 - y) * stride;
                for (int x = 0; x < Width; x++)
                {
                    int p = row + x * bytesPerPixel;
                    byte a = bytesPerPixel == 4 ? data[p + 3] : (byte)255;
                    image[x, y] = new Rgba32(data[p + 2], data[p + 1], data[p], a);
                }
            }
            return image;
        }

        public void EncodeFrame(int index, Image<Rgba32> image)
        {
            SetFrameData(index, EncodeRgb(image));
        }

        public void AddFrame(Image<Rgba32> image)
        {
            AddFrame(EncodeRgb(image));
        }

        // ---------- MJPEG frames ----------

        public JpegImage DecodeJpegFrame(int index)
        {
            RequireCodec(AviCodec.Mjpeg);
            var image = JpegImage.Load(GetFrameData(index));
            if (image.Width != Width || image.Height != Height)
                throw new InvalidDataException("MJPEG frame dimensions do not match the AVI stream.");
            return image;
        }

        public void EncodeJpegFrame(int index, JpegImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            SetFrameData(index, image.ToArray());
        }

        public void AddFrame(JpegImage image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            AddFrame(image.ToArray());
        }

        // ---------- output ----------

        public byte[] ToArray()
        {
            int maxFrame = 0;
            foreach (int chunk in _frames)
                maxFrame = Math.Max(maxFrame, _movi[chunk].Data.Length);

            using var output = new MemoryStream();
            output.Write(Ascii.GetBytes("RIFF"));
            output.Write(new byte[4]);
            output.Write(Ascii.GetBytes("AVI "));

            WriteList(output, "hdrl", header =>
            {
                WriteChunk(header, "avih", PatchedMainHeader(maxFrame));
                for (int s = 0; s < _streams.Count; s++)
                {
                    int stream = s;
                    WriteList(header, "strl", strl =>
                    {
                        foreach (var (id, data) in _streams[stream])
                            WriteChunk(strl, id, stream == _videoStream ? PatchedVideoChunk(id, data, maxFrame) : data);
                    });
                }
                foreach (var raw in _headerExtras)
                    header.Write(raw);
            });

            foreach (var raw in _topLevelExtras)
                output.Write(raw);

            var index = new byte[16 * _movi.Count];
            WriteList(output, "movi", movi =>
            {
                long offset = 4;
                for (int i = 0; i < _movi.Count; i++)
                {
                    var (id, data) = _movi[i];
                    Ascii.GetBytes(id, 0, 4, index, i * 16);
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(i * 16 + 4), FlagKeyFrame);
                    BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(i * 16 + 8), (uint)offset);
                    BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(i * 16 + 12), data.Length);
                    WriteChunk(movi, id, data);
                    offset += 8 + data.Length + (data.Length & 1);
                }
            });
            WriteChunk(output, "idx1", index);

            var bytes = output.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
            return bytes;
        }

        public void Save(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            File.WriteAllBytes(path, ToArray());
        }

        public void Save(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var bytes = ToArray();
            stream.Write(bytes, 0, bytes.Length);
        }

        // ---------- parsing ----------

        private void ParseHeaderList(byte[] data, int start, int end)
        {
            foreach (var (id, offset, size) in Chunks(data, start, end))
            {
                if (id == "avih")
                {
                    if (_avih != null)
                        throw new InvalidDataException("Duplicate AVI main header.");
                    if (size < MainHeaderSize)
                        throw new InvalidDataException("AVI main header is too short.");
                    _avih = data.AsSpan(offset, size).ToArray();
                }
                else if (id == "LIST" && size >= 4 && Tag(data, offset) == "strl")
                {
                    var chunks = new List<(string, byte[])>();
                    foreach (var (subId, subOffset, subSize) in Chunks(data, offset + 4, offset + size))
                        chunks.Add((subId, data.AsSpan(subOffset, subSize).ToArray()));

                    var strh = FindChunk(chunks, "strh");
                    if (strh == null || strh.Length < StreamHeaderSize)
                        throw new InvalidDataException("AVI stream header is missing or too short.");
                    if (Ascii.GetString(strh, 0, 4) == "vids")
                    {
                        if (_videoStream >= 0)
                            throw new NotSupportedException("Multiple AVI video streams are not supported.");
                        var strf = FindChunk(chunks, "strf");
                        if (strf == null || strf.Length < BitmapInfoSize)
                            throw new InvalidDataException("AVI video stream has no bitmap header.");
                        _videoStream = _streams.Count;
                    }
                    _streams.Add(chunks);
                }
                else
                {
                    _headerExtras.Add(RawChunk(data, offset - 8, size));
                }
            }
        }

        private void ParseMovieList(byte[] data, int start, int end)
        {
            string prefix = _videoStream.ToString("D2");
            var pending = new Stack<(int Start, int End)>();
            pending.Push((start, end));
            while (pending.Count > 0)
            {
                var range = pending.Pop();
                foreach (var (id, offset, size) in Chunks(data, range.Start, range.End))
                {
                    if (id == "LIST")
                    {
                        if (Tag(data, offset) != "rec ")
                            throw new NotSupportedException("Only record lists are supported inside AVI movie data.");
                        // Resume siblings after visiting the record, preserving frame/audio order.
                        pending.Push((offset + size + (size & 1), range.End));
                        pending.Push((offset + 4, offset + size));
                        break;
                    }
                    if (id.StartsWith("ix", StringComparison.Ordinal))
                        continue; // OpenDML sub-index, stale after rewriting

                    if (id.StartsWith(prefix, StringComparison.Ordinal) && (id.EndsWith("db", StringComparison.Ordinal) || id.EndsWith("dc", StringComparison.Ordinal)))
                        _frames.Add(_movi.Count);
                    _movi.Add((id, data.AsSpan(offset, size).ToArray()));
                }
            }
        }

        private static IEnumerable<(string Id, int Offset, int Size)> Chunks(byte[] data, int start, int end)
            => RiffReader.Chunks(data, start, end);

        private static byte[] RawChunk(byte[] data, int chunkStart, int size)
        {
            int length = checked(8 + size + (size & 1));
            return data.AsSpan(chunkStart, length).ToArray();
        }

        private static byte[] FindChunk(List<(string Id, byte[] Data)> chunks, string id)
        {
            byte[] found = null;
            foreach (var (chunkId, data) in chunks)
            {
                if (chunkId == id)
                {
                    if (found != null)
                        throw new InvalidDataException($"Duplicate AVI {id} chunk.");
                    found = data;
                }
            }
            return found;
        }

        private static string Tag(ReadOnlySpan<byte> data, int offset) => Ascii.GetString(data.Slice(offset, 4));

        // ---------- writing ----------

        private byte[] PatchedMainHeader(int maxFrame)
        {
            var avih = (byte[])_avih.Clone();
            var span = avih.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)Math.Round(1_000_000.0 / FrameRate));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), (uint)Math.Min(uint.MaxValue, Math.Ceiling(maxFrame * FrameRate)));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12), BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12)) | FlagHasIndex);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16), FrameCount);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24), _streams.Count);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28), maxFrame);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(32), Width);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(36), Height);
            return avih;
        }

        private byte[] PatchedVideoChunk(string id, byte[] original, int maxFrame)
        {
            if (id != "strh" && id != "strf")
                return original;

            var data = (byte[])original.Clone();
            var span = data.AsSpan();
            if (id == "strh")
            {
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20), FrameRateDenominator);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24), FrameRateNumerator);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(32), FrameCount);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(36), maxFrame);
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(52), (short)Math.Min(short.MaxValue, Width));
                BinaryPrimitives.WriteInt16LittleEndian(span.Slice(54), (short)Math.Min(short.MaxValue, Height));
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), Width);
                BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), TopDown ? -Height : Height);
                BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14), (ushort)BitCount);
                if (Codec == AviCodec.Rgb)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(16), 0);
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20), Stride * Height);
                }
                else
                {
                    Ascii.GetBytes("MJPG", 0, 4, data, 16);
                    BinaryPrimitives.WriteInt32LittleEndian(span.Slice(20), maxFrame);
                }
            }
            return data;
        }

        private static void WriteChunk(Stream output, string id, byte[] data)
        {
            var header = new byte[8];
            Ascii.GetBytes(id, 0, 4, header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length);
            output.Write(header);
            output.Write(data);
            if ((data.Length & 1) == 1)
                output.WriteByte(0);
        }

        private static void WriteList(Stream output, string type, Action<Stream> body)
        {
            using var list = new MemoryStream();
            list.Write(Ascii.GetBytes(type));
            body(list);
            WriteChunk(output, "LIST", list.ToArray());
        }

        // ---------- helpers ----------

        private int FrameChunk(int index)
        {
            if (index < 0 || index >= _frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _frames[index];
        }

        private static long RgbByteLength(int width, int height, int bitCount)
        {
            long stride = ((long)width * bitCount / 8 + 3) & ~3L;
            return stride > long.MaxValue / height ? long.MaxValue : stride * height;
        }

        private byte[] ValidateFrame(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (Codec == AviCodec.Rgb)
            {
                long required = RgbByteLength(Width, Height, BitCount);
                if (data.Length != required)
                    throw new ArgumentException($"An RGB frame must be exactly {required} bytes ({Width}x{Height}, {BitCount}-bit, rows padded to 4 bytes).", nameof(data));
            }
            else if (!IsJpegSignature(data))
            {
                throw new ArgumentException("An MJPEG frame must be a JPEG file.", nameof(data));
            }
            return data;
        }

        private static bool IsJpegSignature(byte[] data)
        {
            return data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
        }

        private byte[] EncodeRgb(Image<Rgba32> image)
        {
            RequireCodec(AviCodec.Rgb);
            if (image == null)
                throw new ArgumentNullException(nameof(image));
            if (image.Width != Width || image.Height != Height)
                throw new ArgumentException($"Frame must be {Width}x{Height}, got {image.Width}x{image.Height}.", nameof(image));

            int bytesPerPixel = BitCount / 8;
            int stride = Stride;
            var data = new byte[stride * Height];
            for (int y = 0; y < Height; y++)
            {
                int row = (TopDown ? y : Height - 1 - y) * stride;
                for (int x = 0; x < Width; x++)
                {
                    var pixel = image[x, y];
                    int p = row + x * bytesPerPixel;
                    data[p] = pixel.B;
                    data[p + 1] = pixel.G;
                    data[p + 2] = pixel.R;
                    if (bytesPerPixel == 4)
                        data[p + 3] = pixel.A;
                }
            }
            return data;
        }

        private void RequireCodec(AviCodec codec)
        {
            if (Codec != codec)
                throw new InvalidOperationException($"This operation needs an {codec} video; the file is {Codec}.");
        }

        private sealed class RgbSequence : IFrameSequence<Image<Rgba32>>
        {
            private readonly AviVideo _video;

            public RgbSequence(AviVideo video) => _video = video;

            public int Count => _video.FrameCount;

            public TResult Read<TResult>(int index, Func<Image<Rgba32>, TResult> reader)
            {
                if (reader == null) throw new ArgumentNullException(nameof(reader));
                using var image = _video.DecodeFrame(index);
                return reader(image);
            }

            public void Modify(int index, Action<Image<Rgba32>> action)
            {
                if (action == null) throw new ArgumentNullException(nameof(action));
                using var image = _video.DecodeFrame(index);
                action(image);
                _video.EncodeFrame(index, image);
            }
        }

        private sealed class JpegSequence : IFrameSequence<JpegImage>
        {
            private readonly AviVideo _video;

            public JpegSequence(AviVideo video) => _video = video;

            public int Count => _video.FrameCount;

            public TResult Read<TResult>(int index, Func<JpegImage, TResult> reader)
            {
                if (reader == null) throw new ArgumentNullException(nameof(reader));
                return reader(_video.DecodeJpegFrame(index));
            }

            public void Modify(int index, Action<JpegImage> action)
            {
                if (action == null) throw new ArgumentNullException(nameof(action));
                var image = _video.DecodeJpegFrame(index);
                action(image);
                _video.EncodeJpegFrame(index, image);
            }
        }
    }
}
