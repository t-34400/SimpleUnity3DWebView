using UnityEngine;
using UnityEngine.UI;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Builds a UI under an existing Canvas. Does not create an XR rig,
    /// EventSystem, camera, WebView backend, texture or default web page.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class BrowserPanelBuilder : MonoBehaviour
    {
        [SerializeField] private BrowserPageFactory factory;
        [SerializeField] private BrowserPanel builtPanel;
        [SerializeField] private string newTabAddress = "";
        public BrowserPanel Panel => builtPanel;
        private void Start()
        {
            var panel = Build();
            panel.SetNewTabAddress(newTabAddress);
            if (factory != null) panel.Configure(factory.CreatePage);
        }
        [ContextMenu("Build Browser UI")] private void BuildFromMenu() { Build(); }

        public BrowserPanel Build()
        {
            if (builtPanel != null) return builtPanel;
            if (GetComponentInParent<Canvas>() == null) throw new System.InvalidOperationException("Place BrowserPanelBuilder under a Canvas");
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = BrowserSampleWidgets.Node(transform, "Browser UI", typeof(Image), typeof(VerticalLayoutGroup));
            try {
                BrowserSampleWidgets.Stretch(root.GetComponent<RectTransform>());
                root.GetComponent<Image>().color = BrowserSampleWidgets.Background; root.GetComponent<Image>().raycastTarget = false;
                var column = root.GetComponent<VerticalLayoutGroup>(); column.padding = new RectOffset(12, 12, 12, 12); column.spacing = 8;
                column.childControlWidth = true; column.childControlHeight = true; column.childForceExpandWidth = true; column.childForceExpandHeight = false;
                var owner = BrowserSampleWidgets.Node(root.transform, "Browser context", typeof(BrowserDispatcher), typeof(LayoutElement));
                owner.GetComponent<LayoutElement>().ignoreLayout = true;
                var dispatcher = owner.GetComponent<BrowserDispatcher>();

                var tabsRow = Row(root.transform, "Tabs", 52);
                var scrollRoot = BrowserSampleWidgets.Node(tabsRow.transform, "Tab scroll", typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
                scrollRoot.GetComponent<Image>().color = BrowserSampleWidgets.Background;
                scrollRoot.GetComponent<LayoutElement>().flexibleWidth = 1; scrollRoot.GetComponent<LayoutElement>().minWidth = 160;
                var viewport = BrowserSampleWidgets.Node(scrollRoot.transform, "Viewport", typeof(Image), typeof(Mask));
                BrowserSampleWidgets.Stretch(viewport.GetComponent<RectTransform>()); viewport.GetComponent<Mask>().showMaskGraphic = false;
                var content = BrowserSampleWidgets.Node(viewport.transform, "Tab buttons", typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
                var contentRect = content.GetComponent<RectTransform>(); contentRect.anchorMin = Vector2.zero; contentRect.anchorMax = new Vector2(0, 1);
                contentRect.pivot = new Vector2(0, .5f); contentRect.sizeDelta = Vector2.zero;
                var horizontal = content.GetComponent<HorizontalLayoutGroup>(); horizontal.spacing = 6; horizontal.childControlWidth = true; horizontal.childControlHeight = true;
                horizontal.childForceExpandWidth = false; horizontal.childForceExpandHeight = true;
                content.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                var scroll = scrollRoot.GetComponent<ScrollRect>(); scroll.content = contentRect; scroll.viewport = viewport.GetComponent<RectTransform>();
                scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
                var create = BrowserSampleWidgets.Button(tabsRow.transform, "New tab", "New", font);
                var close = BrowserSampleWidgets.Button(tabsRow.transform, "Close tab", "Close", font);

                var addressRow = Row(root.transform, "Address bar", 52);
                var fieldObject = BrowserSampleWidgets.Node(addressRow.transform, "Requested URL", typeof(Image), typeof(InputField), typeof(LayoutElement));
                fieldObject.GetComponent<Image>().color = new Color(.11f, .14f, .2f, 1);
                fieldObject.GetComponent<LayoutElement>().flexibleWidth = 1; fieldObject.GetComponent<LayoutElement>().minWidth = 160;
                var text = BrowserSampleWidgets.Label(fieldObject.transform, "Text", "", font); BrowserSampleWidgets.Stretch(text.rectTransform, 10);
                var placeholder = BrowserSampleWidgets.Label(fieldObject.transform, "Placeholder", "Enter an absolute HTTP(S) URL", font);
                placeholder.color = new Color(.6f, .66f, .75f, 1); BrowserSampleWidgets.Stretch(placeholder.rectTransform, 10);
                var address = fieldObject.GetComponent<InputField>(); address.targetGraphic = fieldObject.GetComponent<Image>();
                address.textComponent = text; address.placeholder = placeholder; address.lineType = InputField.LineType.SingleLine;
                address.contentType = InputField.ContentType.Standard; address.keyboardType = TouchScreenKeyboardType.Default;
                var go = BrowserSampleWidgets.Button(addressRow.transform, "Navigate", "Go", font);

                var page = BrowserSampleWidgets.Node(root.transform, "Page", typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView), typeof(LayoutElement));
                page.GetComponent<LayoutElement>().flexibleHeight = 1; page.GetComponent<LayoutElement>().minHeight = 120;
                var status = BrowserSampleWidgets.Label(root.transform, "Status", "Browser not configured.", font, 18);
                var statusLayout = status.gameObject.AddComponent<LayoutElement>();
                statusLayout.minHeight = statusLayout.preferredHeight = 40; statusLayout.flexibleHeight = 0;
                var panel = root.AddComponent<BrowserPanel>();
                panel.SetWidgets(dispatcher, page.GetComponent<BrowserView>(), address, go, create, close, contentRect, status, font);
                panel.SetNewTabAddress(newTabAddress);
                builtPanel = panel;
                return panel;
            } catch { BrowserSampleWidgets.Remove(root); throw; }
        }

        private static GameObject Row(Transform parent, string name, float height)
        {
            var row = BrowserSampleWidgets.Node(parent, name, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var size = row.GetComponent<LayoutElement>();
            size.minHeight = size.preferredHeight = height; size.flexibleHeight = 0;
            var layout = row.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
            return row;
        }
    }
}
