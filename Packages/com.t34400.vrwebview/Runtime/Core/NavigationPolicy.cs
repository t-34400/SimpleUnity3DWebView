using System;

namespace T34400.VRWebView
{
    public static class NavigationPolicy
    {
        public static Uri Parse(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("An absolute HTTP(S) URL without embedded credentials is required.", nameof(address));
            return uri;
        }
    }
}
