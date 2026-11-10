using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Sample-owned Unity texture lifetime, separate from native page/GPU outputs.</summary>
    internal sealed class BrowserFaviconTextures : IDisposable
    {
        private sealed class Entry { internal Texture2D Texture; internal int Users; internal long Used; }
        internal sealed class Lease : IDisposable {
            internal Texture2D Texture { get; }
            private Action release;
            internal Lease(Texture2D texture, Action release) { Texture = texture; this.release = release; }
            public void Dispose() { var action = release; release = null; action?.Invoke(); }
        }
        private readonly BrowserFaviconCache disk;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private long clock;
        private bool disposed;
        internal event Action<string> Changed;
        internal BrowserFaviconTextures(string directory) { disk = new BrowserFaviconCache(directory); }
        internal void Store(BrowserPageIcon icon) {
            if (disposed || icon == null || !icon.HasImage || !disk.Store(icon)) return;
            var handlers = Changed;
            if (handlers != null) foreach (Action<string> handler in handlers.GetInvocationList()) try { handler(icon.Address); } catch (Exception error) { Debug.LogException(error); }
        }
        internal Lease Acquire(string address) {
            if (disposed) return null;
            byte[] bytes = disk.Read(address); if (bytes == null) return null;
            string key = BrowserFaviconCache.Digest(bytes);
            if (!entries.TryGetValue(key, out var entry)) {
                if (entries.Count >= 256) {
                    var spare = entries.Where(p => p.Value.Users == 0).OrderBy(p => p.Value.Used).FirstOrDefault();
                    if (spare.Key == null) return null;
                    DestroyTexture(spare.Value.Texture); entries.Remove(spare.Key);
                }
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try {
                    if (!ImageConversion.LoadImage(texture, bytes, true) || texture.width > BrowserPageIcon.MaximumDimension || texture.height > BrowserPageIcon.MaximumDimension) {
                        DestroyTexture(texture); return null;
                    }
                } catch (Exception error) when (error is ArgumentException || error is UnityException) { DestroyTexture(texture); return null; }
                texture.name = "Browser favicon"; texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
                entry = new Entry { Texture = texture }; entries.Add(key, entry);
            }
            entry.Users++; entry.Used = ++clock;
            return new Lease(entry.Texture, () => { if (!disposed) { entry.Users--; entry.Used = ++clock; } });
        }
        private static void DestroyTexture(Texture2D texture) { if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture); }
        public void Dispose() {
            if (disposed) return; disposed = true; Changed = null;
            foreach (var entry in entries.Values) DestroyTexture(entry.Texture); entries.Clear();
        }
    }
    public sealed class BrowserFaviconView : MonoBehaviour
    {
        private BrowserFaviconTextures textures;
        private BrowserFaviconTextures.Lease lease;
        private RawImage image;
        private Text fallback;
        private string address;
        internal static BrowserFaviconView Add(Transform parent, Font font, float width = 28) {
            var node = BrowserSampleWidgets.Node(parent, "Site icon", typeof(LayoutElement), typeof(BrowserFaviconView));
            var size = node.GetComponent<LayoutElement>(); size.minWidth = size.preferredWidth = width; size.flexibleWidth = 0;
            size.minHeight = size.preferredHeight = width; size.flexibleHeight = 0;
            var view = node.GetComponent<BrowserFaviconView>(); view.image = BrowserSampleWidgets.Node(node.transform, "Icon image", typeof(RawImage)).GetComponent<RawImage>(); view.image.raycastTarget = false;
            view.image.rectTransform.sizeDelta = new Vector2(width, width);
            view.fallback = BrowserSampleWidgets.Label(view.image.transform, "Icon placeholder", "·", font, 18);
            view.fallback.alignment = TextAnchor.MiddleCenter; BrowserSampleWidgets.Stretch(view.fallback.rectTransform);
            return view;
        }
        public void Configure(BrowserDesktopPanel app, string url) {
            var service = app?.Favicons;
            if (service == textures && address == url) return;
            if (textures != null) textures.Changed -= IconChanged;
            lease?.Dispose(); lease = null; textures = service; address = url;
            if (isActiveAndEnabled && textures != null) textures.Changed += IconChanged;
            Refresh();
        }
        private void IconChanged(string source) {
            if (Uri.TryCreate(source, UriKind.Absolute, out var incoming) && Uri.TryCreate(address, UriKind.Absolute, out var current)
                && incoming.GetLeftPart(UriPartial.Authority) == current.GetLeftPart(UriPartial.Authority)) Refresh();
        }
        private void Refresh() {
            if (image == null) return;
            var next = isActiveAndEnabled ? textures?.Acquire(address) : null;
            if (next == null && lease != null) return; // Cache eviction must not blank a currently displayed icon.
            lease?.Dispose(); lease = next;
            image.texture = lease?.Texture; image.color = lease == null ? new Color(.18f, .28f, .4f) : Color.white;
            fallback.gameObject.SetActive(lease == null);
            fallback.text = Uri.TryCreate(address, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host.Substring(0, 1).ToUpperInvariant() : "·";
        }
        private void OnEnable() { if (textures != null) { textures.Changed += IconChanged; Refresh(); } }
        private void OnDisable() { if (textures != null) textures.Changed -= IconChanged; lease?.Dispose(); lease = null; if (image != null) image.texture = null; }
    }
}
