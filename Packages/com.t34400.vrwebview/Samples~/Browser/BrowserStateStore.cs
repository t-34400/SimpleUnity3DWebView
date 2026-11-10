using System;
using System.IO;
using System.Text;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Sample persistence. Codec is injected so policy/IO can run without Unity.</summary>
    public sealed class BrowserStateStore {
        private readonly string path;
        private readonly Func<string, BrowserSavedState> decode;
        private readonly Func<BrowserSavedState, string> encode;
        public bool IsReadOnly { get; private set; }
        public string Warning { get; private set; }
        public BrowserStateStore(string path, Func<string, BrowserSavedState> decode, Func<BrowserSavedState, string> encode) {
            this.path = path ?? throw new ArgumentNullException(nameof(path)); this.decode = decode ?? throw new ArgumentNullException(nameof(decode)); this.encode = encode ?? throw new ArgumentNullException(nameof(encode));
        }
        private BrowserSavedState Read(string name) {
            if (new FileInfo(name).Length > 2 * 1024 * 1024) throw new InvalidDataException("Browser data exceeds limit");
            return BrowserLibrary.Sanitize(decode(File.ReadAllText(name, Encoding.UTF8)));
        }
        public BrowserSavedState Load() {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return new BrowserSavedState();
            try { return Read(path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException) {
                IsReadOnly = true; Warning = "Saved browser data could not be loaded; existing files are preserved. Forget saved session to enable saving again.";
                try { return Read(path + ".bak"); } catch (Exception fallback) when (fallback is IOException || fallback is UnauthorizedAccessException || fallback is ArgumentException || fallback is NotSupportedException) { return new BrowserSavedState(); }
            }
        }
        public bool Save(BrowserSavedState state) {
            if (IsReadOnly) return false;
            string text = encode(BrowserLibrary.Sanitize(state));
            if (text == null || Encoding.UTF8.GetByteCount(text) > 2 * 1024 * 1024) { Warning = "Browser data exceeds the storage limit. Remove old history or bookmarks."; return false; }
            string temp = path + ".tmp";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    byte[] bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
                Warning = null; return true;
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) {
                Warning = "Browser data could not be saved. Check application storage."; return false;
            } finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        public void Forget() {
            // Explicit user action only; library memory can be retained and saved again.
            foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
            IsReadOnly = false; Warning = null;
        }
    }
}
