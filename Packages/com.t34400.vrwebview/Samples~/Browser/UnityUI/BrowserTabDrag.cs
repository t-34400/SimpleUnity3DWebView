using UnityEngine;
using UnityEngine.EventSystems;
namespace T34400.VRWebView.Samples.Browser
{
    public sealed class BrowserTabDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler
    {
        private BrowserDesktopPanel application; private BrowserWindowView window; private long tab;
        public void Configure(BrowserDesktopPanel app, BrowserWindowView view, long id) { application = app; window = view; tab = id; }
        public void OnBeginDrag(PointerEventData data) { application.BeginTabDrag(tab, window, data); }
        public void OnDrag(PointerEventData data) { application.DragTab(data); }
        public void OnEndDrag(PointerEventData data) { application.EndTabDrag(); }
        public void OnCancel(BaseEventData data) { application.CancelTabDrag(); }
    }
    public sealed class BrowserWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler
    {
        private BrowserWindowView window; private bool resize, active;
        private Vector3 startPosition, origin;
        private Vector2 startSize;
        public void Configure(BrowserWindowView view, bool resizing) { window = view; resize = resizing; }
        public void OnBeginDrag(PointerEventData data) {
            var rect = (RectTransform)window.transform; startPosition = rect.localPosition; startSize = rect.sizeDelta;
            active = RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)rect.parent, data.position, data.pressEventCamera, out origin);
        }
        public void OnDrag(PointerEventData data) {
            if (!active || !RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)window.transform.parent, data.position, data.pressEventCamera, out var world)) return;
            var rect = (RectTransform)window.transform; Vector3 delta = rect.parent.InverseTransformVector(world - origin);
            if (!resize) rect.localPosition = startPosition + delta;
            else rect.sizeDelta = new Vector2(Mathf.Clamp(startSize.x + delta.x, 800, 1800), Mathf.Clamp(startSize.y - delta.y, 600, 1400));
        }
        public void OnEndDrag(PointerEventData data) { active = false; }
        public void OnCancel(BaseEventData data) {
            if (!active) return; active = false; var rect = (RectTransform)window.transform; rect.localPosition = startPosition; rect.sizeDelta = startSize;
        }
    }
}
