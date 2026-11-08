using System;
using UnityEngine;
using UnityEngine.UI;

namespace T34400.VRWebView.Samples.Browser
{
    internal static class BrowserSampleWidgets
    {
        internal static readonly Color Background = new Color(.055f, .075f, .11f, 1);
        internal static readonly Color ButtonColor = new Color(.16f, .21f, .29f, 1);
        internal static readonly Color SelectedColor = new Color(.12f, .39f, .63f, 1);
        internal static GameObject Node(Transform parent, string name, params Type[] components)
        {
            var value = new GameObject(name, typeof(RectTransform)); value.transform.SetParent(parent, false);
            foreach (var component in components) value.AddComponent(component);
            return value;
        }
        internal static void Stretch(RectTransform rect, float padding = 0)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding); rect.offsetMax = new Vector2(-padding, -padding);
        }
        internal static Text Label(Transform parent, string name, string text, Font font, int size = 20)
        {
            var value = Node(parent, name, typeof(Text)).GetComponent<Text>();
            value.font = font; value.fontSize = size; value.text = text; value.color = Color.white;
            value.alignment = TextAnchor.MiddleLeft; value.raycastTarget = false;
            return value;
        }
        internal static Button Button(Transform parent, string name, string label, Font font, float width = 100)
        {
            var value = Node(parent, name, typeof(Image), typeof(Button), typeof(LayoutElement));
            var image = value.GetComponent<Image>(); image.color = ButtonColor;
            var button = value.GetComponent<Button>(); button.targetGraphic = image;
            var layout = value.GetComponent<LayoutElement>(); layout.minHeight = 44; layout.preferredWidth = width; layout.flexibleWidth = 0;
            var text = Label(value.transform, "Label", label, font); text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, 5);
            return button;
        }
        internal static void Remove(GameObject value)
        {
            if (value == null) return;
            value.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
