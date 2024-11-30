#nullable enable

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WebView
{
    public interface IWebViewController
    {
        void StartUpdate();
        void StopUpdate();
        void LoadUrl(string url);
        void Reload();
        void GoBack();
        void GoForward();
        void SetKeyboardInputEnabled(bool isEnabled);
        void EvaluateJavascript(string script);
    }

    class WebViewBitmapReceiver : WebViewReceivedDataManager
    {
        [SerializeField] private RawImage image = default!;
        [SerializeField] private PointerEventSource pointerEventSource = default!;
        [SerializeField] private Vector2Int webViewSize = new Vector2Int(600, 480);
        [SerializeField] private Vector2Int textureSize = new Vector2Int(300, 240);
        [SerializeField] private float intervalSec = 0.05f;
        [SerializeField] private bool keyboardEnabled = true;
        [SerializeField] private UnityEvent<string> urlChanged = default!;
        [SerializeField] private UnityEvent<string, string> dataReceived = default!;

        private IWebViewController? webViewController;
        private WebViewActivityManager? webViewActivityManager;

        private Texture2D? texture;

        private float previousUpdateTime = float.NegativeInfinity;
        private object isUpdatedLock = new object();
        private bool isUpdated = false;
        private object bitmapByteArrayLock = new object();
        private byte[]? bitmapByteArray;

        private object newUrlLock = new object();
        private string? newUrl = null;

        private object receivedDataLock = new object();
        private List<ReceivedData> receivedDataList = new List<ReceivedData>();
        
        public override IWebViewController? WebViewController => webViewController;

        private Texture2D Texture
        {
            get
            {
                texture ??= new Texture2D(1, 1);
                return texture;
            }
        }

        private bool IsUpdated
        {
            get
            {
                lock(isUpdatedLock)
                {
                    return isUpdated;
                }
            }
            set
            {
                lock(isUpdatedLock)
                {
                    isUpdated = value;
                }
            }
        }

        public void StartUpdate() => webViewController?.StartUpdate();
        public void StopUpdate() => webViewController?.StopUpdate();
        public void LoadUrl(string url) => webViewController?.LoadUrl(url);

        private void Start()
        {
            webViewActivityManager = new(pointerEventSource, gameObject.name,  webViewSize.x, webViewSize.y, textureSize.x, textureSize.y, (long)(intervalSec * 1000));
            webViewController = webViewActivityManager?.GetWebViewController();
            webViewController?.SetKeyboardInputEnabled(keyboardEnabled);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                webViewActivityManager = new(pointerEventSource, gameObject.name, webViewSize.x, webViewSize.y, textureSize.x, textureSize.y, (long)(intervalSec * 1000));
                webViewController = webViewActivityManager?.GetWebViewController();
                webViewController?.SetKeyboardInputEnabled(keyboardEnabled);
            }
        }

        private void Update()
        {
            lock (receivedDataLock)
            {
                foreach (var receivedData in receivedDataList)
                {
                    Debug.Log($"Data received: {receivedData.type} {receivedData.data}");
                    InvokeDataReceivedEvent(receivedData);
                    dataReceived?.Invoke(receivedData.type, receivedData.data);
                }
                receivedDataList.Clear();
            }
            lock (newUrlLock)
            {
                if (newUrl != null)
                {
                    urlChanged?.Invoke(newUrl);
                    newUrl = null;
                }
            }

            UpdateTextureIfNeeded();
        }

        private void UpdateTextureIfNeeded()
        {
            var currentTime = Time.time;
            if(currentTime - previousUpdateTime > intervalSec)
            {
                var dataReceived = IsUpdated;
                IsUpdated = false;

                lock(bitmapByteArrayLock)
                {
                    dataReceived = dataReceived && bitmapByteArray != null;

                    if(dataReceived)
                    {
                        Texture.LoadImage(bitmapByteArray);
                        Texture.Apply();
                    }
                }

                if(dataReceived)
                {
                    image.texture = Texture;
                    previousUpdateTime = currentTime;
                }
            }
        }

        private void OnDestroy()
        {
            webViewActivityManager?.OnDestroy();
        }

        public void ReceiveUpdateCallback()
        {
            lock(bitmapByteArrayLock)
            {
                bitmapByteArray = webViewActivityManager?.GetBitmapBytes();
            }
            IsUpdated = true;
        }

        public void ReceiveNewUrl(string url)
        {
            lock (newUrlLock)
            {
                newUrl = url;
            }
        }

        public void ReceiveJsonData(string json)
        {
            var data = JsonUtility.FromJson<ReceivedData>(json);
            lock(receivedDataLock)
            {
                receivedDataList.Add(data);
            }
        }
    }
}
