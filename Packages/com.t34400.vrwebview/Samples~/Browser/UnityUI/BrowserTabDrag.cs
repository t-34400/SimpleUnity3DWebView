using UnityEngine;
using UnityEngine.EventSystems;
namespace T34400.VRWebView.Samples.Browser
{
    public sealed class BrowserTabDrag : MonoBehaviour, IBrowserSpatialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler
    {
        private BrowserDesktopPanel application; private BrowserWindowView window; private long tab;
        public void Configure(BrowserDesktopPanel app, BrowserWindowView view, long id) { application = app; window = view; tab = id; }
        public void OnBeginDrag(PointerEventData data) { application.BeginTabDrag(tab, window, data); }
        public void OnDrag(PointerEventData data) { application.DragTab(data); }
        public void OnEndDrag(PointerEventData data) { application.EndTabDrag(); }
        public void OnCancel(BaseEventData data) { application.CancelTabDrag(); }
    }
}
