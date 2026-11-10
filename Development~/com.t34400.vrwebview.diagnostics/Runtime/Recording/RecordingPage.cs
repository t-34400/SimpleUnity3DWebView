using System;
using UnityEngine;

namespace T34400.VRWebView
{
    /// <summary>Maintainer-only offline page for recording the real core panel.</summary>
    public sealed class RecordingPage : MonoBehaviour
    {
        public BrowserController browser;
        public TextAsset pageTemplate;
        public TextAsset video;

        private async void Start()
        {
            try {
                if (browser == null || pageTemplate == null || video == null)
                    throw new InvalidOperationException("Recording page assets are missing");
                var session = browser.Session;
                if (session == null) throw new InvalidOperationException("Enable BrowserController first");
                await session.InitializeAsync();
                string html = pageTemplate.text.Replace("__VIDEO_BASE64__", Convert.ToBase64String(video.bytes));
                await session.LoadHtmlAsync(html, "https://vrwebview.invalid/");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (this != null && isActiveAndEnabled) Debug.LogException(error, this); }
        }
    }
}
