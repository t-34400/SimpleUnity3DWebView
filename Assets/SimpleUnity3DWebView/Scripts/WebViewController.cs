#nullable enable

using UnityEngine;

namespace WebView
{
    class WebViewController : IWebViewController
    {
        private readonly PointerEventSource pointerEventSource;
        private readonly WebViewManagerWrapper webViewManagerWrapper;

        internal WebViewController(PointerEventSource pointerEventSource, AndroidJavaObject webViewManager)
        {
            this.pointerEventSource = pointerEventSource;
            webViewManagerWrapper = new WebViewManagerWrapper(webViewManager);

            pointerEventSource.OnPointerDown += InvokeDownEvent;
            pointerEventSource.OnDrag += InvokeMoveEvent;
            pointerEventSource.OnPointerUp += InvokeUpEvent;
        }

        internal void OnDestroy()
        {
            pointerEventSource.OnPointerDown -= InvokeDownEvent;
            pointerEventSource.OnDrag -= InvokeMoveEvent;
            pointerEventSource.OnPointerUp -= InvokeUpEvent;
            
            webViewManagerWrapper.OnDestroy();
        }

        public void StartUpdate() => webViewManagerWrapper.StartUpdate();
        public void StopUpdate() => webViewManagerWrapper.StopUpdate();
        public void LoadUrl(string url) => webViewManagerWrapper?.LoadUrl(url);
        public void Reload() => webViewManagerWrapper?.Reload();
        public void GoBack() => webViewManagerWrapper?.GoBack();
        public void GoForward() => webViewManagerWrapper?.GoForward();
        public void SetKeyboardInputEnabled(bool isEnabled) => webViewManagerWrapper?.SetKeyboardInputEnabled(isEnabled);
        public void EvaluateJavascript(string script) => webViewManagerWrapper?.EvaluateJavascript(script);
        internal void InvokeDownEvent(Vector2 normalizedPoint) => webViewManagerWrapper.InvokeDownEvent(normalizedPoint.x, normalizedPoint.y);
        internal void InvokeMoveEvent(Vector2 normalizedPoint) => webViewManagerWrapper.InvokeMoveEvent(normalizedPoint.x, normalizedPoint.y);
        internal void InvokeUpEvent(Vector2 normalizedPoint) => webViewManagerWrapper.InvokeUpEvent(normalizedPoint.x, normalizedPoint.y);

        private class WebViewManagerWrapper
        {
            private const string NATIVE_METHOD_NAME__START_UPDATE = "startUpdate";
            private const string NATIVE_METHOD_NAME__STOP_UPDATE = "stopUpdate";
            private const string NATIVE_METHOD_NAME__LOAD_URL = "loadUrl";
            private const string NATIVE_METHOD_NAME__RELOAD = "reload";
            private const string NATIVE_METHOD_NAME__GO_BACK = "goBack";
            private const string NATIVE_METHOD_NAME__GO_FORWARD = "goForward";
            private const string NATIVE_METHOD_NAME__SET_KEYBOARD_INPUT_ENABLED = "setKeyboardInputEnabled";


            private const string NATIVE_METHOD_NAME__INVOKE_DOWN_EVENT = "invokeDownEvent";
            private const string NATIVE_METHOD_NAME__INVOKE_MOVE_EVENT = "invokeMoveEvent";
            private const string NATIVE_METHOD_NAME__INVOKE_UP_EVENT = "invokeUpEvent";

            private const string NATIVE_METHOD_NAME__EVALUATE_JAVASCRIPT = "evaluateJavascript";

            private AndroidJavaObject? webViewManager;
            internal WebViewManagerWrapper(AndroidJavaObject webViewManager)
            {
                this.webViewManager = webViewManager;
            }

            internal void OnDestroy()
            {
                if(webViewManager != null)
                {
                    webViewManager.Dispose();
                    webViewManager = null;
                }
            }

            internal void StartUpdate() => webViewManager?.Call(NATIVE_METHOD_NAME__START_UPDATE);
            internal void StopUpdate() => webViewManager?.Call(NATIVE_METHOD_NAME__STOP_UPDATE);
            internal void LoadUrl(string url) => webViewManager?.Call(NATIVE_METHOD_NAME__LOAD_URL, url);
            internal void Reload() => webViewManager?.Call(NATIVE_METHOD_NAME__RELOAD);
            internal void GoBack() => webViewManager?.Call(NATIVE_METHOD_NAME__GO_BACK);
            internal void GoForward() => webViewManager?.Call(NATIVE_METHOD_NAME__GO_FORWARD);
            internal void SetKeyboardInputEnabled(bool isEnabled) => webViewManager?.Call(NATIVE_METHOD_NAME__SET_KEYBOARD_INPUT_ENABLED, isEnabled);
            internal void InvokeDownEvent(float normalizedX, float normalizedY) => webViewManager?.Call(NATIVE_METHOD_NAME__INVOKE_DOWN_EVENT, normalizedX, 1.0f - normalizedY);
            internal void InvokeMoveEvent(float normalizedX, float normalizedY) => webViewManager?.Call(NATIVE_METHOD_NAME__INVOKE_MOVE_EVENT, normalizedX, 1.0f - normalizedY);
            internal void InvokeUpEvent(float normalizedX, float normalizedY) => webViewManager?.Call(NATIVE_METHOD_NAME__INVOKE_UP_EVENT, normalizedX, 1.0f - normalizedY);
            internal void EvaluateJavascript(string script) => webViewManager?.Call(NATIVE_METHOD_NAME__EVALUATE_JAVASCRIPT, script);
        }
    }
}
