#nullable enable

using System;
using UnityEngine;

namespace WebView
{
    [Serializable]
    public struct ReceivedData
    {
        public string type;
        public string data;
    }

    public abstract class WebViewReceivedDataManager : MonoBehaviour
    {
        public event Action<ReceivedData>? DataReceived;

        public abstract IWebViewController? WebViewController { get; }
        
        protected void InvokeDataReceivedEvent(ReceivedData receivedData) => DataReceived?.Invoke(receivedData);
    }
}
