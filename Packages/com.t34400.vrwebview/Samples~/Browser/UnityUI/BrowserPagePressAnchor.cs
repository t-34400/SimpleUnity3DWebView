using UnityEngine;
using UnityEngine.EventSystems;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Remembers the page press that triggered native long-click without changing runtime APIs.</summary>
    public sealed class BrowserPagePressAnchor : MonoBehaviour, IPointerDownHandler {
        private Vector2 normalized;
        private bool valid;
        public void OnPointerDown(PointerEventData data) {
            var rect = (RectTransform)transform; Vector2 point;
            if (data is BrowserRayPointerEventData ray && ray.HasWorldRay) {
                if (!new Plane(rect.forward, rect.position).Raycast(ray.WorldRay, out float distance)) return;
                Vector3 local = rect.InverseTransformPoint(ray.WorldRay.GetPoint(distance)); point = new Vector2(local.x, local.y);
            } else if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out point)) return;
            Rect bounds = rect.rect; valid = bounds.width > 0 && bounds.height > 0;
            if (valid) normalized = new Vector2(Mathf.Clamp01((point.x - bounds.xMin) / bounds.width), Mathf.Clamp01((point.y - bounds.yMin) / bounds.height));
        }
        public Vector3 WorldPoint {
            get { var rect = (RectTransform)transform; var bounds = rect.rect;
                return rect.TransformPoint(valid ? new Vector3(bounds.xMin + normalized.x * bounds.width, bounds.yMin + normalized.y * bounds.height, 0) : new Vector3(bounds.center.x, bounds.center.y, 0)); }
        }
        public void Clear() { valid = false; }
    }
}
