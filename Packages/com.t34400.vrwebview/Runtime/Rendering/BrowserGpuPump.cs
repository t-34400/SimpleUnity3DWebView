using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace T34400.VRWebView
{
    // Keeps issuing render events after the visible component closes, until the GPU
    // releases all generations. Texture destruction never races a queued plugin event.
    public sealed class BrowserGpuPump : MonoBehaviour
    {
        private static BrowserGpuPump instance;
        private readonly List<(ulong id, RenderTexture texture)> retired = new List<(ulong, RenderTexture)>();
        internal event Action Tick;
        internal event Action<bool> FocusChanged;
        internal event Action<bool> PauseChanged;
        private static void Notify(Action actions) {
            if (actions == null) return;
            foreach (Action action in actions.GetInvocationList())
                try { action(); } catch (Exception error) { Debug.LogException(error); }
        }
        private static void Notify(Action<bool> actions, bool value) {
            if (actions == null) return;
            foreach (Action<bool> action in actions.GetInvocationList())
                try { action(value); } catch (Exception error) { Debug.LogException(error); }
        }
        private void OnApplicationFocus(bool value) { Notify(FocusChanged, value); }
        private void OnApplicationPause(bool value) { Notify(PauseChanged, value); }
        private IntPtr callback;
        private int eventId;
        private CommandBuffer renderEvents;
        internal static BrowserGpuPump Ensure()
        {
            if (instance != null) return instance;
            var owner = new GameObject("VRWebView GPU render pump");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<BrowserGpuPump>();
            instance.callback = BrowserGpuNative.VrGetRenderEvent();
            instance.eventId = BrowserGpuNative.VrGetEventId();
            if (instance.callback != IntPtr.Zero)
            {
                instance.renderEvents = new CommandBuffer { name = "VRWebView GPU render event" };
                instance.renderEvents.IssuePluginEventAndData(instance.callback, instance.eventId, IntPtr.Zero);
            }
            return instance;
        }
        internal void Retire(ulong id, RenderTexture texture) { retired.Add((id, texture)); }
        private void Update()
        {
            Notify(Tick);
            if (Application.platform != RuntimePlatform.Android || renderEvents == null) return;
            Graphics.ExecuteCommandBuffer(renderEvents);
            for (int i = retired.Count - 1; i >= 0; --i)
            {
                if (BrowserGpuNative.VrAlive(retired[i].id) != 0) continue;
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
