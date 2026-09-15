#nullable enable

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
    /// The codec list is copied, but codec objects are shared. Configure codecs before
    /// registration and keep their identifiers and overhead stable. Concurrent operations
    /// require thread-safe codecs and unchanged configuration.
    /// </summary>
    public sealed class PayloadEnvelope
    {
        private const byte Version = 1;
        private const int HeaderSize = 5;
        private const byte FlagCompressed = 0x01;
        private static readonly byte[] Magic = { 0x53, 0x4C }; // "SL"

        private readonly IReadOnlyList<Registration> _codecs;

        private sealed record Registration(IPayloadCodec Codec, byte Id, int Overhead);

        /// <summary>AES-GCM for sealing; AES-GCM and HMAC accepted when opening.</summary>
        public PayloadEnvelope()
            : this(new AesGcmPayloadCodec(), new HmacPayloadCodec())
        {
        }

        public PayloadEnvelope(params IPayloadCodec[] codecs)
        {
            ArgumentNullException.ThrowIfNull(codecs);
            if (codecs.Length == 0)
                throw new ArgumentException("At least one codec is required.", nameof(codecs));
            var registrations = new List<Registration>(codecs.Length);
            var ids = new HashSet<byte>();
            foreach (var codec in codecs)
            {
                if (codec == null)
                    throw new ArgumentException("Codecs must not contain null.", nameof(codecs));
                byte id = codec.Id;
                int overhead = codec.Overhead;
                if (!ids.Add(id))
                    throw new ArgumentException("Codec ids must be unique.", nameof(codecs));
                if (overhead < 0 || overhead > Array.MaxLength - HeaderSize)
                    throw new ArgumentException("Codec overhead must be nonnegative and leave room for the envelope header.", nameof(codecs));
                registrations.Add(new Registration(codec, id, overhead));
            }
            _codecs = registrations;
        }

        /// <summary>Compress with Brotli before sealing. Skipped automatically when it would not shrink the data.</summary>
        public bool Compress { get; set; }

        /// <summary>Bytes added by the header and the sealing codec. Compression never adds more.</summary>
        public int Overhead
        {
            get
            {
                ValidateRegistration(_codecs[0]);
                return HeaderSize + _codecs[0].Overhead;
            }
        }

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

            var registration = _codecs[0];
            ValidateRegistration(registration);
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

            long expectedLength = (long)body.Length + registration.Overhead;
            if (expectedLength > Array.MaxLength - HeaderSize)
                throw new InvalidOperationException("Sealed payload exceeds the maximum byte array length.");
            var header = new byte[] { Magic[0], Magic[1], Version, flags, registration.Id };
            var sealedBody = registration.Codec.Seal(body, header, key); // the header is bound into the tag
            ValidateRegistration(registration);
            if (sealedBody == null || sealedBody.Length != expectedLength)
                throw new InvalidOperationException("The codec returned null or a sealed length inconsistent with its overhead.");

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
            var registration = _codecs.FirstOrDefault(c => c.Id == codecId);
            if (registration == null)
                return ExtractResult.Unsupported();

            ValidateRegistration(registration);
            var sealedBody = sealedData.Slice(HeaderSize);
            if (sealedBody.Length < registration.Overhead)
                return ExtractResult.AuthenticationFailed();
            bool opened = registration.Codec.TryOpen(sealedBody, sealedData.Slice(0, HeaderSize), key, out var body);
            ValidateRegistration(registration);
            if (!opened)
                return ExtractResult.AuthenticationFailed();
            if (body == null || body.Length != sealedBody.Length - registration.Overhead)
                throw new InvalidOperationException("The codec reported success with null plaintext or an inconsistent length.");
            if ((flags & ~FlagCompressed) != 0)
                return ExtractResult.Unsupported();

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

        private static void ValidateRegistration(Registration registration)
        {
            if (registration.Codec.Id != registration.Id || registration.Codec.Overhead != registration.Overhead)
                throw new InvalidOperationException("Codec identifiers and overhead must not change after registration.");
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
