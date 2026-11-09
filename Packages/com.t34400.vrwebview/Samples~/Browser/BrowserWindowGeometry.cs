using System;
namespace T34400.VRWebView.Samples.Browser
{
    public enum BrowserWindowPlacementMode { FaceViewer, FollowHand, TranslateOnly }
    public readonly struct BrowserWindowResize
    {
        public readonly float Width, Height, OffsetX, OffsetY;
        internal BrowserWindowResize(float width, float height, float x, float y) { Width = width; Height = height; OffsetX = x; OffsetY = y; }
    }
    public static class BrowserWindowGeometry
    {
        /// <summary>Resize about the opposite corner. Deltas are in the original
        /// window's local coordinates, independent from its pose in 3D.</summary>
        public static BrowserWindowResize Resize(float width, float height, float deltaX, float deltaY, int cornerX, int cornerY) {
            if ((cornerX != -1 && cornerX != 1) || (cornerY != -1 && cornerY != 1)) throw new ArgumentOutOfRangeException(nameof(cornerX));
            if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsInfinity(width) || float.IsNaN(height) || float.IsInfinity(height)) throw new ArgumentOutOfRangeException(nameof(width));
            if (float.IsNaN(deltaX) || float.IsInfinity(deltaX) || float.IsNaN(deltaY) || float.IsInfinity(deltaY)) throw new ArgumentOutOfRangeException(nameof(deltaX));
            float w = Math.Max(800, Math.Min(1800, width + cornerX * deltaX));
            float h = Math.Max(600, Math.Min(1400, height + cornerY * deltaY));
            return new BrowserWindowResize(w, h, cornerX * (w - width) * .5f, cornerY * (h - height) * .5f);
        }
    }
}
