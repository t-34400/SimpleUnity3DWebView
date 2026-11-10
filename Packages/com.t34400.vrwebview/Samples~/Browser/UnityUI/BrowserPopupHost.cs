using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>One local popup per window. Does not change page layout or native focus.</summary>
    public sealed class BrowserPopupHost : MonoBehaviour {
        private GameObject layer;
        private RectTransform card, content, anchor;
        private Vector3? anchorLocal;
        private float desiredWidth;
        private bool rightAligned;
        public bool IsOpen => layer != null;
        public string Kind { get; private set; }
        public RectTransform Card => card;
        public event Action Closed;
        public Transform Open(string kind, RectTransform source, float width, bool alignRight = true) {
            if (source != null && layer != null && source.IsChildOf(layer.transform)) {
                var corners = new Vector3[4]; source.GetWorldCorners(corners); return OpenCore(kind, null, corners[3], width, alignRight);
            }
            return OpenCore(kind, source, null, width, alignRight);
        }
        public Transform OpenAt(string kind, Vector3 source, float width) => OpenCore(kind, null, source, width, false);
        private Transform OpenCore(string kind, RectTransform source, Vector3? world, float width, bool alignRight) {
            Close(); Kind = kind; anchor = source; anchorLocal = world.HasValue ? transform.InverseTransformPoint(world.Value) : (Vector3?)null; desiredWidth = width; rightAligned = alignRight;
            layer = BrowserSampleWidgets.Node(transform, "Popup dismiss layer", typeof(Image), typeof(BrowserPopupDismiss));
            BrowserSampleWidgets.Stretch((RectTransform)layer.transform); layer.GetComponent<Image>().color = Color.clear;
            layer.GetComponent<BrowserPopupDismiss>().Host = this;
            var node = BrowserSampleWidgets.Node(layer.transform, kind + " popup", typeof(Image), typeof(Outline), typeof(RectMask2D), typeof(ScrollRect), typeof(BrowserPopupSurface));
            card = (RectTransform)node.transform; card.anchorMin = card.anchorMax = card.pivot = new Vector2(0, 1);
            node.GetComponent<Image>().color = new Color(.085f, .11f, .16f, 1); node.GetComponent<Outline>().effectColor = new Color(.3f, .38f, .48f, 1);
            var body = BrowserSampleWidgets.Node(card, "Popup content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content = (RectTransform)body.transform; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(-18, 0); content.anchoredPosition = new Vector2(-9, 0);
            var layout = body.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(12, 12, 12, 12); layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = node.GetComponent<ScrollRect>(); scroll.viewport = card; scroll.content = content; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = BrowserSampleWidgets.VerticalScrollbar(card); scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }
        public void Reflow() {
            if (!IsOpen) return;
            card.sizeDelta = new Vector2(Mathf.Min(desiredWidth, ((RectTransform)transform).rect.width - 16), 100);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content); Position();
        }
        private void Position() {
            if (card == null) return;
            var window = (RectTransform)transform; Rect bounds = window.rect; Vector3 point;
            if (anchor != null) { var corners = new Vector3[4]; anchor.GetWorldCorners(corners); point = corners[3]; }
            else point = window.TransformPoint(anchorLocal ?? new Vector3(bounds.xMax - 16, bounds.yMax - 150, 0));
            Vector3 local = window.InverseTransformPoint(point);
            var rect = BrowserPopupGeometry.Place(bounds.width, bounds.height, local.x - bounds.xMin, bounds.yMax - local.y,
                desiredWidth, Mathf.Min(560, Mathf.Max(64, LayoutUtility.GetPreferredHeight(content))), rightAligned);
            card.anchoredPosition = new Vector2(rect.X, -rect.Y); card.sizeDelta = new Vector2(rect.Width, rect.Height);
        }
        private void LateUpdate() { if (IsOpen) Position(); }
        public void Close() {
            if (layer == null) return;
            var old = layer; layer = null; card = content = anchor = null; anchorLocal = null; Kind = null;
            foreach (var input in old.GetComponentsInChildren<InputField>(true)) input.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(old.transform))
                EventSystem.current.SetSelectedGameObject(null);
            BrowserSampleWidgets.Remove(old); Closed?.Invoke();
        }
        private void OnDisable() { Close(); }
    }
    public sealed class BrowserPopupDismiss : MonoBehaviour, IPointerDownHandler, IPointerClickHandler {
        public BrowserPopupHost Host;
        public void OnPointerDown(PointerEventData data) { data.eligibleForClick = false; Host.Close(); data.Use(); }
        public void OnPointerClick(PointerEventData data) { data.Use(); }
    }
    // Stop an empty card area from bubbling a press to the outside-dismiss layer.
    public sealed class BrowserPopupSurface : MonoBehaviour, IPointerDownHandler, IPointerClickHandler {
        public void OnPointerDown(PointerEventData data) { }
        public void OnPointerClick(PointerEventData data) { }
    }
}
