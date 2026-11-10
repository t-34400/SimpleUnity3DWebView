using System;
namespace T34400.VRWebView.Samples.Browser
{
    public readonly struct BrowserPopupRect {
        public readonly float X, Y, Width, Height;
        public BrowserPopupRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    }
    /// <summary>Sample popup placement in top-left window coordinates; no UI dependency.</summary>
    public static class BrowserPopupGeometry {
        public static BrowserPopupRect Place(float windowWidth, float windowHeight, float anchorX, float anchorY, float width, float height, bool alignRight) {
            foreach (float value in new[] { windowWidth, windowHeight, anchorX, anchorY, width, height })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Finite popup geometry required");
            if (windowWidth < 32 || windowHeight < 32 || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            const float margin = 8, gap = 6;
            width = Math.Min(width, windowWidth - margin * 2); height = Math.Min(height, windowHeight - margin * 2);
            float x = alignRight ? anchorX - width : anchorX;
            float below = Math.Max(0, windowHeight - margin - anchorY - gap), above = Math.Max(0, anchorY - gap - margin);
            bool upwards = height > below && above > below;
            height = Math.Min(height, Math.Max(1, upwards ? above : below));
            float y = upwards ? anchorY - gap - height : anchorY + gap;
            x = Math.Max(margin, Math.Min(windowWidth - margin - width, x));
            y = Math.Max(margin, Math.Min(windowHeight - margin - height, y));
            return new BrowserPopupRect(x, y, width, height);
        }
    }
}
