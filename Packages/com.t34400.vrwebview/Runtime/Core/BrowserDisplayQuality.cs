using System;
namespace T34400.VRWebView
{
    public enum BrowserDisplayQuality { Standard = 0, ReduceShimmer = 1, ImproveObliqueViews = 2 }
    /// <summary>Output sampling only; page layout, text zoom and input coordinates stay independent.</summary>
    public static class BrowserDisplayQualityPolicy
    {
        public static void Validate(BrowserDisplayQuality value) {
            if (value < BrowserDisplayQuality.Standard || value > BrowserDisplayQuality.ImproveObliqueViews)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        public static BrowserDisplayQuality FromSavedValue(int value) => value >= 0 && value <= 2 ? (BrowserDisplayQuality)value : BrowserDisplayQuality.Standard;
        public static bool UsesMipmaps(BrowserDisplayQuality value) { Validate(value); return value != BrowserDisplayQuality.Standard; }
        public static int Anisotropy(BrowserDisplayQuality value) { Validate(value); return value == BrowserDisplayQuality.ImproveObliqueViews ? 4 : 1; }
    }
    /// <summary>Optional owner-thread output capability. Selecting a mode does not initialize hidden pages.</summary>
    public interface IBrowserDisplayQualityBackend
    {
        BrowserDisplayQuality DisplayQuality { get; }
        void SetDisplayQuality(BrowserDisplayQuality value);
    }
}
