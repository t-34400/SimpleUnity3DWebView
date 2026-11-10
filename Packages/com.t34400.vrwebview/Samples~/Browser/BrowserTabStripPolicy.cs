using System;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Tested against the full strip width, including space occupied by a docked +.</summary>
    public static class BrowserTabStripPolicy
    {
        public static bool ShouldDock(int count, float available, float tabWidth = 220, float spacing = 8, float addWidth = 52)
        {
            if (count < 0 || float.IsNaN(available) || float.IsInfinity(available) || available < 0) throw new ArgumentOutOfRangeException(nameof(available));
            return count * (tabWidth + spacing) + addWidth > available + .5f;
        }
    }
}
