using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using SteganoLib.Crypto;

namespace SteganoLib.Payload
{
    /// <summary>
    /// Frames a payload so the receiver can tell "nothing here" from "wrong key".
    /// Layout: magic (2), version (1), flags (1), codec id (1), codec body.
    /// The first registered codec seals; any registered codec can open.
    /// </summary>
    public sealed class PayloadEnvelope
    {
        private const byte Version = 1;
        private const int HeaderSize = 5;
        private const byte FlagCompressed = 0x01;
        private static readonly byte[] Magic = { 0x53, 0x4C }; // "SL"

        private readonly IReadOnlyList<IPayloadCodec> _codecs;

        /// <summary>AES-GCM for sealing; AES-GCM and HMAC accepted when opening.</summary>
        public PayloadEnvelope()
            : this(new AesGcmPayloadCodec(), new HmacPayloadCodec())
        {
        }

        public PayloadEnvelope(params IPayloadCodec[] codecs)
        {
            if (codecs == null || codecs.Length == 0)
                throw new ArgumentException("At least one codec is required.", nameof(codecs));
            if (codecs.Any(c => c == null))
                throw new ArgumentException("Codecs must not contain null.", nameof(codecs));
            if (codecs.Select(c => c.Id).Distinct().Count() != codecs.Length)
                throw new ArgumentException("Codec ids must be unique.", nameof(codecs));

            _codecs = codecs.ToArray();
        }

        /// <summary>Compress with Brotli before sealing. Skipped automatically when it would not shrink the data.</summary>
        public bool Compress { get; set; }

        /// <summary>Bytes added by the header and the sealing codec. Compression never adds more.</summary>
        public int Overhead => HeaderSize + _codecs[0].Overhead;

        public byte[] Seal(byte[] payload, StegoKey key)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            return Seal(payload.AsSpan(), key);
        }

        public byte[] Seal(ReadOnlySpan<byte> payload, StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            byte flags = 0;
            ReadOnlySpan<byte> body = payload;

            if (Compress)
            {
                var compressed = BrotliCompress(payload);
                if (compressed.Length < payload.Length)
                {
                    body = compressed;
                    flags |= FlagCompressed;
                }
            }

            var codec = _codecs[0];
            var header = new byte[] { Magic[0], Magic[1], Version, flags, codec.Id };
            var sealedBody = codec.Seal(body, header, key); // the header is bound into the tag

            var output = new byte[HeaderSize + sealedBody.Length];
            header.CopyTo(output, 0);
            sealedBody.CopyTo(output, HeaderSize);
            return output;
        }

        public ExtractResult Open(byte[] sealedData, StegoKey key)
        {
            if (sealedData == null)
                throw new ArgumentNullException(nameof(sealedData));
            return Open(sealedData.AsSpan(), key);
        }

        public ExtractResult Open(ReadOnlySpan<byte> sealedData, StegoKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (sealedData.Length < HeaderSize || sealedData[0] != Magic[0] || sealedData[1] != Magic[1])
                return ExtractResult.NotFound();

            if (sealedData[2] != Version)
                return ExtractResult.Unsupported();

            byte flags = sealedData[3];
            byte codecId = sealedData[4];
            var codec = _codecs.FirstOrDefault(c => c.Id == codecId);
            if (codec == null)
                return ExtractResult.Unsupported();

            if (!codec.TryOpen(sealedData.Slice(HeaderSize), sealedData.Slice(0, HeaderSize), key, out var body))
                return ExtractResult.AuthenticationFailed();

            if ((flags & FlagCompressed) != 0)
            {
                try
                {
                    body = BrotliDecompress(body);
                }
                catch (InvalidDataException)
                {
                    return ExtractResult.AuthenticationFailed();
                }
            }

            return ExtractResult.Success(body);
        }

        private static byte[] BrotliCompress(ReadOnlySpan<byte> input)
        {
            using var output = new MemoryStream();
            using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
                brotli.Write(input);
            return output.ToArray();
        }

        private static byte[] BrotliDecompress(byte[] input)
        {
            using var source = new MemoryStream(input);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            return output.ToArray();
        }
    }
}
