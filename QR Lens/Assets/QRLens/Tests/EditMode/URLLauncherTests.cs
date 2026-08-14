using NUnit.Framework;
using QRLens.Core;

namespace QRLens.Tests
{
    public sealed class URLLauncherTests
    {
        [TestCase("https://example.com")]
        [TestCase("http://example.com/path?q=qr")]
        [TestCase("  HTTPS://example.com/qr  ")]
        public void TryGetHttpUrl_AcceptsSafeAbsoluteLinks(string value)
        {
            Assert.That(URLLauncher.TryGetHttpUrl(value, out var url), Is.True);
            Assert.That(url, Is.Not.Null);
        }

        [TestCase("")]
        [TestCase("example.com")]
        [TestCase("ftp://example.com/file")]
        [TestCase("https:///missing-host")]
        [TestCase("https://user:password@example.com")]
        [TestCase("https://example.com/line\nbreak")]
        public void TryGetHttpUrl_RejectsUnsafeOrMalformedValues(string value)
        {
            Assert.That(URLLauncher.TryGetHttpUrl(value, out _), Is.False);
        }

        [Test]
        public void TryGetHttpUrl_RejectsOversizedValues()
        {
            var value = "https://example.com/" + new string('a', 8192);

            Assert.That(URLLauncher.TryGetHttpUrl(value, out _), Is.False);
        }
    }
}
