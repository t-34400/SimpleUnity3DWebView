#nullable enable

using System;
using UnityEngine;

namespace WebView
{
    class WebViewDataReceiver : MonoBehaviour
    {
        public static string UrlChangedMessageMethodName => nameof(OnUrlChanged);
        public static string JsonMessageMethodName => nameof(OnDataReceived);

        public event Action<string>? UrlChanged;
        public event Action<ReceivedData>? DataReceived;

        public void OnUrlChanged(string url)
        {
            UrlChanged?.Invoke(url);
        }

        public void OnDataReceived(string json)
        {
            ReceivedData? data = JsonUtility.FromJson<ReceivedData>(json);

            if (data != null)
                DataReceived?.Invoke(data.Value);
        }
    }
}