#nullable enable

using System;
using UnityEngine;

namespace WebView
{
    class WebViewJavaBridge : IDisposable
    {
        private AndroidJavaObject bridge;

        private readonly Vector2 normalizedTouchSlop;
        private Vector2 normalizedDownPoint;

        public WebViewJavaBridge(
            string id,
            int width, int height,
            long intervalMSec,
            Vector2 normalizedTouchSlop,
            string receiverObjectName,
            string urlChangedMessageMethodName,
            string jsonMessageMethodName)
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    var resolver = new AndroidJavaObject(
                        "com.t34400.webviewtexture.unitybridge.UnityPlayerEnvironmentResolver"
                    );

#if !UNITY_2023_1_OR_NEWER
                    resolver.Call("registerLegacyUnityPlayer", activity.Get<AndroidJavaObject>("mUnityPlayer"));
# endif

                    bridge = new AndroidJavaObject(
                        "com.t34400.webviewtexture.unitybridge.WebViewUnityBridge",
                        activity,
                        resolver,
                        id,
                        width, height,
                        intervalMSec,
                        receiverObjectName,
                        urlChangedMessageMethodName,
                        jsonMessageMethodName
                    );
                }
            }
        }

        public int GetFrameIndex() => bridge.Call<int>("getFrameIndex");
        public byte[] GetFrameBytes()
        {
            var sbytes = bridge.Call<sbyte[]>("getFrameBytes");
            return Array.ConvertAll(sbytes, b => unchecked((byte)b));
        }

        public void SetKeyboardEnabled(bool enabled) => bridge.Call("setKeyboardEnabled", enabled);
        public void LoadUrl(string url) => bridge.Call("loadUrl", url);
        public void Reload() => bridge.Call("reload");
        public void GoBack() => bridge.Call("goBack");
        public void GoForward() => bridge.Call("goForward");
        public void EvaluateJavascript(string script) => bridge.Call("evaluateJavascript", script);

        public void StartUpdate() => bridge.Call("startUpdate");
        public void StopUpdate() => bridge.Call("stopUpdate");

        public void SendTouchDown(Vector2 normalizedPoint) => SendTouchDown(normalizedPoint.x, normalizedPoint.y);
        public void SendTouchMove(Vector2 normalizedPoint) => SendTouchMove(normalizedPoint.x, normalizedPoint.y);
        public void SendTouchUp(Vector2 normalizedPoint) => SendTouchUp(normalizedPoint.x, normalizedPoint.y);
        public void SendTouchDown(float x, float y)
        {
            normalizedDownPoint = new Vector2(x, y);

            bridge.Call("sendTouchDown", x, 1f - y);
        }

        public void SendTouchMove(float x, float y)
        {
            if (!IsTouchSlopExceeded(x, y))
            {
                return;
            }

            bridge.Call("sendTouchMove", x, 1f - y);
        }

        public void SendTouchUp(float x, float y)
        {
            if (!IsTouchSlopExceeded(x, y))
            {
                x = normalizedDownPoint.x;
                y = normalizedDownPoint.y;
            }

            bridge.Call("sendTouchUp", x, 1f - y);
        }

        public void Dispose()
        {
            bridge.Call("destory");
            bridge.Dispose();
        }

        private bool IsTouchSlopExceeded(float x, float y)
        {
            return Mathf.Abs(normalizedDownPoint.x - x) > normalizedTouchSlop.x
                || Mathf.Abs(normalizedDownPoint.y - y) > normalizedTouchSlop.y;
        }
    }
}