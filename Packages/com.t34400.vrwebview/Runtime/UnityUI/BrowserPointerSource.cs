using UnityEngine;
using UnityEngine.EventSystems;

namespace T34400.VRWebView
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class BrowserPointerSource : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, IInitializePotentialDragHandler, ICancelHandler
    {
        public PointerCapture Capture { get; } = new PointerCapture();
        private RectTransform target;
        private void Awake() { target = (RectTransform)transform; }
        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
        public void OnPointerDown(PointerEventData data) { Forward(data, PointerPhase.Down); }
        public void OnPointerUp(PointerEventData data) { Forward(data, PointerPhase.Up); }
        public void OnDrag(PointerEventData data) { Forward(data, PointerPhase.Move); }
        public void OnCancel(BaseEventData data) { Capture.Cancel(); }
        private void OnDisable() { Capture.Cancel(); }
        private void OnApplicationFocus(bool focus) { if (!focus) Capture.Cancel(); }
        private void Forward(PointerEventData data, PointerPhase phase)
        {
            if (target == null) target = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(target, data.position,
                data.pressEventCamera, out var local)) return;
            Rect rect = target.rect;
            if (BrowserCoordinates.TryNormalize(local.x, local.y, rect.xMin, rect.yMin,
                rect.width, rect.height, out float x, out float y))
                Capture.Send(data.pointerId, phase, x, y);
        }
    }
}
