using NUnit.Framework;
using QRLens.Core;
using ZXing;
using ZXing.Common;

namespace QRLens.Tests
{
    public sealed class QRFrameDecoderTests
    {
        [Test]
        public void TryDecodeLuminance_DecodesGeneratedQrPayload()
        {
            const string expected = "https://example.com";
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions { Width = 320, Height = 320, Margin = 4 }
            };
            var pixels = writer.Write(expected);
            var luminance = new byte[pixels.Width * pixels.Height];

            for (var index = 0; index < luminance.Length; index++)
            {
                // PixelData uses four equal RGB channels for a black-and-white QR image.
                luminance[index] = pixels.Pixels[index * 4];
            }

            var decoded = QRFrameDecoder.TryDecodeLuminance(
                luminance,
                pixels.Width,
                pixels.Height,
                out var payload,
                out var hasTextPayload);

            Assert.That(decoded, Is.True);
            Assert.That(payload, Is.EqualTo(expected));
            Assert.That(hasTextPayload, Is.True);
        }

        [Test]
        public void TryDecodeLuminance_ReturnsFalseForBlankFrame()
        {
            var decoded = QRFrameDecoder.TryDecodeLuminance(
                new byte[320 * 240],
                320,
                240,
                out _,
                out _);

            Assert.That(decoded, Is.False);
        }

        [Test]
        public void TryDecodeLuminance_RejectsInvalidFrames()
        {
            Assert.That(
                QRFrameDecoder.TryDecodeLuminance(null, 10, 10, out _, out _),
                Is.False);
            Assert.That(
                QRFrameDecoder.TryDecodeLuminance(new byte[4], 0, 2, out _, out _),
                Is.False);
            Assert.That(
                QRFrameDecoder.TryDecodeLuminance(new byte[3], 2, 2, out _, out _),
                Is.False);
        }
    }
}
