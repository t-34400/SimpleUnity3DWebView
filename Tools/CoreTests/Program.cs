using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;
using T34400.VRWebView.Editor;
using System.Linq;
using System.Xml.Linq;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Rejects<T>(Task task) where T : Exception
    { try { await task; } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Test(string label, Func<Task> action)
    { await action(); passed++; Console.WriteLine("PASS " + label); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    private sealed class Backend : IBrowserBackend
    {
        public readonly TaskCompletionSource<bool> Init = new TaskCompletionSource<bool>();
        public int Initializations, Disposals, Navigations;
        public Uri LastUrl;
        public Task InitializeAsync(CancellationToken token)
        { Initializations++; token.Register(() => Init.TrySetCanceled()); return Init.Task; }
        public Task NavigateAsync(Uri url, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Navigations++; LastUrl = url; return Task.CompletedTask; }
        public void Dispose() { Disposals++; }
    }

    public static async Task Main()
    {
        await Test("host automatic startup waits for readiness and is consumed before dispatch", () => Sync(() => {
            var timing = new HostTrialTiming();
            Check(!timing.TryTakeStartup(100, true, false, true, true, true, out _), "Unarmed menu started a trial");
            timing.ArmStartup("native-webinfo", 10);
            Check(!timing.TryTakeStartup(12.99f, true, false, true, true, true, out _), "Startup delay was shortened");
            Check(timing.TryTakeStartup(13, true, false, true, true, true, out var mode) && mode == "native-webinfo", "Due ready trial was lost");
            Check(timing.StartupCase == "menu", "Pending case survived dispatch");
            Check(!timing.TryTakeStartup(100, true, false, true, true, true, out _), "Trial replayed after dispatch");
        }));
        await Test("host startup survives each readiness interruption without restarting its deadline", () => Sync(() => {
            for (int gate = 0; gate < 5; gate++) {
                var timing = new HostTrialTiming(); timing.ArmStartup("webview", 0);
                Check(!timing.TryTakeStartup(20, gate != 0, gate == 1, gate != 2, gate != 3, gate != 4, out _), "Readiness gate was bypassed: " + gate);
                Check(timing.StartupCase == "webview", "Blocked startup was consumed");
                Check(timing.TryTakeStartup(20.1f, true, false, true, true, true, out var mode) && mode == "webview", "Recovered readiness restarted the delay");
            }
        }));
        await Test("host bridge rearm replaces a pending trial and menu never auto launches", () => Sync(() => {
            var timing = new HostTrialTiming(); timing.ArmStartup("native", 0);
            timing.ArmStartup("menu", 1);
            Check(!timing.TryTakeStartup(100, true, false, true, true, true, out _), "Reopened menu replayed an old trial");
            timing.ArmStartup("keyboard", 100);
            Check(!timing.TryTakeStartup(102, true, false, true, true, true, out _), "New startup inherited the old deadline");
            Check(timing.TryTakeStartup(103, true, false, true, true, true, out var mode) && mode == "keyboard", "New pending startup missing");
        }));
        await Test("host keyboard timeout is independent of startup and cannot close an absent instance", () => Sync(() => {
            var timing = new HostTrialTiming(); timing.ArmStartup("native", 0); timing.ArmKeyboardTimeout(5);
            Check(!timing.ShouldCloseKeyboard(24.99f, true), "Keyboard closed early");
            Check(timing.ShouldCloseKeyboard(25, true), "Keyboard timeout missed its boundary");
            Check(!timing.ShouldCloseKeyboard(100, false), "Absent keyboard was closed");
            Check(timing.TryTakeStartup(100, true, false, true, true, true, out _), "Keyboard timeout consumed the pending startup");
            timing.ArmStartup("menu", 100);
            Check(timing.ShouldCloseKeyboard(100, true), "Reopening the bridge reset the keyboard timeout");
        }));
        await Test("host keyboard replacement starts a fresh deadline without treating time as IME completion", () => Sync(() => {
            var timing = new HostTrialTiming(); timing.ArmKeyboardTimeout(0);
            timing.ArmKeyboardTimeout(15);
            Check(!timing.ShouldCloseKeyboard(20, true), "Replacement inherited the first keyboard deadline");
            Check(timing.ShouldCloseKeyboard(35, true), "Replacement did not retain the 20 second diagnostic timeout");
            var observation = new HostPointerObservation(false, true, true, true, false, true, false, true);
            Check(observation.Blocked && !observation.RayPolicyPermitted, "A diagnostic timer was confused with actual trial/IME state");
        }));
        await Test("host IME return separates trial suppression from missing tracking", () => Sync(() => {
            var editing = new HostPointerObservation(false, false, true, false, false, true, false, true);
            Check(editing.Blocked && !editing.RayPolicyPermitted, "Editing did not suppress input");
            var focused = new HostPointerObservation(false, true, true, false, false, true, false, true);
            Check(focused.BlockReasons == HostPointerBlockReason.NativeTrial, "Focus return was confused with trial completion");
            var ended = new HostPointerObservation(false, true, false, false, false, false, false, true);
            Check(!ended.Blocked && !ended.RayPolicyPermitted && ended.RaySuppressionReason == "NoInputSource", "Missing tracking blamed on a completed trial");
            var tracked = new HostPointerObservation(false, true, false, false, false, true, false, true);
            Check(tracked.CanReadTrackedPointer && tracked.RayPolicyPermitted, "Restored tracked input was denied");
        }));
        await Test("host focus interruption cancels a gesture without clicking on held reacquisition", () => Sync(() => {
            var press = new PointerPressState();
            var browsing = new HostPointerObservation(false, true, false, false, false, true, false, false);
            press.Update(browsing.CanReadTrackedPointer, 1, false);
            Check(press.Update(browsing.CanReadTrackedPointer, 1, true) == PointerPhase.Down, "Initial gesture missing");
            var lost = new HostPointerObservation(false, false, true, false, false, true, false, true);
            Check(press.Update(lost.CanReadTrackedPointer, 1, true) == PointerPhase.Cancel, "Suppression did not cancel held gesture");
            Check(press.Update(browsing.CanReadTrackedPointer, 1, true) == null, "Focus recovery generated a phantom click");
            press.Update(browsing.CanReadTrackedPointer, 1, false);
            Check(press.Update(browsing.CanReadTrackedPointer, 1, true) == PointerPhase.Down, "Fresh gesture after release was rejected");
        }));
        await Test("host keyboard API states remain distinguishable after closing the instance", () => Sync(() => {
            var active = new HostPointerObservation(false, true, false, false, true, true, false, false);
            Check(active.BlockReasons == HostPointerBlockReason.UnityKeyboardActive, "Active instance was ignored");
            var visible = new HostPointerObservation(false, true, false, true, false, true, false, false);
            Check(visible.BlockReasons == HostPointerBlockReason.UnityKeyboardVisible, "Visible flag was confused with an active instance");
            Check(!visible.RayPolicyPermitted, "A latched visibility flag no longer suppresses the existing gate");
            var closed = new HostPointerObservation(false, true, false, false, false, true, false, false);
            Check(closed.RayPolicyPermitted, "Closed keyboard retained suppression");
        }));
        await Test("host gaze remains suspended after a trial and never bypasses input suppression", () => Sync(() => {
            var menu = new HostPointerObservation(false, true, false, false, false, false, true, false);
            Check(menu.GazeAllowed && menu.RayPolicyPermitted, "Optional initial menu gaze missing");
            var trial = new HostPointerObservation(false, true, true, false, false, false, true, false);
            Check(trial.GazeAllowed && !trial.RayPolicyPermitted, "Gaze bypassed native trial suppression");
            var returned = new HostPointerObservation(false, true, false, false, false, false, true, true);
            Check(!returned.GazeAllowed && !returned.RayPolicyPermitted, "Trial return silently reenabled gaze");
        }));
        await Test("host diagnostic preserves simultaneous causes without claiming HMD visibility", () => Sync(() => {
            var observation = new HostPointerObservation(true, false, true, true, true, true, false, true);
            var all = HostPointerBlockReason.Paused | HostPointerBlockReason.ApplicationUnfocused | HostPointerBlockReason.NativeTrial
                | HostPointerBlockReason.UnityKeyboardVisible | HostPointerBlockReason.UnityKeyboardActive;
            Check(observation.BlockReasons == all, "Suppression causes were reduced to only the first one");
            Check(observation.LogFields.Contains("Paused|ApplicationUnfocused|NativeTrial|UnityKeyboardVisible|UnityKeyboardActive"), "Diagnostic codes are not parseable");
            Check(observation.LogFields.Contains("rayPolicyPermitted=False"), "Diagnostic reported policy permission");
        }));
        const string manifest = "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><uses-feature android:name='oculus.software.overlay_keyboard' android:required='false'/><application><activity android:name='com.unity3d.player.UnityPlayerGameActivity' android:exported='true'><intent-filter><action android:name='android.intent.action.MAIN'/><category android:name='android.intent.category.LAUNCHER'/><category android:name='com.oculus.intent.category.VR'/></intent-filter><meta-data android:name='com.oculus.vr.focusaware' android:value='true'/><meta-data android:name='android.app.lib_name' android:value='game'/></activity><activity android:name='test.Other'/><meta-data android:name='com.oculus.supportedDevices' android:value='quest3'/></application></manifest>";
        await Test("OpenXR session focus tracks focused state and clears on lifecycle reset", () => Sync(() => {
            var focus = new ProbeXrFocusState();
            Check(focus.Focus == null, "Cold session claimed focus");
            for (int state = 1; state <= 8; state++) {
                focus.Observe(state);
                Check(focus.Focus == (state == 5), "Nonfocused state accepted or FOCUSED rejected");
            }
            focus.Observe(5); focus.Reset(); Check(focus.Focus == null, "Destroyed session retained focus");
            focus.Observe(int.MaxValue); Check(focus.Focus == null, "Unknown state claimed focus");
            focus.Observe(0); Check(focus.Focus == null, "UNKNOWN state claimed focus");
        }));
        await Test("OpenXR session observations take precedence without reviving stale display focus", () => Sync(() => {
            Check(ActivityProbeInputPolicy.ResolveFocus(false, null, true) == true, "Legacy display observation ignored");
            Check(ActivityProbeInputPolicy.ResolveFocus(true, true, false) == true, "Current session focus ignored");
            Check(ActivityProbeInputPolicy.ResolveFocus(true, false, true) == false, "Lost session focus overridden");
            Check(ActivityProbeInputPolicy.ResolveFocus(true, null, true) == null, "Destroyed session revived by stale display event");
        }));
        await Test("owned Activity input requires every ownership, focus and readiness condition", () => Sync(() => {
            int ready = (int)(ActivityProbeState.Alive | ActivityProbeState.WindowFocus | ActivityProbeState.TextureOnly | ActivityProbeState.Ready | ActivityProbeState.Resumed);
            Check(ActivityProbeInputPolicy.CanUseOwnedHand(false, true, true, ready), "Valid owned host denied");
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, true, null, ready), "Unknown XR focus accepted");
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, true, false, ready), "Lost XR focus accepted");
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(true, true, true, ready), "Paused input accepted");
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, false, true, ready), "Stopped display accepted");
            foreach (var bit in new[] { ActivityProbeState.Alive, ActivityProbeState.WindowFocus, ActivityProbeState.TextureOnly, ActivityProbeState.Ready, ActivityProbeState.Resumed })
                Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, true, true, ready & ~(int)bit), "Missing host condition accepted");
        }));
        await Test("native editing and IME always exclude owned-hand routing", () => Sync(() => {
            const int ready = 1 | 2 | 4 | 32 | 64;
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, true, true, ready | 8), "DOM editing routed");
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, true, true, ready | 16), "IME overlay routed");
            Check(!ActivityProbeInputPolicy.CanUseOwnedHand(false, true, true, 0), "Stale or closed native snapshot routed");
        }));
        await Test("joint pinch hysteresis rejects stale and invalid hand samples", () => Sync(() => {
            var pinch = new ProbePinch();
            Check(pinch.Update(0.03, 0) && !pinch.Pressed, "Open hand pinched");
            Check(pinch.Update(0.02, 0.01) && pinch.Pressed, "Pinch not recognized");
            Check(pinch.Update(0.035, 0.02) && pinch.Pressed, "Pinch jitter released");
            Check(pinch.Update(0.041, 0.01) && !pinch.Pressed, "Release missed");
            pinch.Update(0.02, 0);
            Check(!pinch.Update(0.02, 0.251) && !pinch.Pressed, "Stale pinch survived");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -0.1 })
                Check(!pinch.Update(invalid, 0) && !pinch.Pressed, "Invalid distance accepted");
            Check(!pinch.Update(0.02, -0.1) && !pinch.Pressed, "Future sample accepted");
            Check(!pinch.Update(0.02, double.NaN) && !pinch.Pressed, "Invalid age accepted");
        }));
        await Test("owned-hand focus loss cancels once and held pinch cannot click on return", () => Sync(() => {
            var state = new PointerPressState();
            state.Update(true, 4, false);
            Check(state.Update(true, 4, true) == PointerPhase.Down, "Initial pinch missing");
            Check(state.Update(false, 4, true) == PointerPhase.Cancel, "Focus loss did not cancel");
            Check(state.Update(false, 4, true) == null, "Duplicate cancel");
            Check(state.Update(true, 4, true) == null && state.Update(true, 4, true) == null, "Held pinch clicked on return");
            state.Update(true, 4, false);
            Check(state.Update(true, 4, true) == PointerPhase.Down, "Released hand did not recover");
        }));
        await Test("entry comparison preserves keyboard, VR, focus and other activity settings", () => Sync(() => {
            string rewritten = HostEntryManifest.Rewrite(manifest, false);
            Check(HostEntryManifest.LauncherName(rewritten) == HostEntryManifest.Activity, "Activity launcher");
            var doc = XDocument.Parse(rewritten); XNamespace a = "http://schemas.android.com/apk/res/android";
            Check(doc.Root.Elements("uses-feature").Any(x => (string)x.Attribute(a + "name") == "oculus.software.overlay_keyboard"), "Keyboard declaration lost");
            Check(doc.Descendants("activity").Count() == 2 && rewritten.Contains("com.oculus.vr.focusaware") && rewritten.Contains("com.oculus.intent.category.VR") && rewritten.Contains("quest3"), "Other settings lost");
            Check(!rewritten.Contains("android.app.lib_name"), "Game bootstrap retained on Activity");
        }));
        await Test("entry comparison roundtrip adds exactly one GameActivity bootstrap", () => Sync(() => {
            string rewritten = HostEntryManifest.Rewrite(HostEntryManifest.Rewrite(manifest, false), true);
            Check(HostEntryManifest.LauncherName(rewritten) == HostEntryManifest.GameActivity, "Game launcher");
            XNamespace a = "http://schemas.android.com/apk/res/android"; var doc = XDocument.Parse(rewritten);
            Check(doc.Descendants("meta-data").Count(x => (string)x.Attribute(a + "name") == "android.app.lib_name") == 1, "Duplicate game bootstrap");
            Check(doc.Descendants("meta-data").Count(x => (string)x.Attribute(a + "name") == "unityplayer.UnityActivity") == 1, "Duplicate Unity marker");
        }));
        await Test("entry comparison refuses custom and ambiguous launchers", () => Sync(() => {
            Throws<InvalidOperationException>(() => HostEntryManifest.Rewrite(manifest.Replace(HostEntryManifest.GameActivity, "custom.Player"), false));
            var doc = XDocument.Parse(manifest); var launcher = doc.Root.Element("application").Element("activity");
            doc.Root.Element("application").Add(new XElement(launcher));
            Throws<InvalidOperationException>(() => HostEntryManifest.Rewrite(doc.ToString(), false));
            Throws<System.Xml.XmlException>(() => HostEntryManifest.Rewrite("<!DOCTYPE manifest SYSTEM 'file:///missing'><manifest/>", false));
        }));
        await Test("initialization is shared and navigation waits for readiness", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, a => a());
            await Rejects<InvalidOperationException>(session.NavigateAsync("https://example.invalid"));
            var init = session.InitializeAsync();
            Check(ReferenceEquals(init, session.InitializeAsync()), "Duplicate initialization");
            Check(backend.Initializations == 1 && session.State == BrowserState.Initializing, "Initial state");
            backend.Init.SetResult(true); await init;
            await session.NavigateAsync("https://example.invalid/page");
            Check(session.State == BrowserState.Ready && backend.Navigations == 1, "Ready navigation");
        });
        await Test("dispose cancels pending init and never returns to ready", async () => {
            var backend = new Backend(); var session = new BrowserSession(backend, a => a());
            var init = session.InitializeAsync(); session.Dispose(); session.Dispose();
            await Rejects<OperationCanceledException>(init);
            Check(session.State == BrowserState.Disposed && backend.Disposals == 1, "Disposed state");
            Throws<ObjectDisposedException>(() => session.InitializeAsync());
        });
        await Test("initialization errors are observable", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, a => a());
            var init = session.InitializeAsync(); backend.Init.SetException(new InvalidOperationException("probe failure"));
            await Rejects<InvalidOperationException>(init);
            Check(session.State == BrowserState.Faulted, "Failure was hidden");
        });
        await Test("queued old-generation notifications are dropped", async () => {
            var queue = new Queue<Action>(); var seen = new List<BrowserState>();
            var backend = new Backend(); var session = new BrowserSession(backend, a => queue.Enqueue(a));
            session.StateChanged += seen.Add;
            var init = session.InitializeAsync(); backend.Init.SetResult(true); await init;
            session.Dispose(); while (queue.Count > 0) queue.Dequeue()();
            Check(seen.Count == 1 && seen[0] == BrowserState.Disposed, "Stale notification delivered");
        });
        await Test("canceled navigation does not reach backend", async () => {
            var backend = new Backend(); backend.Init.SetResult(true);
            using var session = new BrowserSession(backend, a => a()); await session.InitializeAsync();
            using var source = new CancellationTokenSource(); source.Cancel();
            await Rejects<OperationCanceledException>(session.NavigateAsync("https://example.invalid", source.Token));
            Check(backend.Navigations == 0, "Canceled navigation dispatched");
        });
        await Test("navigation rejects unsafe schemes and URL credentials", () => Sync(() => {
            foreach (var address in new[] { "javascript:alert(1)", "file:///tmp/a", "relative", "https://user:secret@example.invalid" })
                Throws<ArgumentException>(() => NavigationPolicy.Parse(address));
            Check(NavigationPolicy.Parse("https://example.invalid/a").Host == "example.invalid", "Valid URL rejected");
        }));
        await Test("second pointer cannot steal capture", () => Sync(() => {
            var capture = new PointerCapture(); var events = new List<BrowserPointerEvent>(); capture.Received += events.Add;
            Check(capture.Send(1, PointerPhase.Down, .2f, .2f), "Down rejected");
            Check(!capture.Send(2, PointerPhase.Down, .3f, .3f), "Capture stolen");
            Check(!capture.Send(2, PointerPhase.Up, .3f, .3f), "Foreign Up accepted");
            Check(capture.Send(1, PointerPhase.Up, .2f, .2f) && events.Count == 2, "Owner Up lost");
        }));
        await Test("drag returning to press point preserves movement and actual release", () => Sync(() => {
            var capture = new PointerCapture(); var events = new List<BrowserPointerEvent>(); capture.Received += events.Add;
            capture.Send(1, PointerPhase.Down, .5f, .5f); capture.Send(1, PointerPhase.Move, .9f, .9f);
            capture.Send(1, PointerPhase.Move, .51f, .51f); capture.Send(1, PointerPhase.Up, .51f, .51f);
            Check(events.Count == 4 && events[3].X == .51f, "Slop discarded real motion");
        }));
        await Test("cancel releases capture exactly once", () => Sync(() => {
            var capture = new PointerCapture(); int canceled = 0;
            capture.Received += e => { if (e.Phase == PointerPhase.Cancel) canceled++; };
            capture.Send(8, PointerPhase.Down, 0, 0); capture.Cancel(); capture.Cancel();
            Check(canceled == 1 && capture.Send(9, PointerPhase.Down, 0, 0), "Cancel failed");
        }));
        await Test("coordinates support arbitrary pivot and captured out-of-bounds drag", () => Sync(() => {
            Check(BrowserCoordinates.TryNormalize(25, 75, 0, 0, 100, 100, out float x, out float y)
                && x == .25f && y == .25f, "Noncenter pivot");
            Check(BrowserCoordinates.TryNormalize(0, 0, -50, -50, 100, 100, out x, out y)
                && x == .5f && y == .5f, "Center pivot");
            Check(BrowserCoordinates.TryNormalize(200, 0, 0, 0, 100, 100, out x, out y) && x == 2, "Drag clamped");
            Check(!BrowserCoordinates.TryNormalize(0, 0, 0, 0, 0, 100, out x, out y), "Zero width accepted");
        }));
        await Test("displayed Canvas UV maps upright and inverted page points to the same source content", () => Sync(() => {
            // A point near the Android page top is at the UI top after Flip Y,
            // and near the UI bottom with an unflipped top-first Canvas buffer.
            Check(BrowserCoordinates.TryMapTextureUv(.2f, .25f, 0, 1, 1, -1, out float x, out float y)
                && Math.Abs(x - .2f) < .00001f && Math.Abs(y - .25f) < .00001f, "Upright click was inverted twice");
            Check(BrowserCoordinates.TryMapTextureUv(.2f, .75f, 0, 0, 1, 1, out x, out y)
                && Math.Abs(y - .25f) < .00001f, "Displayed inverted point does not reach its source row");
            Check(BrowserCoordinates.TryMapTextureUv(0, 0, 0, 1, 1, -1, out x, out y) && x == 0 && y == 0, "Top-left corner");
            Check(BrowserCoordinates.TryMapTextureUv(1, 1, 0, 1, 1, -1, out x, out y) && x == 1 && y == 1, "Bottom-right corner");
        }));
        await Test("sampled UV coordinates preserve crop, mirroring and out-of-bounds capture", () => Sync(() => {
            Check(BrowserCoordinates.TryMapTextureUv(.5f, .2f, .1f, .2f, .6f, .5f, out float x, out float y)
                && Math.Abs(x - .4f) < .00001f && Math.Abs(y - .6f) < .00001f, "Cropped UV mapping");
            Check(BrowserCoordinates.TryMapTextureUv(.2f, .25f, 1, 1, -1, -1, out x, out y)
                && Math.Abs(x - .8f) < .00001f && Math.Abs(y - .25f) < .00001f, "Mirrored UV mapping");
            Check(BrowserCoordinates.TryMapTextureUv(-.5f, 1.5f, 0, 1, 1, -1, out x, out y) && x == -.5f && y == 1.5f, "Capture was clamped");
            Check(!BrowserCoordinates.TryMapTextureUv(float.NaN, 0, 0, 0, 1, 1, out x, out y), "Nonfinite UV accepted");
        }));
        await Test("pinch and trigger produce one press, movement and release", () => Sync(() => {
            var press = new PointerPressState();
            Check(press.Update(true, 1, false) == null, "Initial tracking generated a press");
            Check(press.Update(true, 1, true) == PointerPhase.Down && press.ActiveSourceId == 1, "Pinch down lost");
            Check(press.Update(true, 1, true) == PointerPhase.Move, "Held pinch did not move");
            Check(press.Update(true, 1, false) == PointerPhase.Up && press.ActiveSourceId == null, "Pinch release lost");
            Check(press.Update(true, 1, false) == null, "Duplicate release");
        }));
        await Test("tracking loss and source replacement cancel without ghost pinch clicks", () => Sync(() => {
            var press = new PointerPressState();
            Check(press.Update(true, 1, true) == null && press.Update(true, 1, true) == null, "Held pinch clicked on initial tracking");
            press.Update(true, 1, false); Check(press.Update(true, 1, true) == PointerPhase.Down, "Fresh pinch rejected");
            Check(press.Update(true, 2, true) == PointerPhase.Cancel, "Replacement source stole gesture");
            Check(press.Update(true, 2, true) == null, "Replacement held pinch clicked");
            press.Update(true, 2, false); Check(press.Update(true, 2, true) == PointerPhase.Down, "Replacement fresh pinch rejected");
            Check(press.Update(false, 2, true) == PointerPhase.Cancel, "Tracking loss did not cancel");
            Check(press.Update(false, 2, true) == null && press.Update(true, 2, true) == null, "Reacquisition clicked");
            press.Update(true, 2, false); Check(press.Update(true, 2, true) == PointerPhase.Down, "Reacquired fresh pinch rejected");
            press.Reset(); Check(press.Update(true, 2, true) == null, "Focus recovery clicked a held pinch");
        }));
        await Test("nonfinite pointer data cannot poison capture", () => Sync(() => {
            var capture = new PointerCapture();
            Check(!capture.Send(1, PointerPhase.Down, float.NaN, 0), "NaN accepted");
            Check(capture.Send(2, PointerPhase.Down, 0, 0), "Bad input kept capture");
        }));
        await Test("request correlation rejects duplicate and stale responses", async () => {
            using var ledger = new RequestLedger();
            var first = ledger.Begin(out long id, out int generation);
            Check(!ledger.Complete(generation + 1, id, "stale"), "Wrong generation accepted");
            Check(ledger.Complete(generation, id, "ok") && await first == "ok", "Response lost");
            Check(!ledger.Complete(generation, id, "duplicate"), "Duplicate accepted");
        });
        await Test("detach cancels pending requests and next generation works", async () => {
            using var ledger = new RequestLedger(); var old = ledger.Begin(out long id, out int generation);
            ledger.Detach(); await Rejects<OperationCanceledException>(old);
            Check(!ledger.Complete(generation, id, "late"), "Detached response accepted");
            var current = ledger.Begin(out id, out int currentGeneration);
            Check(currentGeneration != generation && ledger.Complete(currentGeneration, id, "new"), "New request failed");
            Check(await current == "new", "New result lost");
        });
        await Test("disposing request ledger cancels pending work and blocks new requests", async () => {
            var ledger = new RequestLedger(); var pending = ledger.Begin(out long id, out int generation);
            ledger.Dispose(); ledger.Dispose(); await Rejects<OperationCanceledException>(pending);
            Throws<ObjectDisposedException>(() => ledger.Begin(out id, out generation));
        });
        await Test("request limit recovers after completion", async () => {
            using var ledger = new RequestLedger(1);
            var first = ledger.Begin(out long id, out int generation);
            Throws<InvalidOperationException>(() => ledger.Begin(out _, out _));
            ledger.Complete(generation, id, "first"); await first;
            var second = ledger.Begin(out id, out generation);
            ledger.Complete(generation, id, "second"); Check(await second == "second", "Limit did not recover");
        });
        await Test("foreign cancellation cannot release owner and invalid phases are rejected", () => Sync(() => {
            var capture = new PointerCapture();
            Check(!capture.Send(1, (PointerPhase)99, 0, 0), "Invalid phase accepted");
            capture.Send(1, PointerPhase.Down, 0, 0); capture.Cancel(2);
            Check(!capture.Send(2, PointerPhase.Down, 0, 0), "Foreign cancellation released owner");
            capture.Cancel(1); Check(capture.Send(2, PointerPhase.Down, 0, 0), "Owner cancellation failed");
        }));
        Console.WriteLine($"Passed {passed} core tests; no Unity or Android runtime was exercised.");
    }
}
