# nullable enable

using System;

namespace WebView
{
    [Serializable]
    public struct ReceivedData
    {
        public string type;
        public string data;
    }
}