using System;
using UnityEngine;

namespace QRLens.Core
{
    public static class URLLauncher
    {
        private const int MaxUrlLength = 8192;

        public static bool TryGetHttpUrl(string payload, out Uri url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            var candidate = payload.Trim();
            if (candidate.Length > MaxUrlLength || ContainsControlCharacter(candidate))
            {
                return false;
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(parsed.Host) ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                !Uri.IsWellFormedUriString(candidate, UriKind.Absolute))
            {
                return false;
            }

            url = parsed;
            return true;
        }

        public static bool TryOpen(string payload)
        {
            if (!TryGetHttpUrl(payload, out var url))
            {
                return false;
            }

            Application.OpenURL(url.AbsoluteUri);
            return true;
        }

        private static bool ContainsControlCharacter(string value)
        {
            foreach (var character in value)
            {
                if (char.IsControl(character))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
