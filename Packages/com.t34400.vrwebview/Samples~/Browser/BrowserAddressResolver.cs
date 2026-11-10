using System;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Sample address-bar policy. Core's explicit URL policy remains unchanged.</summary>
    public static class BrowserAddressResolver
    {
        public static string Resolve(string input, string searchEndpoint = "https://duckduckgo.com/?q=") {
            string value = (input ?? "").Trim();
            if (value.Length == 0 || value.Length > 8192) throw new ArgumentException("Enter an address or search terms");
            if (value.Contains("://")) return NavigationPolicy.Parse(value).AbsoluteUri;
            // Schemes that are not web navigation must never become javascript/file URLs.
            if (value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unsupported address scheme");
            bool whitespace = false; foreach (char c in value) if (char.IsWhiteSpace(c)) { whitespace = true; break; }
            string host = value.Split('/')[0];
            if (!whitespace && (host.Contains(".") || host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || host.StartsWith("[")))
                return NavigationPolicy.Parse("https://" + value).AbsoluteUri;
            string endpoint = NavigationPolicy.Parse(searchEndpoint).AbsoluteUri;
            if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Search requires HTTPS");
            return endpoint + Uri.EscapeDataString(value);
        }
    }
}
