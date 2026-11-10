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
        private readonly List<(ulong id, RenderTexture texture, bool diagnostics)> retired = new List<(ulong, RenderTexture, bool)>();
        private readonly BrowserMipCommandPlan<RenderTexture> mipTargets = new BrowserMipCommandPlan<RenderTexture>();
        internal int MipTargetCount => mipTargets.Count;
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
        private Action copyCommand;
        private Action<RenderTexture> mipCommand;
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
                instance.copyCommand = instance.RecordCopy;
                instance.mipCommand = instance.RecordMips;
            }
            return instance;
        }
        internal void RegisterMipTarget(ulong id, RenderTexture texture) {
            if (texture != null && texture.useMipMap) mipTargets.Add(id, texture);
        }
        internal void Retire(ulong id, RenderTexture texture, bool diagnostics = false) {
            mipTargets.Remove(id); // Withdraw before another frame can record work for this generation.
            retired.Add((id, texture, diagnostics));
        }
        private void RecordCopy() { renderEvents.IssuePluginEventAndData(callback, eventId, IntPtr.Zero); }
        private void RecordMips(RenderTexture target) { renderEvents.GenerateMips(target); }
        private void Update()
        {
            Notify(Tick);
            if (Application.platform != RuntimePlatform.Android || renderEvents == null) return;
            renderEvents.Clear(); // Never replay commands referencing a retired generation.
            mipTargets.Record(copyCommand, mipCommand);
            Graphics.ExecuteCommandBuffer(renderEvents);
            for (int i = retired.Count - 1; i >= 0; --i)
            {
                if (BrowserGpuNative.VrAlive(retired[i].id) != 0) continue;
                if (retired[i].texture != null) Destroy(retired[i].texture);
                if (retired[i].diagnostics) Debug.Log("[VRWebViewRuntime] event=target_retired host=" + retired[i].id + " nativeAlive=0 unityDestroyRequested=true");
                retired.RemoveAt(i);
            }
        }
        private void OnDestroy()
        {
            renderEvents?.Release();
            renderEvents = null;
            mipTargets.Clear();
            if (instance == this) instance = null;
        }
    }
}
