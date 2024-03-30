#nullable enable

using UnityEngine;

namespace WebView
{
    internal class WebViewActivityManager
    {
        private const string NATIVE_CLASS_NAME__UNITY_PLAYER_ACTIVITY = "com.t34400.webviewtexture.WebViewUnityPlayerActivity";
        private const string NATIVE_FIELD_NAME__CURRENT_ACTIVITY = "currentWebViewActivity";
        private const string NATIVE_METHOD_NAME__GENERATE_WEBVIEW_TEXTURE_PROVIDER = "generateWebViewTextureProvider";
        private const string NATIVE_METHOD_NAME__REMOVE_WEBVIEW_TEXTURE_PROVIDER = "removeWebViewTextureProvider";
        private const string NATIVE_METHOD_NAME__GET_BITMAP_BYTES = "getBitmapBytes";

        private string gameObjectName;
        private AndroidJavaObject? currentActivity;
        private WebViewController? webViewController;

        internal WebViewActivityManager(PointerEventSource pointerEventSource, string gameObjectName, int webViewWidth, int webViewHeight, int textureWidth, int textureHeight, long intervalMilliSec)
        {
            this.gameObjectName = gameObjectName;

            using(AndroidJavaClass webViewUnityPlayerActivity = new(NATIVE_CLASS_NAME__UNITY_PLAYER_ACTIVITY))
            {
                currentActivity = webViewUnityPlayerActivity.GetStatic<AndroidJavaObject>(NATIVE_FIELD_NAME__CURRENT_ACTIVITY);

                if(currentActivity != null)
                {
                    var webViewManager = currentActivity.Call<AndroidJavaObject>(NATIVE_METHOD_NAME__GENERATE_WEBVIEW_TEXTURE_PROVIDER, 
                        gameObjectName, webViewWidth, webViewHeight, textureWidth, textureHeight, intervalMilliSec);
                    if(webViewManager != null)
                    {
                        webViewController = new(pointerEventSource, webViewManager);
                    }
                }
            }
        }

        internal byte[]? GetBitmapBytes()
        {
            return (byte[]?) (System.Array?) currentActivity?.Call<sbyte[]>(NATIVE_METHOD_NAME__GET_BITMAP_BYTES);
        }

        internal void OnDestroy()
        {
            if(currentActivity != null)
            {
                webViewController?.OnDestroy();

                currentActivity.Call(NATIVE_METHOD_NAME__REMOVE_WEBVIEW_TEXTURE_PROVIDER, gameObjectName);
                currentActivity.Dispose();
                currentActivity = null;
            }
        }

        internal IWebViewController? GetWebViewController() => webViewController;
    }
}