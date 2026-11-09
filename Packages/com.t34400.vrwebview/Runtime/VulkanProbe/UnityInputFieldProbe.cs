using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView
{
    /// <summary>U1 control: a stock uGUI InputField. Never creates a browser or a native editor.</summary>
    public sealed class UnityInputFieldProbe : MonoBehaviour
    {
        public InputField field;
        private string previous;
        private float nextTick;
        private void Start() {
            if (field == null || field.GetType() != typeof(InputField)) {
                Debug.LogError("[VRWebViewUnityInput] U1 event=invalid_field"); enabled = false; return;
            }
            field.onValueChanged.AddListener(Changed);
            Debug.Log("[VRWebViewUnityInput] U1 event=ready unity=" + Application.unityVersion
                + " stock=True webview=False keyboardType=" + field.keyboardType + " inputType=" + field.inputType
                + " hideMobile=" + field.shouldHideMobileInput + " hideInput=" + TouchScreenKeyboard.hideInput
                + " supported=" + TouchScreenKeyboard.isSupported + " inPlace=" + TouchScreenKeyboard.isInPlaceEditingAllowed);
        }
        private void Changed(string value) {
            // Deliberately omit text, URL, selection and composition contents.
            Debug.Log("[VRWebViewUnityInput] U1 event=text_changed");
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
        private void OnDestroy() { if (field != null) field.onValueChanged.RemoveListener(Changed); }
    }
}
