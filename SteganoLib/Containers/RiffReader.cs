using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SteganoLib.Containers
{
    /// <summary>RIFF boundaries shared by the audio, video and metadata readers.</summary>
    internal static class RiffReader
    {
        public static int ContainerEnd(byte[] data, string form, bool allowTrailing = false)
        {
            if (data.Length < 12 || Tag(data, 0) != "RIFF" || Tag(data, 8) != form)
                throw new InvalidDataException($"Not a RIFF {form} file.");
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
            if (size < 4 || size > data.Length - 8)
                throw new InvalidDataException("Invalid RIFF container length.");
            int end = 8 + (int)size;
            if (!allowTrailing && end != data.Length)
                throw new InvalidDataException("Unexpected data after the RIFF container.");

            // Validate nested LIST/RIFF bodies without using the call stack.
            var pending = new Stack<(int Start, int End)>();
            pending.Push((12, end));
            while (pending.Count > 0)
            {
                var range = pending.Pop();
                foreach (var (id, offset, length) in Chunks(data, range.Start, range.End))
                    if (id == "LIST" || id == "RIFF")
                        pending.Push((offset + 4, offset + length));
            }
            return end;
        }

        public static IEnumerable<(string Id, int Offset, int Size)> Chunks(byte[] data, int start, int end)
        {
            if (start < 0 || end < start || end > data.Length)
                throw new InvalidDataException("Invalid RIFF chunk range.");
            int pos = start;
            while (pos < end)
            {
                if (end - pos < 8)
                    throw new InvalidDataException("Truncated RIFF chunk header.");
                string id = Tag(data, pos);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
                pos += 8;
                if (size > end - pos)
                    throw new InvalidDataException("RIFF chunk extends beyond its parent.");
                int length = (int)size;
                if ((length & 1) != 0 && end - pos - length < 1)
                    throw new InvalidDataException("Missing RIFF chunk padding.");
                if ((id == "LIST" || id == "RIFF") && length < 4)
                    throw new InvalidDataException("RIFF list is missing its type.");
                yield return (id, pos, length);
                pos += length + (length & 1);
            }
        }

        private static string Tag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);
    }
}
