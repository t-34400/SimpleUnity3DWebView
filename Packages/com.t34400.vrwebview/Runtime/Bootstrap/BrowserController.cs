using System;
using System.Threading.Tasks;
using UnityEngine;

namespace T34400.VRWebView
{
    /// <summary>Optional scene composition. XR ray/input modules remain application-owned.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BrowserDispatcher))]
    public sealed class BrowserController : MonoBehaviour
    {
        [SerializeField] private BrowserView view;
        [SerializeField] private int width = 1024, height = 768;
        [SerializeField] private string initialUrl = "https://example.com/";
        [SerializeField] private bool enableKnownProviderImeWorkaround = true;
        [SerializeField] private bool enableDiagnostics;
        [SerializeField] private BrowserDisplayQuality displayQuality;
        public BrowserDisplayQuality DisplayQuality => displayQuality;
        private BrowserDispatcher dispatcher;
        private AndroidBrowserBackend backend;
        private BrowserSession session;
        private int generation;
        public BrowserSession Session => session;
        public AndroidBrowserBackend Backend => backend;
        public Exception LastError { get; private set; }
        public void Configure(BrowserView target, string address = "https://example.com/", bool diagnostics = false)
        {
            if (session != null) throw new InvalidOperationException("Configure before enabling the controller");
            view = target; initialUrl = address; enableDiagnostics = diagnostics;
        }
        private async void OnEnable()
        {
            int version = ++generation;
            try {
                dispatcher = GetComponent<BrowserDispatcher>(); dispatcher.InitializeOnOwnerThread();
                backend = new AndroidBrowserBackend(dispatcher, width, height, enableKnownProviderImeWorkaround, enableDiagnostics);
                backend.SetDisplayQuality(displayQuality);
                session = new BrowserSession(backend, dispatcher.Scheduler);
                if (view != null) view.Bind(backend.Surface, backend, dispatcher);
                var created = session;
                await created.InitializeAsync();
                if (generation == version && !string.IsNullOrWhiteSpace(initialUrl)) await created.NavigateAsync(initialUrl);
            } catch (OperationCanceledException) { }
            catch (Exception error) {
                if (version != generation) return;
                LastError = error; Debug.LogException(error, this); Close();
            }
        }
        public Task NavigateAsync(string address) => session != null ? session.NavigateAsync(address)
            : throw new InvalidOperationException("Enable and initialize BrowserController first");
        public void EndEditing() { backend?.EndEditing(); }
        public void SetDisplayQuality(BrowserDisplayQuality value) {
            BrowserDisplayQualityPolicy.Validate(value); backend?.SetDisplayQuality(value); displayQuality = value;
        }
        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }
        private void Close()
        {
            generation++;
            var old = session; session = null; var previousBackend = backend; backend = null;
            try { if (view != null) view.Unbind(); }
            finally { if (old != null) old.Dispose(); else previousBackend?.Dispose(); }
        }
    }
}
