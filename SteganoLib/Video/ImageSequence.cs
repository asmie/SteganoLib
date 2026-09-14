using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SteganoLib.Video
{
    /// <summary>
    /// A video stored as one lossless image file per frame, the usual intermediate of
    /// video tools. Frames are loaded on demand and written back to the same path, so
    /// copy the directory first if the originals must stay untouched.
    /// </summary>
    public sealed class ImageSequence : IFrameSequence<Image<Rgba32>>
    {
        private static readonly string[] LossyExtensions = { ".jpg", ".jpeg", ".jfif", ".webp", ".gif" };

        private readonly List<string> _paths;

        public ImageSequence(IEnumerable<string> paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            _paths = paths.ToList();
            foreach (var path in _paths)
            {
                if (path == null)
                    throw new ArgumentException("Paths must not be null.", nameof(paths));
                if (Array.IndexOf(LossyExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0)
                    throw new NotSupportedException($"{path}: frames are written back in the file's own format, which must be lossless (PNG, BMP, TIFF).");
            }
        }

        /// <summary>All files matching <paramref name="searchPattern"/> in <paramref name="directory"/>, in ordinal name order.</summary>
        public static ImageSequence FromDirectory(string directory, string searchPattern = "*.png")
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (searchPattern == null) throw new ArgumentNullException(nameof(searchPattern));

            return new ImageSequence(Directory.GetFiles(directory, searchPattern).OrderBy(p => p, StringComparer.Ordinal));
        }

        public IReadOnlyList<string> Paths => _paths;

        public int Count => _paths.Count;

        public TResult Read<TResult>(int index, Func<Image<Rgba32>, TResult> reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            using var image = Image.Load<Rgba32>(PathAt(index));
            return reader(image);
        }

        public void Modify(int index, Action<Image<Rgba32>> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            string path = PathAt(index);
            using var image = Image.Load<Rgba32>(path);
            action(image);
            image.Save(path);
        }

        private string PathAt(int index)
        {
            if (index < 0 || index >= _paths.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _paths[index];
        }
    }
}
