using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Optional, bounded sample cache. No network requests or hidden page initialization.</summary>
    public sealed class BrowserFaviconCache
    {
        private readonly string directory;
        private readonly int capacity;
        private readonly Dictionary<string, byte[]> memory = new Dictionary<string, byte[]>();
        private readonly Queue<string> insertion = new Queue<string>();
        public BrowserFaviconCache(string directory, int capacity = 256)
        {
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity;
        }
        public static string Digest(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string Key(string identity) => Digest(Encoding.UTF8.GetBytes(identity));
        private static string Page(string address) { var uri = NavigationPolicy.Parse(address); return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri; }
        private static string Origin(string address) => NavigationPolicy.Parse(address).GetLeftPart(UriPartial.Authority);
        private void Remember(string key, byte[] bytes) {
            if (!memory.ContainsKey(key)) insertion.Enqueue(key);
            memory[key] = bytes;
            while (memory.Count > capacity) memory.Remove(insertion.Dequeue());
        }
        public bool Store(BrowserPageIcon icon)
        {
            if (icon == null || !icon.HasImage) return false;
            byte[] bytes = icon.CopyPng();
            string[] identities = { "page:" + Page(icon.Address), "origin:" + Origin(icon.Address) };
            foreach (string identity in identities) Remember(Key(identity), bytes);
            try {
                Directory.CreateDirectory(directory);
                // Exact page first; origin is only a fallback for saved pages never opened yet.
                foreach (string identity in identities) {
                    string file = Path.Combine(directory, Key(identity) + ".png"), temp = file + ".tmp";
                    try {
                        File.WriteAllBytes(temp, bytes);
                        if (File.Exists(file)) File.Delete(file); File.Move(temp, file);
                    } finally { if (File.Exists(temp)) File.Delete(temp); }
                }
                foreach (var file in new DirectoryInfo(directory).GetFiles("*.png").OrderByDescending(p => p.LastWriteTimeUtc).ThenBy(p => p.Name).Skip(capacity)) file.Delete();
                return true;
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return true; }
        }
        public byte[] Read(string address)
        {
            if (!BrowserLibrary.IsWebAddress(address)) return null;
            foreach (string identity in new[] { "page:" + Page(address), "origin:" + Origin(address) }) {
                try {
                    string key = Key(identity);
                    if (memory.TryGetValue(key, out var cached)) return (byte[])cached.Clone();
                    string file = Path.Combine(directory, key + ".png");
                    if (!File.Exists(file) || new FileInfo(file).Length > BrowserPageIcon.MaximumBytes) continue;
                    byte[] bytes = File.ReadAllBytes(file); if (BrowserPageIcon.IsSmallPng(bytes)) { Remember(key, bytes); return (byte[])bytes.Clone(); }
                } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }
            return null;
        }
    }
}
