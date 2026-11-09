using System;
using System.Runtime.InteropServices;
using System.Text;

namespace T34400.VRWebView
{
    internal static class BrowserGpuNative
    {
        private const string Library = "GfxPluginVRWebView";
        [DllImport(Library)] internal static extern IntPtr VrGetRenderEvent();
        [DllImport(Library)] internal static extern int VrGetEventId();
        [DllImport(Library)] internal static extern ulong VrCreate(int width, int height, IntPtr target);
        [DllImport(Library)] internal static extern int VrAlive(ulong id);
        [DllImport(Library)] internal static extern int VrFailed(ulong id);
        [DllImport(Library)] internal static extern void VrAbandon(ulong id);
        [DllImport(Library)] private static extern int VrStatus(ulong id, [Out] byte[] output, int capacity);
        internal static string Status(ulong id)
        {
            var bytes = new byte[2048];
            int count = VrStatus(id, bytes, bytes.Length);
            return Encoding.UTF8.GetString(bytes, 0, count);
        }
    }
}
