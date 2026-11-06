namespace T34400.VRWebView
{
    public static class BrowserCoordinates
    {
        public static bool TryNormalize(float localX, float localY, float xMin, float yMin,
            float width, float height, out float x, out float y)
        {
            x = y = 0;
            if (!(width > 0) || !(height > 0)) return false;
            x = (localX - xMin) / width;
            y = 1f - (localY - yMin) / height;
            return !float.IsNaN(x) && !float.IsNaN(y) && !float.IsInfinity(x) && !float.IsInfinity(y);
        }

        /// <summary>Map a top-left normalized UI point through the displayed texture UV rectangle.</summary>
        public static bool TryMapTextureUv(float x, float y, float uvX, float uvY,
            float uvWidth, float uvHeight, out float u, out float v)
        {
            u = uvX + x * uvWidth;
            v = uvY + (1f - y) * uvHeight;
            return !float.IsNaN(u) && !float.IsNaN(v) && !float.IsInfinity(u) && !float.IsInfinity(v);
        }
    }
}
