using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Explicit selection and main-thread editing handoff before opening Unity's keyboard.</summary>
    public sealed class BrowserAddressInput : InputField
    {
        public Func<Task> PrepareEditing;
        public event Action<string> EditingFailed;
        public event Action Submitted;
        private int request;
        private float checkAt;
        private bool watching, preparing;
        protected override void Awake() { base.Awake(); shouldActivateOnSelect = false; shouldHideMobileInput = false; }
        public override void OnPointerDown(PointerEventData data) {
            base.OnPointerDown(data);
            // Text editing begins on press, like a normal InputField. Pinch release
            // need not hit the exact same graphic again to start editing.
            if (data.button == PointerEventData.InputButton.Left && !isFocused && !preparing && !watching) BeginEditing();
        }
        public override void OnPointerClick(PointerEventData data) {
            if (data.button == PointerEventData.InputButton.Left && !isFocused && !preparing && !watching) BeginEditing();
        }
        public void BeginEditing() {
            if (!IsActive() || !IsInteractable()) return;
            var system = EventSystem.current;
            if (system == null) { Fail("event_system_missing"); return; }
            // XR pointer clicks must not depend on Selectable's navigation mode or
            // assume InputField.OnPointerDown has already selected the object.
            system.SetSelectedGameObject(gameObject);
            int version = ++request; preparing = true;
            Debug.Log("[VRWebViewSample] BS1 event=address_edit phase=prepare request=" + version + " selected=" + (system.currentSelectedGameObject == gameObject));
            Task task;
            try { task = PrepareEditing?.Invoke() ?? Task.CompletedTask; }
            catch (Exception error) { Fail(error.GetType().Name); return; }
            StartCoroutine(PrepareOnMainThread(task, version));
        }
        private IEnumerator PrepareOnMainThread(Task task, int version) {
            float deadline = Time.unscaledTime + 2;
            while (!task.IsCompleted && version == request && Time.unscaledTime < deadline) yield return null;
            if (version != request) yield break;
            preparing = false;
            if (!task.IsCompleted) { Fail("editing_handoff_timeout"); yield break; }
            if (task.IsFaulted || task.IsCanceled) { Fail(task.IsCanceled ? "editing_handoff_canceled" : task.Exception.GetBaseException().GetType().Name); yield break; }
            if (!IsActive() || !IsInteractable() || !Application.isFocused) {
                Debug.Log("[VRWebViewSample] BS1 event=address_edit phase=aborted reason=input_unavailable"); yield break;
            }
            if (EventSystem.current?.currentSelectedGameObject != gameObject) {
                Debug.Log("[VRWebViewSample] BS1 event=address_edit phase=aborted reason=selection_changed"); yield break;
            }
            ActivateInputField(); checkAt = Time.unscaledTime + 1.5f; watching = true;
            Debug.Log("[VRWebViewSample] BS1 event=address_keyboard_request request=" + version);
        }
        private void Fail(string reason) {
            preparing = false; watching = false;
            Debug.LogWarning("[VRWebViewSample] BS1 event=address_keyboard_failed reason=" + reason);
            EditingFailed?.Invoke("Could not open the keyboard. Select the address to retry.");
        }
        public override void OnSubmit(BaseEventData data) { if (isFocused) Submitted?.Invoke(); else BeginEditing(); }
        protected override void LateUpdate() {
            bool done = touchScreenKeyboard != null && touchScreenKeyboard.status == TouchScreenKeyboard.Status.Done;
            base.LateUpdate();
            if (done) { watching = false; Submitted?.Invoke(); return; }
            if (!watching || Application.platform != RuntimePlatform.Android) return;
            if (TouchScreenKeyboard.visible || !Application.isFocused) { watching = false; return; }
            if (Time.unscaledTime < checkAt) return;
            watching = false; DeactivateInputField(); Fail("visibility_timeout");
        }
        public override void OnDeselect(BaseEventData data) {
            if (preparing) Debug.Log("[VRWebViewSample] BS1 event=address_edit phase=aborted reason=deselected");
            request++; preparing = watching = false; base.OnDeselect(data);
        }
        protected override void OnDisable() { request++; preparing = watching = false; base.OnDisable(); }
    }
}
