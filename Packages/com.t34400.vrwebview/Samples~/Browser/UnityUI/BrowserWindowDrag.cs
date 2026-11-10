using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Blank-header spatial grab, or opposite-corner-anchored resize.</summary>
    public sealed class BrowserWindowDrag : MonoBehaviour, IBrowserSpatialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, ICancelHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private BrowserWindowView window;
        private int cornerX, cornerY;
        private bool active, hovered;
        private Vector3 startPosition, startPointer;
        private Vector2 startSize;
        private Quaternion startRotation, startGrip, startFacing;
        private Vector3 grabOffset;
        private Plane startPlane;
        private float grabDistance;
        private Matrix4x4 localToWorld, worldToLocal;
        private Image[] strokes;
        public void Configure(BrowserWindowView view, int x = 0, int y = 0, Image[] cornerStrokes = null) { window = view; cornerX = x; cornerY = y; strokes = cornerStrokes; Paint(); }
        public void OnPointerDown(PointerEventData data) { window.BringToFront(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Paint(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Paint(); }
        private void Paint() {
            if (strokes == null) return;
            foreach (var image in strokes) if (image != null) image.color = active || hovered ? Color.cyan : new Color(.3f, .4f, .5f, .6f);
        }
        public void OnBeginDrag(PointerEventData data) {
            var rect = (RectTransform)window.transform; startPosition = rect.position; startSize = rect.sizeDelta;
            startRotation = rect.rotation;
            localToWorld = rect.localToWorldMatrix; worldToLocal = rect.worldToLocalMatrix;
            startPlane = new Plane(rect.forward, rect.position);
            active = TryPoint(data, out startPointer);
            if (data is BrowserRayPointerEventData ray && ray.HasWorldRay)
                grabDistance = Mathf.Max(.1f, Vector3.Dot(startPointer - ray.WorldRay.origin, ray.WorldRay.direction));
            grabOffset = Quaternion.Inverse(startRotation) * (startPointer - startPosition);
            startFacing = window.Viewer != null ? Facing(startPosition - window.Viewer.transform.position, startRotation) : startRotation;
            startGrip = data is BrowserRayPointerEventData pose && pose.HasWorldRotation ? pose.WorldRotation
                : data is BrowserRayPointerEventData direction && direction.HasWorldRay ? Quaternion.LookRotation(direction.WorldRay.direction, Vector3.up) : startRotation;
            window.BringToFront(); Paint();
        }
        private static Quaternion Facing(Vector3 direction, Quaternion fallback) {
            return direction.sqrMagnitude > .0001f && Vector3.Cross(direction.normalized, Vector3.up).sqrMagnitude > .0001f
                ? Quaternion.LookRotation(direction, Vector3.up) : fallback;
        }
        private bool TryPoint(PointerEventData data, out Vector3 world) {
            if (data is BrowserRayPointerEventData ray && ray.HasWorldRay) {
                if (startPlane.Raycast(ray.WorldRay, out float distance) && distance >= 0) { world = ray.WorldRay.GetPoint(distance); return true; }
                world = default; return false;
            }
            return RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)window.transform, data.position, data.pressEventCamera, out world);
        }
        public void OnDrag(PointerEventData data) {
            if (!active) return;
            Vector3 world;
            if (cornerX == 0 && data is BrowserRayPointerEventData ray && ray.HasWorldRay) world = ray.WorldRay.GetPoint(grabDistance);
            else if (!TryPoint(data, out world)) return;
            var rect = (RectTransform)window.transform;
            if (cornerX == 0) {
                Quaternion rotation = startRotation;
                if (window.PlacementMode == BrowserWindowPlacementMode.FaceViewer && window.Viewer != null) {
                    Vector3 translatedCenter = startPosition + world - startPointer;
                    rotation = Facing(translatedCenter - window.Viewer.transform.position, startFacing) * Quaternion.Inverse(startFacing) * startRotation;
                } else if (window.PlacementMode == BrowserWindowPlacementMode.FollowHand && data is BrowserRayPointerEventData pose && pose.HasWorldRay) {
                    Quaternion grip = pose.HasWorldRotation ? pose.WorldRotation : Quaternion.LookRotation(pose.WorldRay.direction, Vector3.up);
                    rotation = grip * Quaternion.Inverse(startGrip) * startRotation;
                }
                rect.SetPositionAndRotation(world - rotation * grabOffset, rotation);
            }
            else {
                Vector3 delta = worldToLocal.MultiplyVector(world - startPointer);
                var resized = BrowserWindowGeometry.Resize(startSize.x, startSize.y, delta.x, delta.y, cornerX, cornerY);
                rect.sizeDelta = new Vector2(resized.Width, resized.Height);
                rect.position = startPosition + localToWorld.MultiplyVector(new Vector3(resized.OffsetX, resized.OffsetY, 0));
            }
        }
        public void OnEndDrag(PointerEventData data) { active = false; window.PlacementChanged(); Paint(); }
        public void OnCancel(BaseEventData data) {
            if (!active) return; active = false;
            var rect = (RectTransform)window.transform; rect.SetPositionAndRotation(startPosition, startRotation); rect.sizeDelta = startSize; Paint();
        }
        private void OnDisable() { if (active && window != null) OnCancel(null); hovered = false; Paint(); }
    }
    public sealed class BrowserWindowActivation : MonoBehaviour, IPointerDownHandler
    {
        public BrowserWindowView Window;
        public void OnPointerDown(PointerEventData data) { if (Window != null) Window.BringToFront(); }
    }

}
