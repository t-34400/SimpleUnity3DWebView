using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace T34400.VRWebView
{
    /// <summary>Diagnostic UI pointer: overlay cue terminated at the same physical
    /// hit used for events. Independent of Canvas transparency sorting and depth.</summary>
    internal sealed class ProbePointerVisual : IDisposable
    {
        private readonly GameObject root;
        private readonly LineRenderer line, marker;
        private readonly Material material;
        private readonly Vector3[] circle = new Vector3[25];
        internal ProbePointerVisual() {
            var shader = Resources.Load<Shader>("VRWebViewProbePointer");
            if (shader == null) { Debug.LogError("[VRWebViewSample] BS1 event=pointer_visual_failed reason=shader_missing"); return; }
            if (!shader.isSupported) { Debug.LogError("[VRWebViewSample] BS1 event=pointer_visual_failed reason=shader_unsupported"); return; }
            material = new Material(shader) { enableInstancing = true };
            root = new GameObject("VRWebView pointer visual");
            line = MakeLine("Ray", 32760, .003f); line.positionCount = 2;
            marker = MakeLine("Hit marker", 32761, .0015f); marker.positionCount = circle.Length;
            Hide();
            Debug.Log("[VRWebViewSample] BS1 event=pointer_visual_ready queue=" + material.renderQueue + " order=" + line.sortingOrder);
        }
        private LineRenderer MakeLine(string name, int order, float width) {
            var value = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>(); value.transform.SetParent(root.transform, false);
            value.sharedMaterial = material; value.useWorldSpace = true; value.alignment = LineAlignment.View;
            value.startWidth = value.endWidth = width; value.sortingOrder = order; value.numCapVertices = 3;
            value.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; value.receiveShadows = false;
            return value;
        }
        internal void Show(bool tracked, Ray ray, RaycastResult hit, Camera camera, bool pressed, Color? feedback) {
            if (line == null) return;
            if (!tracked) { Hide(); return; }
            bool hasHit = hit.gameObject != null;
            Color color = feedback ?? (pressed ? Color.yellow : hasHit ? Color.cyan : new Color(.6f, .65f, .7f));
            line.enabled = true; line.startColor = line.endColor = color;
            line.SetPosition(0, ray.origin); line.SetPosition(1, hasHit ? hit.worldPosition : ray.GetPoint(3));
            marker.enabled = hasHit && camera != null;
            if (!marker.enabled) return;
            marker.startColor = marker.endColor = color;
            Vector3 center = hit.worldPosition + (camera.transform.position - hit.worldPosition).normalized * .002f;
            float radius = Mathf.Clamp(Vector3.Distance(camera.transform.position, hit.worldPosition) * .007f, .006f, .018f);
            for (int i = 0; i < circle.Length; i++) {
                float angle = i * Mathf.PI * 2 / (circle.Length - 1);
                circle[i] = center + radius * (Mathf.Cos(angle) * camera.transform.right + Mathf.Sin(angle) * camera.transform.up);
            }
            marker.SetPositions(circle);
        }
        internal void Hide() { if (line != null) line.enabled = false; if (marker != null) marker.enabled = false; }
        public void Dispose() { if (root != null) UnityEngine.Object.Destroy(root); if (material != null) UnityEngine.Object.Destroy(material); }
    }
}
