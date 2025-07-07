#nullable enable

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WebView
{
    public class WebViewManager : MonoBehaviour
    {
        [SerializeField] private RawImage webViewImage = default!;
        [SerializeField] private PointerEventSource pointerEventSource = default!;
        [SerializeField] private int textureWidth = 1280;
        [SerializeField] private int intervalMSec = 100;
        [SerializeField] private string defaultUrl = "https://www.google.com/";
        [SerializeField] private Vector2 normalizedTouchSlop = Vector2.one * 0.05f;

        [Header("Events")]
        [SerializeField] private UnityEvent<string> urlChanged = default!;
        [SerializeField] private UnityEvent<ReceivedData> dataReceived = default!;

        private WebViewJavaBridge? bridge;
        private WebViewDataReceiver? receiver;
        private WebViewTextureUpdater? textureUpdater;

        public void LoadUrl(string url) => bridge?.LoadUrl(url);
        public void Reload() => bridge?.Reload();
        public void GoBack() => bridge?.GoBack();
        public void GoForward() => bridge?.GoForward();
        public void SetKeyboardInputEnabled(bool enabled) => bridge?.SetKeyboardEnabled(enabled);
        public void EvaluateJavascript(string script) => bridge?.EvaluateJavascript(script);

        private void Start()
        {
            var id = Guid.NewGuid().ToString();

            var imageRectSize = webViewImage.rectTransform.sizeDelta;
            var aspect = imageRectSize.y / imageRectSize.x;
            var textureHeight = (int)(textureWidth * aspect);

            var receiverObject = new GameObject(id);
            receiverObject.transform.parent = transform;

            receiver = receiverObject.AddComponent<WebViewDataReceiver>();
            receiver.UrlChanged += urlChanged.Invoke;
            receiver.DataReceived += dataReceived.Invoke;

            bridge = new(
                id,
                textureWidth, textureHeight,
                intervalMSec,
                normalizedTouchSlop,
                id,
                WebViewDataReceiver.UrlChangedMessageMethodName,
                WebViewDataReceiver.JsonMessageMethodName
            );

            var texture = new Texture2D(textureWidth, textureHeight);
            webViewImage.texture = texture;

            textureUpdater = new WebViewTextureUpdater(bridge, texture);

            pointerEventSource.OnPointerDown += bridge.SendTouchDown;
            pointerEventSource.OnPointerUp += bridge.SendTouchUp;
            pointerEventSource.OnDrag += bridge.SendTouchMove;

            if (!string.IsNullOrEmpty(defaultUrl))
                bridge.LoadUrl(defaultUrl);

            bridge.StartUpdate();
        }

        private void OnEnable()
        {
            bridge?.StartUpdate();
        }

        private void Update()
        {
            if (textureUpdater == null)
                return;

            if (textureUpdater.TryUpdateTexture())
            {
                webViewImage.texture = textureUpdater.Texture;
            }
        }

        private void OnDisable()
        {
            bridge?.StopUpdate();
        }

        private void OnDestroy()
        {
            if (receiver != null)
            {
                receiver.UrlChanged -= urlChanged.Invoke;
                receiver.DataReceived -= dataReceived.Invoke;
            }

            if (pointerEventSource != null && bridge != null)
            {
                pointerEventSource.OnPointerDown -= bridge.SendTouchDown;
                pointerEventSource.OnPointerUp -= bridge.SendTouchUp;
                pointerEventSource.OnDrag -= bridge.SendTouchMove;
            }

            bridge?.Dispose();
            bridge = null;
        }
    }
}