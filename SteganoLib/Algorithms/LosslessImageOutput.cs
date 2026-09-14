using System;
using System.IO;

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Tiff.Constants;

namespace SteganoLib.Algorithms
{
    /// <summary>Encoders that preserve the RGB bytes carrying image payloads, regardless of source encoding settings.</summary>
    internal static class LosslessImageOutput
    {
        public static IImageEncoder EncoderForPath(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => CreatePngEncoder(),
                ".bmp" => new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32, SupportTransparency = true },
                ".tif" or ".tiff" => new TiffEncoder
                {
                    BitsPerPixel = TiffBitsPerPixel.Bit24,
                    PhotometricInterpretation = TiffPhotometricInterpretation.Rgb,
                    Compression = TiffCompression.Deflate,
                },
                _ => throw new NotSupportedException("Image payload output must be PNG, BMP or TIFF (.png, .bmp, .tif, .tiff) to preserve the embedded RGB samples."),
            };
        }

        public static PngEncoder CreatePngEncoder() => new()
        {
            BitDepth = PngBitDepth.Bit8,
            ColorType = PngColorType.RgbWithAlpha,
            TransparentColorMode = PngTransparentColorMode.Preserve,
        };
    }
}
