using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView
{
    /// <summary>U2: one extra show on the stock Unity editor after local readiness.</summary>
    public sealed class UnityImeOrderProbe : MonoBehaviour
    {
        public InputField field;
        private readonly ConcurrentQueue<UnityImeNativeRead> replies = new ConcurrentQueue<UnityImeNativeRead>();
        private UnityImeShowGate gate;
        private CancellationTokenSource lease;
        private TouchScreenKeyboard armedKeyboard;
        private int attempt;
        private void Start() {
            if (field == null || field.GetType() != typeof(InputField)) { Trace("invalid_field"); enabled = false; return; }
            field.onEndEdit.AddListener(EditEnded);
            Trace("ready", "stock=True webview=False maxExtraShows=1 readiness=local_not_ime_ack");
        }
        private void Trace(string phase, string fields = "") { Debug.Log("[VRWebViewUnityInput] U2 event=" + phase + " attempt=" + attempt + " " + fields); }
        private bool Selected() => field != null && field.isFocused && EventSystem.current?.currentSelectedGameObject == field.gameObject;
        private void Update() {
            if (Application.platform != RuntimePlatform.Android) return;
            var keyboard = field != null ? field.touchScreenKeyboard : null;
            if (keyboard != null && !ReferenceEquals(keyboard, armedKeyboard)) {
                Cancel("new_keyboard"); armedKeyboard = keyboard;
                if (Selected() && Application.isFocused) {
                    attempt++; lease = new CancellationTokenSource(); gate = new UnityImeShowGate(Time.unscaledTime);
                    Trace("armed");
                } else Trace("skipped", "reason=initial_input_unavailable");
            }
            if (lease != null && (!Selected() || !Application.isFocused || keyboard == null || keyboard.status != TouchScreenKeyboard.Status.Visible))
                Cancel("editing_ended");
            while (replies.TryDequeue(out var sample)) {
                // Show results remain useful even if the overlay acquired focus before
                // the reply. They never reactivate a cancelled editing attempt.
                if (sample.ShowOperation) {
                    Debug.Log("[VRWebViewUnityInput] U2 event=show_result attempt=" + sample.Attempt
                        + " issued=" + sample.Issued + " accepted=" + sample.Accepted + " reason=" + sample.Reason + " stage=" + sample.Stage);
                    continue;
                }
                if (sample.Attempt != attempt || gate == null || gate.Finished) continue;
                Trace("observation", "localInputType=0x" + sample.InputType.ToString("x") + " localImeOptions=0x" + sample.ImeOptions.ToString("x")
                    + " viewFocus=" + sample.ViewFocus + " windowFocus=" + sample.WindowFocus + " immActive=" + sample.Active + " ready=" + sample.Ready + " reason=" + sample.Reason + " stage=" + sample.Stage);
                if (!Selected() || !Application.isFocused || lease.IsCancellationRequested) { Cancel("input_unavailable"); continue; }
                if (sample.Reason != "local_ready" && sample.Reason != "not_ready") { Cancel(sample.Reason); continue; }
                gate.Observe(Time.unscaledTime, sample.Ready, sample.View, sample.InputType, sample.ImeOptions);
                if (gate.TimedOut) { Trace("ready_timeout"); Cancel("ready_timeout"); continue; }
                if (gate.ClaimShow()) {
                    Trace("show_dispatch", "localInputType=0x" + sample.InputType.ToString("x") + " localImeOptions=0x" + sample.ImeOptions.ToString("x"));
                    UnityImeNativeRead.Post(attempt, lease.Token, replies.Enqueue, true, sample.View, sample.InputType, sample.ImeOptions);
                }
            }
            if (gate == null || gate.Finished) return;
            if (!Selected() || !Application.isFocused || keyboard == null || keyboard.status != TouchScreenKeyboard.Status.Visible) { Cancel("editing_ended"); return; }
            if (gate.Expire(Time.unscaledTime)) { Trace("ready_timeout"); Cancel("ready_timeout"); return; }
            if (gate.TryRead(Time.unscaledTime)) UnityImeNativeRead.Post(attempt, lease.Token, replies.Enqueue);
        }
        private void Cancel(string reason) {
            if (lease == null) return;
            lease.Cancel(); lease.Dispose(); lease = null; gate?.Cancel();
            Trace("cancelled", "reason=" + reason);
        }
        private void EditEnded(string ignored) { Cancel("field_end_edit"); }
        private void OnApplicationFocus(bool value) { if (!value) Cancel("focus_lost"); }
        private void OnApplicationPause(bool value) { if (value) Cancel("paused"); }
        private void OnDisable() { Cancel("disabled"); }
        private void OnDestroy() { if (field != null) field.onEndEdit.RemoveListener(EditEnded); Cancel("destroyed"); }
    }
}
