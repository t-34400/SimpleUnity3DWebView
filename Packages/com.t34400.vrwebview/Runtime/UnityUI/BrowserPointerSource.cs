using UnityEngine;
using UnityEngine.EventSystems;

namespace T34400.VRWebView
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class BrowserPointerSource : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IDragHandler, IInitializePotentialDragHandler, ICancelHandler, IScrollHandler,
        IPointerEnterHandler, IPointerMoveHandler, IPointerExitHandler
    {
        public PointerCapture Capture { get; } = new PointerCapture();
        public HoverCapture Hover { get; } = new HoverCapture();
        public event System.Action<BrowserScrollEvent> ScrollReceived;
        private bool inputEnabled = true;
        private bool focused = true;
        public bool InputEnabled {
            get => inputEnabled;
            set { if (inputEnabled == value) return; inputEnabled = value; if (!value) ResetInput(); }
        }
        private RectTransform target;
        private void Awake() { target = (RectTransform)transform; }
        private void OnEnable() { focused = Application.isFocused; }
        public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
        public void OnPointerDown(PointerEventData data) { Forward(data, PointerPhase.Down); }
        public void OnPointerUp(PointerEventData data) { Forward(data, PointerPhase.Up); UpdateHover(data); }
        public void OnPointerEnter(PointerEventData data) { UpdateHover(data); }
        public void OnPointerMove(PointerEventData data) { UpdateHover(data); }
        public void OnPointerExit(PointerEventData data) { Hover.Exit(data.pointerId); }
        private void UpdateHover(PointerEventData data)
        {
            if (!inputEnabled || !focused || Capture.IsCaptured) return;
            if (TryPoint(data, out float x, out float y, true) && x >= 0 && x <= 1 && y >= 0 && y <= 1)
                Hover.Send(data.pointerId, Hover.IsHovering ? HoverPhase.Move : HoverPhase.Enter, x, y);
            else Hover.Exit(data.pointerId);
        }
        public void OnDrag(PointerEventData data) { Forward(data, PointerPhase.Move); }
        public void OnCancel(BaseEventData data) { ResetInput(); }
        private void OnDisable() { ResetInput(); }
        private void OnApplicationFocus(bool focus) { focused = focus; if (!focus) ResetInput(); }
        private void ResetInput() { try { Capture.Cancel(); } finally { Hover.Exit(); } }
        public void OnScroll(PointerEventData data)
        {
            if (inputEnabled && TryPoint(data, out float x, out float y))
                ScrollReceived?.Invoke(new BrowserScrollEvent(data.pointerId, x, y, data.scrollDelta.x, data.scrollDelta.y));
        }
        private void Forward(PointerEventData data, PointerPhase phase)
        {
            if (!inputEnabled) return;
            if (phase == PointerPhase.Down) Hover.Exit();
            if (TryPoint(data, out float x, out float y)) Capture.Send(data.pointerId, phase, x, y);
            else if (phase == PointerPhase.Up) Capture.Cancel(data.pointerId);
        }
        private bool TryPoint(PointerEventData data, out float x, out float y, bool hover = false)
        {
            x = y = 0;
            if (target == null) target = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(target, data.position,
                hover ? data.enterEventCamera : data.pressEventCamera, out var local))
                return false;
            Rect rect = target.rect;
            return BrowserCoordinates.TryNormalize(local.x, local.y, rect.xMin, rect.yMin,
                rect.width, rect.height, out x, out y);
        }
    }
}
