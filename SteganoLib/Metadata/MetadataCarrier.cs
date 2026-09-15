#nullable enable

using System;
using System.IO;

namespace SteganoLib.Metadata
{
    public enum MetadataFormat
    {
        Png,
        Jpeg,
        Wav,
    }

    /// <summary>
    /// A container file opened at the metadata level. Embedding changes metadata without
    /// decoding image or audio data; tools that strip metadata can remove the payload.
    /// <see cref="Load(byte[], MetadataOptions)"/> picks the store from the file signature.
    /// The supplied store is shared with this carrier; operations do not clone it.
    /// </summary>
    public sealed class MetadataCarrier
    {
        public MetadataCarrier(IMetadataStore store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public IMetadataStore Store { get; }

        /// <summary>Format recognised from the first bytes, or <c>null</c> when none matches.</summary>
        public static MetadataFormat? Detect(ReadOnlySpan<byte> data)
        {
            if (PngMetadataStore.IsPng(data)) return MetadataFormat.Png;
            if (JpegMetadataStore.IsJpeg(data)) return MetadataFormat.Jpeg;
            if (WavMetadataStore.IsWav(data)) return MetadataFormat.Wav;
            return null;
        }

        /// <exception cref="NotSupportedException">The signature is not PNG, JPEG or RIFF WAVE.</exception>
        /// <exception cref="InvalidDataException">The file is recognised but corrupt.</exception>
        public static MetadataCarrier Load(byte[] data, MetadataOptions? options = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            options ??= new MetadataOptions();

            IMetadataStore store = Detect(data) switch
            {
                MetadataFormat.Png => new PngMetadataStore(data, options.PngChunkType, options.PngKeyword),
                MetadataFormat.Jpeg => new JpegMetadataStore(data, options.JpegAppNumber, options.JpegIdentifier),
                MetadataFormat.Wav => new WavMetadataStore(data, options.WavChunkId),
                _ => throw new NotSupportedException("Only PNG, JPEG and RIFF WAVE containers are supported."),
            };
            return new MetadataCarrier(store);
        }

        public static MetadataCarrier Load(string path, MetadataOptions? options = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return Load(File.ReadAllBytes(path), options);
        }

        public static MetadataCarrier Load(Stream stream, MetadataOptions? options = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Load(buffer.ToArray(), options);
        }

        public byte[] ToArray() => Store.ToArray();

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
    }
}
