using System;
using System.IO;

namespace SteganoLib.Cli
{
    /// <summary>Which carrier family a file is handled as.</summary>
    internal enum CarrierKind
    {
        /// <summary>Pick from the file extension.</summary>
        Auto,

        /// <summary>Lossless raster image (PNG, BMP, TIFF): LSB matching in keyed pixel order.</summary>
        Image,

        /// <summary>Baseline JPEG: F5 in the DCT domain.</summary>
        Jpeg,

        /// <summary>RIFF WAVE: keyed LSB in the samples.</summary>
        Wav,

        /// <summary>Plain text: zero-width, whitespace or homoglyph coding.</summary>
        Text,

        /// <summary>PNG, JPEG or WAV metadata chunk, leaving pixels and samples alone.</summary>
        Metadata,
    }

    internal enum TextMethod
    {
        ZeroWidth,
        Whitespace,
        Homoglyph,
    }

    internal static class CarrierKinds
    {
        public static CarrierKind Detect(CarrierKind requested, string path)
        {
            if (requested != CarrierKind.Auto)
                return requested;

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png":
                case ".bmp":
                case ".tif":
                case ".tiff":
                    return CarrierKind.Image;
                case ".jpg":
                case ".jpeg":
                case ".jfif":
                    return CarrierKind.Jpeg;
                case ".wav":
                case ".wave":
                    return CarrierKind.Wav;
                case ".txt":
                case ".md":
                case ".html":
                case ".htm":
                case ".csv":
                case ".xml":
                case ".json":
                    return CarrierKind.Text;
                default:
                    throw new CliException($"Cannot tell the carrier type of '{path}' from its extension; pass --carrier.", ExitCodes.Usage);
            }
        }

        public static CarrierKind ParseKind(string value)
        {
            return value?.ToLowerInvariant() switch
            {
                null or "" or "auto" => CarrierKind.Auto,
                "image" or "png" or "bmp" => CarrierKind.Image,
                "jpeg" or "jpg" => CarrierKind.Jpeg,
                "wav" or "audio" => CarrierKind.Wav,
                "text" or "txt" => CarrierKind.Text,
                "metadata" or "meta" => CarrierKind.Metadata,
                _ => throw new CliException($"Unknown carrier '{value}'. Use auto, image, jpeg, wav, text or metadata.", ExitCodes.Usage),
            };
        }

        public static TextMethod ParseTextMethod(string value)
        {
            return value?.ToLowerInvariant() switch
            {
                null or "" or "zero-width" or "zerowidth" or "zw" => TextMethod.ZeroWidth,
                "whitespace" or "ws" => TextMethod.Whitespace,
                "homoglyph" or "hg" => TextMethod.Homoglyph,
                _ => throw new CliException($"Unknown text method '{value}'. Use zero-width, whitespace or homoglyph.", ExitCodes.Usage),
            };
        }
    }
}
