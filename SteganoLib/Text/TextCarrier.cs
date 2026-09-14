using System;
using System.IO;
using System.Text;

namespace SteganoLib.Text
{
    /// <summary>Mutable text used as a steganographic carrier. Files are read and written as UTF-8 without a byte order mark.</summary>
    public sealed class TextCarrier
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
        private string _text;

        public TextCarrier(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string Text
        {
            get => _text;
            set => _text = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static TextCarrier Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            return new TextCarrier(File.ReadAllText(path, Utf8));
        }

        public static TextCarrier Load(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return new TextCarrier(reader.ReadToEnd());
        }

        public void Save(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            File.WriteAllText(path, _text, Utf8);
        }

        public void Save(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var bytes = Utf8.GetBytes(_text);
            stream.Write(bytes, 0, bytes.Length);
        }

        public override string ToString() => _text;
    }
}
