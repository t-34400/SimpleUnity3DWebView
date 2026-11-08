using UnityEngine;
using UnityEngine.EventSystems;

namespace T34400.VRWebView
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class BrowserPointerSource : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, IInitializePotentialDragHandler, ICancelHandler, IScrollHandler
    {
        public PointerCapture Capture { get; } = new PointerCapture();
        public event System.Action<BrowserScrollEvent> ScrollReceived;
        private bool inputEnabled = true;
        public bool InputEnabled {
            get => inputEnabled;
            set { if (inputEnabled == value) return; inputEnabled = value; if (!value) Capture.Cancel(); }
        }
        private RectTransform target;
        private void Awake() { target = (RectTransform)transform; }
        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
        public void OnPointerDown(PointerEventData data) { Forward(data, PointerPhase.Down); }
        public void OnPointerUp(PointerEventData data) { Forward(data, PointerPhase.Up); }
        public void OnDrag(PointerEventData data) { Forward(data, PointerPhase.Move); }
        public void OnCancel(BaseEventData data) { Capture.Cancel(); }
        private void OnDisable() { Capture.Cancel(); }
        private void OnApplicationFocus(bool focus) { if (!focus) Capture.Cancel(); }
        public void OnScroll(PointerEventData data)
        {
            if (inputEnabled && TryPoint(data, out float x, out float y))
                ScrollReceived?.Invoke(new BrowserScrollEvent(data.pointerId, x, y, data.scrollDelta.x, data.scrollDelta.y));
        }
        private void Forward(PointerEventData data, PointerPhase phase)
        {
            if (!inputEnabled) return;
            if (TryPoint(data, out float x, out float y)) Capture.Send(data.pointerId, phase, x, y);
            else if (phase == PointerPhase.Up) Capture.Cancel(data.pointerId);
        }
        private bool TryPoint(PointerEventData data, out float x, out float y)
        {
            x = y = 0;
            if (target == null) target = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(target, data.position,
                data.pressEventCamera, out var local))
                return false;
            Rect rect = target.rect;
            return BrowserCoordinates.TryNormalize(local.x, local.y, rect.xMin, rect.yMin,
                rect.width, rect.height, out x, out y);
        }
    }
}
