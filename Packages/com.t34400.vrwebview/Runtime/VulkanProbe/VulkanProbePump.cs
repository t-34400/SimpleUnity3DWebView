using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace T34400.VRWebView
{
    // Keeps issuing render events after the visible component closes, until the GPU
    // releases all generations. Texture destruction never races a queued plugin event.
    public sealed class VulkanProbePump : MonoBehaviour
    {
        private static VulkanProbePump instance;
        private readonly List<(ulong id, RenderTexture texture)> retired = new List<(ulong, RenderTexture)>();
        private IntPtr callback;
        private int eventId;
        private CommandBuffer renderEvents;
        internal static VulkanProbePump Ensure()
        {
            if (instance != null) return instance;
            var owner = new GameObject("VRWebView Vulkan render pump");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<VulkanProbePump>();
            instance.callback = VulkanProbeNative.VrGetRenderEvent();
            instance.eventId = VulkanProbeNative.VrGetEventId();
            if (instance.callback != IntPtr.Zero)
            {
                instance.renderEvents = new CommandBuffer { name = "VRWebView Vulkan render event" };
                instance.renderEvents.IssuePluginEventAndData(instance.callback, instance.eventId, IntPtr.Zero);
            }
            return instance;
        }
        internal void Retire(ulong id, RenderTexture texture) { retired.Add((id, texture)); }
        private void Update()
        {
            if (Application.platform != RuntimePlatform.Android || renderEvents == null) return;
            Graphics.ExecuteCommandBuffer(renderEvents);
            for (int i = retired.Count - 1; i >= 0; --i)
            {
                if (VulkanProbeNative.VrAlive(retired[i].id) != 0) continue;
                if (retired[i].texture != null) Destroy(retired[i].texture);
                retired.RemoveAt(i);
            }
        }
        private void OnDestroy()
        {
            renderEvents?.Release();
            renderEvents = null;
            if (instance == this) instance = null;
        }
    }
}
