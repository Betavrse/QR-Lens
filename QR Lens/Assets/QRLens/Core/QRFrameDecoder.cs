using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;

namespace QRLens.Core
{
    /// <summary>
    /// Decodes QR codes from an 8-bit grayscale frame. This code is platform-neutral so a
    /// future PICO camera provider can reuse the same local decoding path.
    /// </summary>
    public static class QRFrameDecoder
    {
        public static bool TryDecodeLuminance(
            byte[] luminance,
            int width,
            int height,
            out string payload,
            out bool hasTextPayload)
        {
            payload = string.Empty;
            hasTextPayload = true;

            if (luminance == null || width <= 0 || height <= 0 ||
                (long)width * height > luminance.LongLength)
            {
                return false;
            }

            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };

            var result = reader.Decode(luminance, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
            if (result == null)
            {
                return false;
            }

            if (result.Text != null)
            {
                payload = result.Text;
                return true;
            }

            var bytes = result.RawBytes;
            if (bytes == null || bytes.Length == 0)
            {
                return true;
            }

            hasTextPayload = false;
            var previewLength = Math.Min(bytes.Length, 24);
            var preview = BitConverter.ToString(bytes, 0, previewLength).Replace('-', ' ');
            payload =
                $"Binary QR payload ({bytes.Length} bytes): {preview}" +
                (bytes.Length > previewLength ? " …" : string.Empty);
            return true;
        }
    }
}
