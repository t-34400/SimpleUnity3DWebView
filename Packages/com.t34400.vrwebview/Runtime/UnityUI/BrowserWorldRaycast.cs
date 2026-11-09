using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView
{
    /// <summary>World-space UI picking without routing a hand ray through mouse
    /// displays or a camera-derived ray. uGUI still supplies graphic/mask/group filters.</summary>
    public static class BrowserWorldRaycast
    {
        public static bool TryRaycast(GraphicRaycaster source, Ray ray, Camera camera, out RaycastResult result) {
            result = default;
            if (source == null || !source.isActiveAndEnabled || camera == null) return false;
            var canvas = source.GetComponent<Canvas>(); if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) return false;
            var graphics = GraphicRegistry.GetGraphicsForCanvas(canvas);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < graphics.Count; i++) {
                var graphic = graphics[i];
                if (graphic == null || !graphic.isActiveAndEnabled || !graphic.raycastTarget || graphic.canvasRenderer.cull || graphic.depth < 0) continue;
                var rect = graphic.rectTransform;
                if (source.ignoreReversedGraphics && Vector3.Dot(ray.direction, rect.forward) <= 0) continue;
                if (!new Plane(rect.forward, rect.position).Raycast(ray, out float distance) || distance < 0) continue;
                var world = ray.GetPoint(distance); Vector3 local = rect.InverseTransformPoint(world);
                if (!rect.rect.Contains(new Vector2(local.x, local.y))) continue;
                Vector3 projected = camera.WorldToScreenPoint(world);
                if (projected.z <= 0 || projected.z > camera.farClipPlane) continue;
                // Preserve masks, CanvasGroup.blocksRaycasts and custom Graphic filters.
                // Only the shape hit uses the physical ray; the screen point is filter metadata.
                var screen = new Vector2(projected.x, projected.y);
                if (!graphic.Raycast(screen, source.eventCamera ?? camera)) continue;
                if (distance > nearest + .0001f || (Mathf.Abs(distance - nearest) <= .0001f && result.gameObject != null && graphic.depth <= result.depth)) continue;
                nearest = distance;
                result = new RaycastResult { gameObject = graphic.gameObject, module = source, distance = distance,
                    worldPosition = world, worldNormal = -rect.forward, screenPosition = screen, depth = graphic.depth,
                    sortingOrder = canvas.sortingOrder, sortingLayer = canvas.sortingLayerID };
            }
            return result.gameObject != null;
        }
    }
}
