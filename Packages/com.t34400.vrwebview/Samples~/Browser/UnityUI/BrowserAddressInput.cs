using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Wait for native page editing to finish before opening Unity's keyboard.</summary>
    public sealed class BrowserAddressInput : InputField
    {
        public Func<Task> PrepareEditing;
        public event Action<string> EditingFailed;
        public event Action Submitted;
        private int request;
        private float checkAt;
        private bool watching;
        protected override void Awake() { base.Awake(); shouldActivateOnSelect = false; shouldHideMobileInput = false; }
        public override void OnPointerClick(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) BeginEditing(); }
        public async void BeginEditing() {
            if (!IsActive() || !IsInteractable()) return;
            int version = ++request;
            try {
                if (PrepareEditing != null) await PrepareEditing();
                if (!Application.isFocused || version != request || !IsActive() || !IsInteractable() || EventSystem.current?.currentSelectedGameObject != gameObject) return;
                ActivateInputField(); checkAt = Time.unscaledTime + 1.5f; watching = true;
                Debug.Log("[VRWebViewSample] BS1 event=address_keyboard_request");
            } catch (Exception error) {
                if (version != request) return;
                Debug.LogError("[VRWebViewSample] BS1 event=address_keyboard_failed type=" + error.GetType().Name);
                EditingFailed?.Invoke("Could not begin address editing. Select the address to retry.");
            }
        }
        public override void OnSubmit(BaseEventData data) { if (isFocused) Submitted?.Invoke(); else BeginEditing(); }
        protected override void LateUpdate() {
            bool done = touchScreenKeyboard != null && touchScreenKeyboard.status == TouchScreenKeyboard.Status.Done;
            base.LateUpdate();
            if (done) { watching = false; Submitted?.Invoke(); return; }
            if (!watching || Application.platform != RuntimePlatform.Android) return;
            if (TouchScreenKeyboard.visible || !Application.isFocused) { watching = false; return; }
            if (Time.unscaledTime < checkAt) return;
            watching = false; DeactivateInputField();
            Debug.LogWarning("[VRWebViewSample] BS1 event=address_keyboard_failed reason=visibility_timeout");
            EditingFailed?.Invoke("Keyboard did not open. Select the address to retry.");
        }
        public override void OnDeselect(BaseEventData data) { request++; watching = false; base.OnDeselect(data); }
        protected override void OnDisable() { request++; watching = false; base.OnDisable(); }
    }
}
