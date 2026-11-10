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
            value.alignment = TextAnchor.MiddleLeft; value.raycastTarget = false; value.supportRichText = false;
            value.horizontalOverflow = HorizontalWrapMode.Wrap; value.verticalOverflow = VerticalWrapMode.Truncate;
            return value;
        }
        internal static Button Button(Transform parent, string name, string label, Font font, float width = 100)
        {
            var value = Node(parent, name, typeof(Image), typeof(Button), typeof(LayoutElement));
            var image = value.GetComponent<Image>(); image.color = ButtonColor;
            var button = value.GetComponent<Button>(); button.targetGraphic = image;
            var layout = value.GetComponent<LayoutElement>(); layout.layoutPriority = 2; layout.minHeight = 44; layout.minWidth = layout.preferredWidth = width; layout.flexibleWidth = 0;
            var text = Label(value.transform, "Label", label, font); text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, 5);
            return button;
        }
        internal static LayoutElement FlexibleWidth(GameObject value, float minimum = 0) {
            var size = value.GetComponent<LayoutElement>() ?? value.AddComponent<LayoutElement>();
            size.layoutPriority = 2; size.minWidth = size.preferredWidth = minimum; size.flexibleWidth = 1;
            return size;
        }
        internal static BrowserAddressInput Input(Transform parent, string name, Font font, string hint = "", float minimum = 100) {
            var node = Node(parent, name, typeof(Image), typeof(BrowserAddressInput));
            node.GetComponent<Image>().color = new Color(.11f, .14f, .2f, 1); FlexibleWidth(node, minimum);
            var input = node.GetComponent<BrowserAddressInput>(); input.targetGraphic = node.GetComponent<Image>();
            var viewport = (RectTransform)Node(node.transform, "Text viewport", typeof(RectMask2D)).transform; Stretch(viewport, 8);
            input.textComponent = Label(viewport, "Text", "", font); Stretch(input.textComponent.rectTransform);
            var placeholder = Label(viewport, "Hint", hint, font); placeholder.color = Color.gray; Stretch(placeholder.rectTransform); input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine; input.keyboardType = TouchScreenKeyboardType.Default; input.shouldHideMobileInput = false;
            input.characterLimit = 8192;
            return input;
        }
        internal static Scrollbar VerticalScrollbar(Transform parent) {
            var node = Node(parent, "Vertical scrollbar", typeof(Image), typeof(Scrollbar));
            var rect = (RectTransform)node.transform; rect.anchorMin = new Vector2(1, 0); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(1, .5f); rect.sizeDelta = new Vector2(12, -16); rect.anchoredPosition = new Vector2(-3, 0);
            node.GetComponent<Image>().color = new Color(.07f, .09f, .13f);
            var track = (RectTransform)Node(node.transform, "Sliding area").transform; Stretch(track);
            var handle = Node(track, "Handle", typeof(Image)).GetComponent<Image>(); handle.color = new Color(.4f, .5f, .63f); Stretch(handle.rectTransform);
            var bar = node.GetComponent<Scrollbar>(); bar.handleRect = handle.rectTransform; bar.targetGraphic = handle; bar.direction = Scrollbar.Direction.BottomToTop;
            return bar;
        }
        internal static void Remove(GameObject value)
        {
            if (value == null) return;
            value.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
