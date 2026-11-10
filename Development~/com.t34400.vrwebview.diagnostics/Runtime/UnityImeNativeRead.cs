using System;
using System.Threading;
using UnityEngine;
namespace T34400.VRWebView
{
    internal sealed class UnityImeNativeRead
    {
        public int Attempt, View, InputType, ImeOptions;
        public bool ShowOperation, Issued, Accepted, Ready, ViewFocus, WindowFocus, Active;
        public string Reason = "not_ready", Stage = "activity";
        // Only public Android / GameActivity methods. No new editor, InputConnection,
        // restartInput, requestFocus, text copying, or EditorInfo mutation.
        public static void Post(int attempt, CancellationToken token, Action<UnityImeNativeRead> reply,
            bool show = false, int expectedView = 0, int expectedType = 0, int expectedOptions = 0) {
            var sample = new UnityImeNativeRead { Attempt = attempt, ShowOperation = show };
            AndroidJavaObject activity = null;
            try {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                var heldActivity = activity;
                heldActivity.Call("runOnUiThread", new AndroidJavaRunnable(() => {
                    using (heldActivity) {
                        try {
                            if (token.IsCancellationRequested) { sample.Reason = "cancelled"; return; }
                            if (heldActivity.Call<bool>("isFinishing") || heldActivity.Call<bool>("isDestroyed")) { sample.Reason = "activity_closed"; return; }
                            sample.Stage = "view";
                            using (var view = heldActivity.Call<AndroidJavaObject>("getCurrentFocus")) {
                                if (view == null) { sample.Reason = "view_missing"; return; }
                                // UnityPlayerGameActivity assigns this resource ID to its
                                // SurfaceView. Avoid depending on an obfuscatable inner class name.
                                using (var resources = heldActivity.Call<AndroidJavaObject>("getResources")) {
                                    int unityView = resources.Call<int>("getIdentifier", "unitySurfaceView", "id", heldActivity.Call<string>("getPackageName"));
                                    if (unityView == 0 || view.Call<int>("getId") != unityView) { sample.Reason = "different_view"; return; }
                                }
                                using (var system = new AndroidJavaClass("java.lang.System")) sample.View = system.CallStatic<int>("identityHashCode", view);
                                // Read only after the stock keyboard instance exists and the
                                // Unity native Open has returned. This is local GameActivity state.
                                sample.Stage = "editor_info";
                                using (var editor = heldActivity.Call<AndroidJavaObject>("getImeEditorInfo")) {
                                    sample.InputType = editor.Get<int>("inputType"); sample.ImeOptions = editor.Get<int>("imeOptions");
                                }
                                sample.Stage = "imm";
                                using (var imm = heldActivity.Call<AndroidJavaObject>("getSystemService", "input_method")) {
                                    sample.ViewFocus = view.Call<bool>("hasFocus"); sample.WindowFocus = view.Call<bool>("hasWindowFocus");
                                    sample.Active = imm != null && imm.Call<bool>("isActive", view);
                                    sample.Ready = view.Call<bool>("isAttachedToWindow") && sample.ViewFocus && sample.WindowFocus && sample.Active && (sample.InputType & 15) == 1;
                                    sample.Reason = sample.Ready ? "local_ready" : "not_ready";
                                    if (!show || !sample.Ready) return;
                                    if (sample.View != expectedView || sample.InputType != expectedType || sample.ImeOptions != expectedOptions) { sample.Reason = "editor_changed"; return; }
                                    if (token.IsCancellationRequested) { sample.Reason = "cancelled"; return; }
                                    sample.Stage = "show"; sample.Issued = true;
                                    sample.Accepted = imm.Call<bool>("showSoftInput", view, 0);
                                    sample.Reason = "show_returned";
                                }
                            }
                        } catch (Exception error) { sample.Reason = error.GetType().Name; }
                        finally { reply(sample); }
                    }
                }));
            } catch (Exception error) { activity?.Dispose(); sample.Reason = error.GetType().Name; reply(sample); }
        }
    }
}
