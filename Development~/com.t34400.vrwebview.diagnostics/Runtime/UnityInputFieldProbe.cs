using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView
{
    /// <summary>U1 control: a stock uGUI InputField. Never creates a browser or a native editor.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class UnityInputFieldProbe : MonoBehaviour
    {
        public InputField field;
        private string previous;
        private float nextTick;
        private TouchScreenKeyboard observedKeyboard;
        private string endedKeyboardText, endedKeyboardStatus = "missing";
        private bool endedKeyboardKnown, endedKeyboardStable, valuesDirty;
        private float nextValues, closingUntil;
        private int closingFrames;
        private void Start() {
            if (field == null || field.GetType() != typeof(InputField)) {
                Debug.LogError("[VRWebViewUnityInput] U1 event=invalid_field"); enabled = false; return;
            }
            field.onValueChanged.AddListener(Changed);
            field.onEndEdit.AddListener(Ended);
            Debug.Log("[VRWebViewUnityInput] U1 event=ready unity=" + Application.unityVersion
                + " stock=True webview=False keyboardType=" + field.keyboardType + " inputType=" + field.inputType
                + " hideMobile=" + field.shouldHideMobileInput + " hideInput=" + TouchScreenKeyboard.hideInput
                + " supported=" + TouchScreenKeyboard.isSupported + " inPlace=" + TouchScreenKeyboard.isInPlaceEditingAllowed
                + " valueProbe=1 readOnly=" + field.readOnly + " validation=" + field.characterValidation + " characterLimit=" + field.characterLimit);
        }
        private void Changed(string value) {
            // Deliberately omit text, URL, selection and composition contents.
            valuesDirty = true;
            Debug.Log("[VRWebViewUnityInput] U1 event=text_changed");
        }
        private void Ended(string ignored) {
            SampleValues("end_edit", field.touchScreenKeyboard);
            observedKeyboard = null; closingFrames = 2; closingUntil = Time.unscaledTime + 1; valuesDirty = true;
        }
        private void LateUpdate() {
            if (field == null) return;
            var keyboard = field.touchScreenKeyboard;
            if (keyboard != null && !ReferenceEquals(keyboard, observedKeyboard)) {
                observedKeyboard = keyboard; endedKeyboardKnown = endedKeyboardStable = false;
                endedKeyboardText = null; valuesDirty = true;
            }
            if (keyboard == null && closingFrames == 0 && Time.unscaledTime >= closingUntil && !valuesDirty) return;
            if (!valuesDirty && closingFrames == 0 && Time.unscaledTime < nextValues) return;
            valuesDirty = false; nextValues = Time.unscaledTime + .25f;
            SampleValues(keyboard != null ? "editing" : "closed", keyboard);
            if (keyboard == null && closingFrames > 0) closingFrames--;
        }
        private void SampleValues(string phase, TouchScreenKeyboard keyboard) {
            // LateUpdate samples follow the stock field; end_edit is a pre-close
            // callback snapshot. Read-only: never assign any of
            // these strings back to the field, keyboard, label or composition state.
            string before = keyboard != null ? keyboard.text ?? "" : endedKeyboardText ?? "";
            string model = field.text ?? "", label = field.textComponent != null ? field.textComponent.text ?? "" : "";
            string after = keyboard != null ? keyboard.text ?? "" : endedKeyboardText ?? "";
            string keyboardStatus = keyboard != null ? keyboard.status.ToString() : endedKeyboardStatus;
            bool known = keyboard != null || endedKeyboardKnown;
            bool stable = keyboard != null ? before == after : endedKeyboardStable;
            int compositionLength = -1;
            try {
                var module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;
                compositionLength = (module != null ? module.input.compositionString : Input.compositionString)?.Length ?? 0;
            } catch (Exception) { /* The comparison remains usable if legacy composition is unavailable. */ }
            int missing = 0, surrogates = 0;
            var font = field.textComponent != null ? field.textComponent.font : null;
            try {
                foreach (char character in label) {
                    if (char.IsSurrogate(character)) { surrogates++; continue; }
                    if (font != null && !char.IsControl(character) && !font.HasCharacter(character)) missing++;
                }
            } catch (Exception) { missing = -1; }
            Debug.Log("[VRWebViewUnityInput] U1 event=values phase=" + phase + " frames=" + Time.frameCount
                + " keyboardSource=" + (keyboard != null ? "live" : known ? "end_snapshot" : "missing")
                + " keyboardStatus=" + keyboardStatus + " stable=" + stable + " fieldLength=" + model.Length + " keyboardLength=" + (known ? after.Length : -1)
                + " fieldEqualsKeyboard=" + (known && model == after) + " labelLength=" + label.Length
                + " labelIsFieldSlice=" + (model.IndexOf(label, StringComparison.Ordinal) >= 0)
                + " emptyLabel=" + (label.Length == 0 && model.Length != 0)
                + " compositionLength=" + compositionLength + " missingBmpGlyphs=" + missing + " surrogateUnits=" + surrogates
                + " fieldFocus=" + field.isFocused + " appFocus=" + Application.isFocused);
            if (phase == "end_edit") {
                endedKeyboardText = after; endedKeyboardStatus = keyboardStatus; endedKeyboardKnown = known; endedKeyboardStable = stable;
            }
        }
        private void Update() {
            if (field == null) return;
            var keyboard = field.touchScreenKeyboard;
            string state = "selected=" + (EventSystem.current?.currentSelectedGameObject == field.gameObject)
                + " fieldFocus=" + field.isFocused + " keyboard=" + (keyboard != null)
                + " visible=" + TouchScreenKeyboard.visible + " appFocus=" + Application.isFocused
                + (keyboard != null ? " active=" + keyboard.active + " status=" + keyboard.status : "");
            if (state == previous && Time.unscaledTime < nextTick) return;
            previous = state; nextTick = Time.unscaledTime + 1;
            Debug.Log("[VRWebViewUnityInput] U1 event=state " + state + " frames=" + Time.frameCount);
        }
        private void OnDestroy() {
            if (field != null) { field.onValueChanged.RemoveListener(Changed); field.onEndEdit.RemoveListener(Ended); }
            endedKeyboardText = null; observedKeyboard = null;
        }
    }
}
