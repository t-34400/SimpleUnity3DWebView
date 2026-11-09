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
        IBrowserPointerSink, IBrowserScrollSink, IBrowserHoverSink, IBrowserEditingSink, IBrowserFailureSource
    {
        private readonly BrowserDispatcher owner;
        private readonly int width, height;
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
        private int readySignal, cancelInitialization;
        private string failureSignal;
        private bool started, ready, disposed;
        public BrowserTextureSource Surface { get; }
        public event Action<Exception> Failed;
        public Exception LastError { get; private set; }
        public string GraphicsStatus { get { owner.VerifyAccess(); return id == 0 ? "Browser closed" : BrowserGpuNative.Status(id); } }

        public AndroidBrowserBackend(BrowserDispatcher dispatcher, int width = 1024, int height = 768,
            bool enableKnownProviderImeWorkaround = true, bool enableDiagnostics = false)
        {
            owner = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)); owner.VerifyAccess();
            if (width < 128 || width > 2048 || height < 128 || height > 2048) throw new ArgumentOutOfRangeException(nameof(width), "Extent must be 128..2048 per axis");
            this.width = width; this.height = height; providerWorkaround = enableKnownProviderImeWorkaround; diagnostics = enableDiagnostics;
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
                var descriptor = new RenderTextureDescriptor(width, height) {
                    graphicsFormat = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = 0, msaaSamples = 1, useMipMap = false, autoGenerateMips = false,
                    volumeDepth = 1, dimension = TextureDimension.Tex2D
                };
                texture = new RenderTexture(descriptor) { name = "VRWebView browser target", filterMode = FilterMode.Bilinear };
                if (!texture.Create()) throw new InvalidOperationException("Browser RenderTexture allocation failed");
                id = BrowserGpuNative.VrCreate(width, height, texture.GetNativeTexturePtr());
                if (id == 0) throw new InvalidOperationException(BrowserGpuNative.Status(0));
                bridge = new AndroidJavaClass("com.t34400.vrwebview.BrowserHost"); callback = new Callback(this);
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("open", activity, (long)id, width, height, providerWorkaround, diagnostics, callback);
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
                Surface.Publish(texture, BrowserSurfaceOrigin.TopLeft);
                if (!disposed) initialized.TrySetResult(true);
            }
        }
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
            var completion = NewCompletion(); long operation;
            lock (pendingGate) {
                if (pending.Count >= 256) throw new InvalidOperationException("Too many browser operations");
                operation = checked(++nextOperation); pending.Add(operation, completion);
            }
            var values = new object[arguments.Length + 2]; values[0] = (long)id; values[1] = operation;
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
            bridge?.CallStatic("pointer", (long)id, action, value.X, value.Y);
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
            bridge?.CallStatic("hover", (long)id, action, value.X, value.Y);
        }
        public void SendScroll(BrowserScrollEvent value)
        {
            VerifyReady();
            if (float.IsNaN(value.X) || float.IsNaN(value.Y) || float.IsInfinity(value.X) || float.IsInfinity(value.Y)
                || float.IsNaN(value.DeltaX) || float.IsNaN(value.DeltaY) || float.IsInfinity(value.DeltaX) || float.IsInfinity(value.DeltaY)) return;
            bridge.CallStatic("scroll", (long)id, value.X, value.Y, value.DeltaX, value.DeltaY);
        }
        public void EndEditing() { VerifyReady(); capture.Cancel(); hover.Exit(); bridge.CallStatic("endEditing", (long)id); }
        private void FocusChanged(bool focused) { if (!focused && !disposed) { try { capture.Cancel(); } finally { hover.Exit(); } } }
        private void PauseChanged(bool paused)
        {
            if (disposed || bridge == null) return;
            if (paused) { try { capture.Cancel(); } finally { hover.Exit(); } }
            bridge.CallStatic("setPaused", (long)id, paused);
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
            try { capture.Cancel(); }
            catch (Exception error) { Debug.LogException(error); }
            try { hover.Exit(); }
            catch (Exception error) { Debug.LogException(error); }
            // Withdraw synchronously before target retirement so bound views stop sampling it.
            Surface.Dispose();
            if (pump != null) { pump.Tick -= Tick; pump.FocusChanged -= FocusChanged; pump.PauseChanged -= PauseChanged; }
            if (id != 0) {
                try {
                    if (bridge != null) bridge.CallStatic("close", (long)id);
                    else BrowserGpuNative.VrAbandon(id); // No Java producer was opened.
                } catch (Exception error) {
                    // No producer acknowledgement: retain the target, never abandon a live producer.
                    Debug.LogException(error);
                }
                pump.Retire(id, texture); id = 0; texture = null;
            } else if (texture != null) { UnityEngine.Object.Destroy(texture); texture = null; }
            bridge?.Dispose(); bridge = null; callback = null;
            registration.Dispose();
        }
        private sealed class Callback : AndroidJavaProxy
        {
            private readonly AndroidBrowserBackend backend;
            internal Callback(AndroidBrowserBackend backend) : base("com.t34400.vrwebview.BrowserHost$Callback") { this.backend = backend; }
            [UnityEngine.Scripting.Preserve] public void ready() { Interlocked.Exchange(ref backend.readySignal, 1); }
            [UnityEngine.Scripting.Preserve] public void completed(long operation, bool success, string code) {
                backend.Complete(operation, success ? null : new InvalidOperationException("Android browser operation failed: " + code));
            }
            [UnityEngine.Scripting.Preserve] public void failed(string code) { Interlocked.Exchange(ref backend.failureSignal, code ?? "unknown"); }
        }
    }
}
