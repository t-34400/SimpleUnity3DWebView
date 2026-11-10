using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using T34400.VRWebView;
using T34400.VRWebView.Samples.Browser;
internal static class BrowserFaviconTests
{
    private static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    private static void WithCache(Action<string> action) { string root = Path.Combine(Path.GetTempPath(), "vrwebview-favicon-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); try { action(root); } finally { Directory.Delete(root, true); } }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("add tab stays inline until the whole tab row no longer fits", () => {
            Check(!BrowserTabStripPolicy.ShouldDock(3, 736), "Exact fit docked");
            Check(BrowserTabStripPolicy.ShouldDock(4, 736), "Overflow not docked");
            Check(!BrowserTabStripPolicy.ShouldDock(4, 964), "Dock did not release after expanding");
            Check(!BrowserTabStripPolicy.ShouldDock(3, 736), "Dock did not release after closing");
        });
        yield return ("icons own their data and reject oversized or non-web payloads", () => {
            byte[] source = Png(); var icon = new BrowserPageIcon("https://EXAMPLE.org:443/", source); source[0] = 0;
            Check(icon.Address == "https://example.org/" && icon.HasImage && icon.CopyPng()[0] == 137, "Borrowed caller buffer");
            byte[] copy = icon.CopyPng(); copy[0] = 0; Check(icon.CopyPng()[0] == 137, "Mutable snapshot");
            byte[] wide = Png(); wide[19] = 65; Check(!BrowserPageIcon.IsSmallPng(wide), "Oversized dimensions accepted");
            Check(!BrowserPageIcon.IsSmallPng(new byte[32769]), "Oversized payload accepted");
            bool rejected = false; try { _ = new BrowserPageIcon("file:///bad", Png()); } catch (ArgumentException) { rejected = true; } Check(rejected, "Non-web icon");
        });
        yield return ("cache survives restart and separates page icons while offering an origin fallback", () => WithCache(root => {
            var cache = new BrowserFaviconCache(root); byte[] a = Png(), b = Png(); b[b.Length - 1] ^= 1;
            cache.Store(new BrowserPageIcon("https://example.org/a#one", a)); cache.Store(new BrowserPageIcon("https://example.org/b", b));
            var restarted = new BrowserFaviconCache(root);
            Check(restarted.Read("https://example.org/a#two").SequenceEqual(a), "Exact page icon overwritten");
            Check(restarted.Read("https://example.org/not-visited").SequenceEqual(b), "Origin fallback missing");
            Check(restarted.Read("http://example.org/") == null && restarted.Read("https://example.org:8443/") == null, "Origin isolation");
            Check(restarted.Read("javascript:bad") == null, "Invalid address read");
        }));
        yield return ("corrupt cached images fall back without modifying saved browser data", () => WithCache(root => {
            var cache = new BrowserFaviconCache(root); cache.Store(new BrowserPageIcon("https://example.org/", Png()));
            foreach (var file in Directory.GetFiles(root, "*.png")) File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            Check(new BrowserFaviconCache(root).Read("https://example.org/") == null, "Corrupt cache returned");
        }));
        yield return ("favicon disk and memory caches have a fixed capacity", () => WithCache(root => {
            var cache = new BrowserFaviconCache(root, 4); cache.Store(new BrowserPageIcon("https://old.example/", Png()));
            foreach (var file in Directory.GetFiles(root, "*.png")) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
            cache.Store(new BrowserPageIcon("https://second.example/", Png())); cache.Store(new BrowserPageIcon("https://new.example/", Png()));
            Check(Directory.GetFiles(root, "*.png").Length == 4, "Unbounded disk cache");
            Check(cache.Read("https://old.example/") == null && new BrowserFaviconCache(root).Read("https://new.example/") != null, "Memory eviction or fresh entry");
        }));
        yield return ("icons remain usable in memory when cache storage is unavailable", () => WithCache(root => {
            string file = Path.Combine(root, "not-a-directory"); File.WriteAllText(file, "occupied");
            var cache = new BrowserFaviconCache(file); Check(cache.Store(new BrowserPageIcon("https://example.org/", Png())), "Live icon rejected");
            Check(cache.Read("https://example.org/") != null, "Live icon needs disk");
        }));
    }
}
