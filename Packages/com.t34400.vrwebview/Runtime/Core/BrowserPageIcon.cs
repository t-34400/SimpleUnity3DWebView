using System;
namespace T34400.VRWebView
{
    /// <summary>Small PNG received by the page engine, without Unity textures or cache policy.</summary>
    public sealed class BrowserPageIcon
    {
        public const int MaximumBytes = 32768, MaximumDimension = 64;
        private readonly byte[] png;
        public string Address { get; }
        public bool HasImage => png.Length != 0;
        public byte[] CopyPng() => (byte[])png.Clone();
        public BrowserPageIcon(string address, byte[] data)
        {
            Address = NavigationPolicy.Parse(address).AbsoluteUri;
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length != 0 && !IsSmallPng(data)) throw new ArgumentException("Expected a PNG up to 64 pixels and 32 KiB", nameof(data));
            png = (byte[])data.Clone();
        }
        public static bool IsSmallPng(byte[] value)
        {
            if (value == null || value.Length < 33 || value.Length > MaximumBytes) return false;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < signature.Length; i++) if (value[i] != signature[i]) return false;
            if (value[8] != 0 || value[9] != 0 || value[10] != 0 || value[11] != 13
                || value[12] != 'I' || value[13] != 'H' || value[14] != 'D' || value[15] != 'R') return false;
            return Dimension(value, 16) && Dimension(value, 20);
        }
        private static bool Dimension(byte[] value, int at) => value[at] == 0 && value[at + 1] == 0 && value[at + 2] == 0
            && value[at + 3] > 0 && value[at + 3] <= MaximumDimension;
    }
    public interface IBrowserIconSource
    {
        BrowserPageIcon PageIcon { get; }
        event Action<BrowserPageIcon> PageIconChanged;
    }
}
