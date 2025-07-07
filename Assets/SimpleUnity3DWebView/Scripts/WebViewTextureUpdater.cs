#nullable enable

using UnityEngine;

namespace WebView
{
    class WebViewTextureUpdater
    {
        private WebViewJavaBridge bridge;
        private Texture2D texture;

        private int lastFrameIndex = 0;

        public Texture2D Texture => texture;

        public WebViewTextureUpdater(WebViewJavaBridge bridge, Texture2D texture)
        {
            this.bridge = bridge;
            this.texture = texture;
        }

        public bool TryUpdateTexture()
        {
            var currentIndex = bridge.GetFrameIndex();

            if (currentIndex == lastFrameIndex)
                return false;

            lastFrameIndex = currentIndex;

            var frameBytes = bridge.GetFrameBytes();
            texture.LoadImage(frameBytes);
            texture.Apply();

            return true;
        }
    }
}