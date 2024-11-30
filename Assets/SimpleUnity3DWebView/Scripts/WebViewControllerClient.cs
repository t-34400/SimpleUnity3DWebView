#nullable enable

using System.Collections;
using UnityEngine;

namespace WebView
{
    public class WebViewControllerClient : MonoBehaviour
    {
        [SerializeField] private WebViewBitmapReceiver webViewBitmapReceiver = default!;
        [SerializeField] private bool loadOnFirstFrame = true;
        [SerializeField] private string loadUrl = "https://www.google.com/";

        private IWebViewController? webViewController;

        
        public void StartUpdate() => webViewController?.StartUpdate();
        public void StopUpdate() => webViewController?.StopUpdate();
        public void LoadUrl(string url) => webViewController?.LoadUrl(url);
        public void Reload() => webViewController?.Reload();
        public void GoBack() => webViewController?.GoBack();
        public void GoForward() => webViewController?.GoForward();
        public void SetKeyboardInputEnabled(bool isEnabled) => webViewController?.SetKeyboardInputEnabled(isEnabled);
        public void EvaluateJavascript(string script) => webViewController?.EvaluateJavascript(script);

        private void Start()
        {
            StartCoroutine(StartUpdateCoroutine());
        }

        private IEnumerator StartUpdateCoroutine()
        {
            yield return null;
            webViewController = webViewBitmapReceiver.WebViewController;
            webViewController?.StartUpdate();
            if (loadOnFirstFrame)
            {
                webViewController?.LoadUrl(loadUrl);
            }
        }
    }
}