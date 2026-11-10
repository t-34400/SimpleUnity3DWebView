using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView.Presentation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace T34400.VRWebView
{
    /// <summary>One Android WebView, direct native IME and a borrowed GPU surface.
    /// Public operations/Dispose run on the supplied Unity dispatcher owner thread.</summary>
    public sealed class AndroidBrowserBackend : IBrowserBackend, IBrowserHtmlBackend,
        IBrowserPointerSink, IBrowserScrollSink, IBrowserHoverSink, IBrowserAsyncEditingSink, IBrowserFailureSource, IBrowserNavigationBackend, IBrowserContentBackend, IBrowserIconSource, IBrowserDisplayQualityBackend
    {
        private readonly BrowserDispatcher owner;
        private readonly Action<Action> schedule;
        private int width, height;
        private static long nextPageId = 1L << 40;
        private long pageId, presentationVersion;
        private bool presentationWanted;
        public BrowserDisplayQuality DisplayQuality { get; private set; }
        private BrowserPageState pageSignal;
        private BrowserPageIcon iconSignal;
        public BrowserPageIcon PageIcon { get; private set; }
        public event Action<BrowserPageIcon> PageIconChanged;
        private BrowserFindResult findSignal;
        private BrowserContextTarget contextSignal;
        private readonly Dictionary<long, TaskCompletionSource<string>> scripts = new Dictionary<long, TaskCompletionSource<string>>();
        public event Action<BrowserFindResult> FindResultChanged;
        public event Action<BrowserContextTarget> ContextRequested;
        public BrowserPageState PageState { get; private set; } = BrowserPageState.Empty;
        public event Action<BrowserPageState> PageStateChanged;
        private readonly bool providerWorkaround, diagnostics;
        private readonly IDisposable registration;
        private readonly object pendingGate = new object();
        private readonly Dictionary<long, TaskCompletionSource<bool>> pending = new Dictionary<long, TaskCompletionSource<bool>>();
        private readonly TaskCompletionSource<bool> initialized = NewCompletion();
        private readonly PointerCapture capture = new PointerCapture();
        private readonly HoverCapture hover = new HoverCapture();
        private BrowserGpuPump pump;
        private RenderTexture texture;
        private AndroidJavaClass bridge;
        private Callback callback;
        private ulong id;
        private long nextOperation;
        private float nextStatistics;
        private int readySignal, cancelInitialization;
        private string failureSignal;
        private bool started, ready, disposed;
        public BrowserTextureSource Surface { get; }
        public event Action<Exception> Failed;
        public Exception LastError { get; private set; }
        public string GraphicsStatus { get { owner.VerifyAccess(); return id == 0 ? (disposed ? "Browser closed" : "Page has no visible presentation") : BrowserGpuNative.Status(id); } }

        public AndroidBrowserBackend(BrowserDispatcher dispatcher, int width = 1024, int height = 768,
            bool enableKnownProviderImeWorkaround = true, bool enableDiagnostics = false, bool presentAutomatically = true)
        {
            owner = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)); owner.VerifyAccess(); schedule = owner.Scheduler;
            if (width < 128 || width > 2048 || height < 128 || height > 2048) throw new ArgumentOutOfRangeException(nameof(width), "Extent must be 128..2048 per axis");
            this.width = width; this.height = height; presentationWanted = presentAutomatically; providerWorkaround = enableKnownProviderImeWorkaround; diagnostics = enableDiagnostics;
            Surface = new BrowserTextureSource(owner); registration = owner.Track(this);
            capture.Received += ForwardPointer;
            hover.Received += ForwardHover;
        }
        private static TaskCompletionSource<bool> NewCompletion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync(CancellationToken token)
        {
            Verify(); token.ThrowIfCancellationRequested();
            if (started) return WaitForInitialization(token);
            started = true;
            try {
                if (Application.platform != RuntimePlatform.Android || Application.isEditor)
                    throw new PlatformNotSupportedException("Android ARM64 player required; Editor does not emulate a browser");
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3)
                    throw new PlatformNotSupportedException("Android Vulkan or OpenGL ES 3 required");
                pump = BrowserGpuPump.Ensure();
                pump.Tick += Tick; pump.FocusChanged += FocusChanged; pump.PauseChanged += PauseChanged;
                pageId = Interlocked.Increment(ref nextPageId);
                bridge = new AndroidJavaClass("com.t34400.vrwebview.BrowserHost"); callback = new Callback(this);
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("openPage", activity, pageId, width, height, providerWorkaround, diagnostics, callback);
            } catch (Exception error) { Fault(error); }
            return WaitForInitialization(token);
        }
        private async Task WaitForInitialization(CancellationToken token)
        {
            using (token.Register(() => { Interlocked.Exchange(ref cancelInitialization, 1); initialized.TrySetCanceled(); }))
                await initialized.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        private void Tick()
        {
            if (disposed) return;
            if (Interlocked.Exchange(ref cancelInitialization, 0) != 0) { Dispose(); return; }
            string failure = Interlocked.Exchange(ref failureSignal, null);
            if (failure != null) { Fault(new InvalidOperationException("Android browser failed: " + failure)); return; }
            if (id != 0 && BrowserGpuNative.VrFailed(id) != 0) { Fault(new InvalidOperationException(BrowserGpuNative.Status(id))); return; }
            if (Interlocked.Exchange(ref readySignal, 0) != 0 && !ready) {
                ready = true;
                // Raw hardware Canvas rows are top-first, as in the proven GPU probe.
                initialized.TrySetResult(true);
                if (presentationWanted) _ = ApplyPresentationAsync(presentationVersion);
            }
            if (diagnostics && Time.unscaledTime >= nextStatistics) {
                nextStatistics = Time.unscaledTime + 1;
                string counters = "";
                if (id != 0) foreach (string line in BrowserGpuNative.Status(id).Split('\n'))
                    if (line.StartsWith("GPU copies=", StringComparison.Ordinal)) { counters = " " + line; break; }
                int operations; lock (pendingGate) operations = pending.Count;
                Debug.Log("[VRWebViewRuntime] event=presentation_stats page=" + pageId + " target=" + id + " visible=" + (id != 0) + " pendingOperations=" + operations + counters);
            }
            var find = Interlocked.Exchange(ref findSignal, null);
            if (find != null) Notify(FindResultChanged, find);
            var context = Interlocked.Exchange(ref contextSignal, null);
            if (context != null) Notify(ContextRequested, context);
            var observed = Interlocked.Exchange(ref pageSignal, null);
            if (observed != null) {
                PageState = observed;
                var handlers = PageStateChanged;
                if (handlers != null) foreach (Action<BrowserPageState> handler in handlers.GetInvocationList())
                    try { handler(observed); } catch (Exception error) { Debug.LogException(error); }
            }
            var icon = Interlocked.Exchange(ref iconSignal, null);
            if (icon != null) { PageIcon = icon; Notify(PageIconChanged, icon); }
        }
        private static void Notify<T>(Action<T> handlers, T value) {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception error) { Debug.LogException(error); }
        }
        public async Task<string> EvaluateJavaScriptAsync(string script, CancellationToken token = default) {
            VerifyReady(); token.ThrowIfCancellationRequested();
            BrowserContentPolicy.ValidateScript(script);
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously); long operation;
            lock (pendingGate) { if (scripts.Count >= 32) throw new InvalidOperationException("Too many scripts pending"); operation = checked(++nextOperation); scripts.Add(operation, completion); }
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using (timeout.Token.Register(() => CompleteScript(operation, null, new OperationCanceledException(timeout.Token)))) {
                    try { bridge.CallStatic("evaluate", pageId, operation, script); }
                    catch (Exception error) { CompleteScript(operation, null, error); }
                    return await completion.Task.ConfigureAwait(false);
                }
            }
        }
        private void CompleteScript(long operation, string result, Exception error) {
            TaskCompletionSource<string> completion;
            lock (pendingGate) { if (!scripts.TryGetValue(operation, out completion)) return; scripts.Remove(operation); }
            if (error is OperationCanceledException) completion.TrySetCanceled();
            else if (error != null) completion.TrySetException(error);
            else completion.TrySetResult(result ?? "null");
        }
        public Task SetTextZoomAsync(int percent, CancellationToken token = default) {
            VerifyReady(); BrowserContentPolicy.ValidateTextZoom(percent);
            return Submit("textZoom", token, percent);
        }
        public Task FindAsync(string query, long revision, CancellationToken token = default) {
            VerifyReady(); BrowserContentPolicy.ValidateFind(query, revision);
            return Submit("find", token, query, revision);
        }
        public Task FindNextAsync(bool forward, CancellationToken token = default) { VerifyReady(); return Submit("findNext", token, forward); }
        public Task ClearFindAsync(CancellationToken token = default) { VerifyReady(); return Submit("clearFind", token); }
        public Task NavigateAsync(Uri url, CancellationToken token)
        {
            VerifyReady(); token.ThrowIfCancellationRequested();
            if (url == null) throw new ArgumentNullException(nameof(url));
            string address = NavigationPolicy.Parse(url.AbsoluteUri).AbsoluteUri;
            return Submit("navigate", token, address);
        }
        public Task LoadHtmlAsync(string html, Uri baseUrl, CancellationToken token)
        {
            VerifyReady(); token.ThrowIfCancellationRequested();
            if (html == null) throw new ArgumentNullException(nameof(html));
            if (html.Length > 2 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(html), "HTML exceeds two million UTF-16 code units");
            if (baseUrl == null) throw new ArgumentNullException(nameof(baseUrl));
            return Submit("loadHtml", token, html, NavigationPolicy.Parse(baseUrl.AbsoluteUri).AbsoluteUri);
        }
        private Task Submit(string method, CancellationToken token, params object[] arguments)
        {
            token.ThrowIfCancellationRequested();
            var completion = NewCompletion(); long operation;
            lock (pendingGate) {
                if (pending.Count >= 256) throw new InvalidOperationException("Too many browser operations");
                operation = checked(++nextOperation); pending.Add(operation, completion);
            }
            var values = new object[arguments.Length + 2]; values[0] = pageId; values[1] = operation;
            Array.Copy(arguments, 0, values, 2, arguments.Length);
            try { bridge.CallStatic(method, values); }
            catch (Exception error) { Complete(operation, error); }
            return AwaitOperation(operation, completion.Task, token);
        }
        private async Task AwaitOperation(long operation, Task task, CancellationToken token)
        {
            using (token.Register(() => Complete(operation, new OperationCanceledException(token))))
                await task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        private void Complete(long operation, Exception error)
        {
            TaskCompletionSource<bool> completion;
            lock (pendingGate) {
                if (!pending.TryGetValue(operation, out completion)) return;
                pending.Remove(operation);
            }
            if (error == null) completion.TrySetResult(true);
            else if (error is OperationCanceledException) completion.TrySetCanceled();
            else completion.TrySetException(error);
        }
        public void SendPointer(BrowserPointerEvent value)
        {
            owner.VerifyAccess();
            // Withdrawal can cancel a view's old gesture after this backend has closed.
            // Its native capture was already cancelled by Dispose; repeated cancel is harmless.
            if (value.Phase == PointerPhase.Cancel && (disposed || !ready)) return;
            VerifyReady();
            if (value.Phase == PointerPhase.Down) hover.Exit();
            capture.Send(value.Id, value.Phase, value.X, value.Y);
        }
        private void ForwardPointer(BrowserPointerEvent value)
        {
            int action = value.Phase == PointerPhase.Down ? 0 : value.Phase == PointerPhase.Up ? 1 : value.Phase == PointerPhase.Move ? 2 : 3;
            bridge?.CallStatic("pointer", pageId, action, value.X, value.Y);
        }
        public void SendHover(BrowserHoverEvent value)
        {
            owner.VerifyAccess();
            if (value.Phase == HoverPhase.Exit && (disposed || !ready)) return;
            VerifyReady(); if (capture.IsCaptured || (!Application.isFocused && value.Phase != HoverPhase.Exit)) return;
            hover.Send(value.Id, value.Phase, value.X, value.Y);
        }
        private void ForwardHover(BrowserHoverEvent value)
        {
            int action = value.Phase == HoverPhase.Enter ? 9 : value.Phase == HoverPhase.Move ? 7 : 10;
            bridge?.CallStatic("hover", pageId, action, value.X, value.Y);
        }
        public void SendScroll(BrowserScrollEvent value)
        {
            VerifyReady();
            if (float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsInfinity(value.X) || float.IsInfinity(value.Y)
                || float.IsNaN(value.DeltaX) || float.IsNaN(value.DeltaY) || float.IsInfinity(value.DeltaX) || float.IsInfinity(value.DeltaY)) return;
            bridge.CallStatic("scroll", pageId, value.X, value.Y, value.DeltaX, value.DeltaY);
        }
        public void EndEditing() {
            owner.VerifyAccess(); if (disposed || !ready) return;
            capture.Cancel(); hover.Exit(); bridge.CallStatic("endEditing", pageId);
        }
        public Task EndEditingAsync(CancellationToken token) {
            owner.VerifyAccess(); if (disposed || !ready) return Task.CompletedTask;
            capture.Cancel(); hover.Exit(); return Submit("endEditingAsync", token);
        }
        public Task ExecuteAsync(BrowserPageCommand command, CancellationToken token) {
            VerifyReady();
            if (!Enum.IsDefined(typeof(BrowserPageCommand), command)) throw new ArgumentOutOfRangeException(nameof(command));
            return Submit("command", token, command.ToString().ToLowerInvariant());
        }
        /// <summary>A visible presentation lease. Hiding preserves the WebView/DOM/media policy.
        /// A fresh output generation avoids writing old queued frames into a new binding.</summary>
        public void SetPresentation(bool visible, int viewportWidth = 0, int viewportHeight = 0) {
            owner.VerifyAccess(); if (disposed) return; // A late view withdrawal is idempotent.
            if (viewportWidth != 0 && (viewportWidth < 128 || viewportWidth > 2048 || viewportHeight < 128 || viewportHeight > 2048))
                throw new ArgumentOutOfRangeException(nameof(viewportWidth));
            bool resized = viewportWidth != 0 && (width != viewportWidth || height != viewportHeight);
            if (viewportWidth != 0) { width = viewportWidth; height = viewportHeight; }
            if (presentationWanted == visible && !resized) return;
            presentationWanted = visible; long version = ++presentationVersion;
            if (ready) _ = ApplyPresentationAsync(version);
        }
        public void SetDisplayQuality(BrowserDisplayQuality value) {
            Verify(); BrowserDisplayQualityPolicy.Validate(value);
            if (DisplayQuality == value) return;
            bool rebuild = BrowserDisplayQualityPolicy.UsesMipmaps(DisplayQuality) != BrowserDisplayQualityPolicy.UsesMipmaps(value);
            DisplayQuality = value;
            if (rebuild) {
                long version = ++presentationVersion;
                if (ready && presentationWanted) _ = ApplyPresentationAsync(version);
            } else if (texture != null) ApplySampling(texture);
            LogDisplayQuality();
        }
        private void ApplySampling(RenderTexture target) {
            target.filterMode = BrowserDisplayQualityPolicy.UsesMipmaps(DisplayQuality) ? FilterMode.Trilinear : FilterMode.Bilinear;
            target.anisoLevel = BrowserDisplayQualityPolicy.Anisotropy(DisplayQuality);
            if (target.useMipMap) target.wrapMode = TextureWrapMode.Clamp;
        }
        private void LogDisplayQuality() {
            if (!diagnostics) return;
            Debug.Log("[VRWebViewRuntime] event=display_quality page=" + pageId + " mode=" + DisplayQuality
                + " visible=" + (id != 0) + " generation=" + presentationVersion + " width=" + width + " height=" + height
                + " mips=" + (texture != null ? texture.mipmapCount : 0)
                + " filter=" + (texture != null ? texture.filterMode.ToString() : "none")
                + " aniso=" + (texture != null ? texture.anisoLevel : 0)
                + " mipTargets=" + (pump != null ? pump.MipTargetCount : 0)
                + " globalAniso=" + QualitySettings.anisotropicFiltering + " qualityLevel=" + QualitySettings.GetQualityLevel());
        }
        private async Task ApplyPresentationAsync(long version) {
            try {
                if (disposed || version != presentationVersion) return;
                var previousId = id; var previousTexture = texture; id = 0; texture = null;
                if (previousId != 0) pump.Retire(previousId, previousTexture, diagnostics);
                Surface.Withdraw();
                if (disposed || version != presentationVersion) return;
                if (!presentationWanted) { await Submit("present", CancellationToken.None, 0L, width, height); return; }
                bool mipmaps = BrowserDisplayQualityPolicy.UsesMipmaps(DisplayQuality);
                var descriptor = new RenderTextureDescriptor(width, height) {
                    graphicsFormat = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = 0, msaaSamples = 1, useMipMap = mipmaps, autoGenerateMips = false,
                    mipCount = mipmaps ? Texture.GenerateAllMips : 1,
                    volumeDepth = 1, dimension = TextureDimension.Tex2D
                };
                texture = new RenderTexture(descriptor) { name = "VRWebView visible target" };
                ApplySampling(texture);
                if (!texture.Create()) throw new InvalidOperationException("Browser target allocation failed");
                Graphics.Blit(Texture2D.blackTexture, texture);
                id = BrowserGpuNative.VrCreate(width, height, texture.GetNativeTexturePtr());
                if (id == 0) throw new InvalidOperationException(BrowserGpuNative.Status(0));
                pump.RegisterMipTarget(id, texture);
                LogDisplayQuality();
                var target = texture;
                // Completion resumes on the supplied owner context explicitly, never a worker.
                var task = Submit("present", CancellationToken.None, (long)id, width, height);
                await task.ConfigureAwait(false);
                schedule(() => { if (!disposed && version == presentationVersion && presentationWanted && texture == target)
                    Surface.Publish(target, BrowserSurfaceOrigin.TopLeft); });
            } catch (Exception error) {
                try { schedule(() => { if (!disposed && version == presentationVersion) Fault(error); }); }
                catch (ObjectDisposedException) { }
            }
        }
        private void FocusChanged(bool focused) { if (!focused && !disposed) { try { capture.Cancel(); } finally { hover.Exit(); } } }
        private void PauseChanged(bool paused)
        {
            if (disposed || bridge == null) return;
            if (paused) { try { capture.Cancel(); } finally { hover.Exit(); } }
            bridge.CallStatic("setPaused", pageId, paused);
        }
        private void Verify() { owner.VerifyAccess(); if (disposed) throw new ObjectDisposedException(nameof(AndroidBrowserBackend)); }
        private void VerifyReady() { Verify(); if (!ready) throw new InvalidOperationException("Await browser initialization first"); }
        private void Fault(Exception error)
        {
            LastError = error; initialized.TrySetException(error);
            Debug.LogError("[VRWebViewRuntime] event=failed type=" + error.GetType().Name);
            if (diagnostics) Debug.LogException(error);
            // Settle submitted operations as failures before disposal cancels the rest.
            long[] operations;
            lock (pendingGate) { operations = new long[pending.Count]; pending.Keys.CopyTo(operations, 0); }
            foreach (long operation in operations) Complete(operation, error);
            Dispose();
            var handlers = Failed;
            if (handlers != null) foreach (Action<Exception> handler in handlers.GetInvocationList())
                try { handler(error); } catch (Exception notification) { Debug.LogException(notification); }
        }
        public void Dispose()
        {
            owner.VerifyAccess(); if (disposed) return;
            disposed = true; ready = false;
            initialized.TrySetCanceled();
            TaskCompletionSource<bool>[] operations;
            lock (pendingGate) { operations = new TaskCompletionSource<bool>[pending.Count]; pending.Values.CopyTo(operations, 0); pending.Clear(); }
            foreach (var operation in operations) operation.TrySetCanceled();
            TaskCompletionSource<string>[] evaluations;
            lock (pendingGate) { evaluations = new TaskCompletionSource<string>[scripts.Count]; scripts.Values.CopyTo(evaluations, 0); scripts.Clear(); }
            foreach (var evaluation in evaluations) evaluation.TrySetCanceled();
            FindResultChanged = null; ContextRequested = null;
            try { capture.Cancel(); }
            catch (Exception error) { Debug.LogException(error); }
            try { hover.Exit(); }
            catch (Exception error) { Debug.LogException(error); }
            // Withdraw synchronously before target retirement so bound views stop sampling it.
            Surface.Dispose();
            if (pump != null) { pump.Tick -= Tick; pump.FocusChanged -= FocusChanged; pump.PauseChanged -= PauseChanged; }
            if (id != 0) { pump.Retire(id, texture, diagnostics); id = 0; texture = null; }
            else if (texture != null) { UnityEngine.Object.Destroy(texture); texture = null; }
            if (pageId != 0) {
                try { bridge?.CallStatic("close", pageId); }
                catch (Exception error) { Debug.LogException(error); } // Without acknowledgement retain queued resources.
                pageId = 0;
            }
            bridge?.Dispose(); bridge = null; callback = null;
            registration.Dispose();
        }
        private sealed class Callback : AndroidJavaProxy
        {
            private readonly AndroidBrowserBackend backend;
            internal Callback(AndroidBrowserBackend backend) : base("com.t34400.vrwebview.BrowserHost$Callback") { this.backend = backend; }
            [UnityEngine.Scripting.Preserve] public void pageIcon(string address, string png) {
                if (png == null || png.Length > ((BrowserPageIcon.MaximumBytes + 2) / 3) * 4) return;
                try { Interlocked.Exchange(ref backend.iconSignal, new BrowserPageIcon(address, Convert.FromBase64String(png))); }
                catch (ArgumentException) { } catch (FormatException) { }
            }
            [UnityEngine.Scripting.Preserve] public void pageState(string address, string title, bool back, bool forward, bool loading, int progress, int error) {
                Interlocked.Exchange(ref backend.pageSignal, new BrowserPageState(address, title, back, forward, loading, progress, error));
            }
            [UnityEngine.Scripting.Preserve] public void evaluated(long operation, string result, bool success) {
                backend.CompleteScript(operation, result, success ? null : new InvalidOperationException("Script evaluation failed"));
            }
            [UnityEngine.Scripting.Preserve] public void found(long revision, int active, int count, bool complete) { Interlocked.Exchange(ref backend.findSignal, new BrowserFindResult(revision, active, count, complete)); }
            [UnityEngine.Scripting.Preserve] public void context(string address, bool image) {
                try { Interlocked.Exchange(ref backend.contextSignal, new BrowserContextTarget(address, image)); } catch (ArgumentException) { }
            }
            [UnityEngine.Scripting.Preserve] public void ready() { Interlocked.Exchange(ref backend.readySignal, 1); }
            [UnityEngine.Scripting.Preserve] public void completed(long operation, bool success, string code) {
                backend.Complete(operation, success ? null : new InvalidOperationException("Android browser operation failed: " + code));
            }
            [UnityEngine.Scripting.Preserve] public void failed(string code) { Interlocked.Exchange(ref backend.failureSignal, code ?? "unknown"); }
        }
    }
}
