using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using InputSystem = UnityEngine.InputSystem.InputSystem;

namespace T34400.VRWebView
{
    /// <summary>AG1 experimental separate-Activity GPU producer, not a production browser API.</summary>
    public sealed class ActivityGpuWebViewProbe : MonoBehaviour
    {
        public Camera headCamera;
        public int width = 1024, height = 768;
        public bool useOwnedActivityJointRay = true;
        private AndroidJavaClass bridge;
        private AndroidJavaObject unityOwner;
        private ulong id;
        private RenderTexture texture;
        private VulkanProbePump pump;
        private RawImage image;
        private RectTransform webRect;
        private Transform panel, spinner;
        private Text status;
        private LineRenderer ray;
        private Material rayMaterial;
        private readonly ProbeRayInput input = new ProbeRayInput();
        private readonly PointerPressState press = new PointerPressState();
        private JointProbeRay joints;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private XRDisplaySubsystem observedDisplay;
        private bool? displayFocus;
        private bool? EffectiveFocus => ActivityProbeInputPolicy.ResolveFocus(ActivityProbeFocusFeature.SeenInstance, ActivityProbeFocusFeature.SessionFocus, displayFocus);
        private bool paused, placed, touching, sourceVisible, pointerTracked, pointerBlocked;
        private int stateBits, lastRenderFrame = -1;
        private long renders;
        private Vector2 lastPoint;
        private string mode = "texture", failure, pointerSource = "none";
        private float startAt, nextLog;
        private bool started;
        private void Trace(string s) { Debug.Log("[VRWebViewActivityGPU] AG1 frame=" + Time.frameCount + " " + s, this); }
        private void Start()
        {
            BuildPanel(); started = true; startAt = Time.unscaledTime + 3;
            if (Application.platform != RuntimePlatform.Android) { failure = "Android player required"; return; }
            bridge = new AndroidJavaClass("com.t34400.vrwebview.ActivityGpuProbe");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) unityOwner = player.GetStatic<AndroidJavaObject>("currentActivity");
            mode = bridge.CallStatic<string>("startupMode", unityOwner); sourceVisible = mode == "native";
            Trace("start revision=AG2 mode=" + mode + " graphics=" + SystemInfo.graphicsDeviceType);
        }
        private void Open()
        {
            if (id != 0 || bridge == null || paused) return;
            failure = null;
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3) { failure = "Select Vulkan or OpenGL ES 3"; return; }
            try {
                width = Mathf.Clamp(width, 128, 2048); height = Mathf.Clamp(height, 128, 2048); pump = VulkanProbePump.Ensure();
                texture = new RenderTexture(new RenderTextureDescriptor(width, height) {
                    graphicsFormat = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm,
                    depthBufferBits = 0, msaaSamples = 1, useMipMap = false, autoGenerateMips = false, volumeDepth = 1, dimension = TextureDimension.Tex2D
                }) { name = "AG1 Unity GPU target", filterMode = FilterMode.Bilinear };
                if (!texture.Create()) throw new InvalidOperationException("Texture creation failed");
                id = VulkanProbeNative.VrCreate(width, height, texture.GetNativeTexturePtr());
                if (id == 0) throw new InvalidOperationException(VulkanProbeNative.Status(0));
                bridge.CallStatic("open", unityOwner, (long)id, width, height, mode);
                image.texture = texture; Trace("opened id=" + id + " mode=" + mode);
            } catch (Exception error) {
                failure = "AG1 open failed: " + error.GetType().Name;
                try { failure += "; " + VulkanProbeNative.Status(0); } catch (Exception) { }
                Trace(failure); Close();
            }
        }
        private void Close()
        {
            CancelTouch(); press.Reset(); joints?.Reset();
            if (id != 0) {
                try { bridge?.CallStatic("close", (long)id); }
                catch (Exception error) { Trace("close failed=" + error.GetType().Name); }
                pump.Retire(id, texture); id = 0; texture = null;
            } else if (texture != null) { Destroy(texture); texture = null; }
            if (image != null) image.texture = null;
        }
        public void Reopen() { Close(); sourceVisible = mode == "native"; Open(); }
        public void Resize() { Close(); width = width == 1024 ? 800 : 1024; height = width == 1024 ? 768 : 600; Open(); }
        public void ToggleSource() { sourceVisible = !sourceVisible; if (id != 0) bridge.CallStatic("source", (long)id, sourceVisible); }
        private void Update()
        {
            UpdateHead(); RefreshDisplay();
            bool running = observedDisplay != null && observedDisplay.running;
            if (started && bridge != null && startAt > 0 && placed && running && Application.isFocused && !paused && Time.unscaledTime >= startAt) { startAt = 0; Open(); }
            if (id != 0 && bridge.CallStatic<int>("phase", (long)id) == -1) { Trace("host finished"); Close(); }
            stateBits = id == 0 || bridge == null ? 0 : bridge.CallStatic<int>("inputState", (long)id);
            if ((stateBits & (int)ActivityProbeState.Alive) != 0) sourceVisible = (stateBits & (int)ActivityProbeState.TextureOnly) == 0;
            UpdatePointer(running);
            if (spinner != null) spinner.localRotation = Quaternion.Euler(0, Time.unscaledTime * 70f, Time.unscaledTime * 45f);
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 1; joints?.Refresh();
            bool? effective = EffectiveFocus;
            string focus = "app=" + Application.isFocused + ",XR=" + (effective.HasValue ? effective.ToString() : "unknown")
                + ",focusSource=" + (ActivityProbeFocusFeature.SeenInstance ? "OpenXRSession" : "DisplayEvent") + ",xrState=" + ActivityProbeFocusFeature.SessionState;
            if (id != 0) bridge.CallStatic("pulse", (long)id, (long)Time.frameCount, renders, focus);
            string native = id == 0 ? "GPU host closed; use Reopen" : VulkanProbeNative.Status(id);
            string host = id == 0 ? "" : bridge.CallStatic<string>("status", (long)id);
            string details = "renders=" + renders + " " + focus + " paused=" + paused + " running=" + running
                + " bits=" + stateBits + " pointer=" + pointerSource + " tracked=" + pointerTracked + " blocked=" + pointerBlocked + " " + joints.Diagnostics;
            Trace(details + " " + native.Replace('\n', ' '));
            if (status != null) status.text = "AG1 UNITY TEXTURE (not the native source page)\n" + (failure ?? native) + "\n" + host + "\n" + details
                + "\nUNITY RAY = MAGENTA. Native toolbar: Browse ends editing; auto return: 120s.";
        }
        private void RefreshDisplay()
        {
            SubsystemManager.GetSubsystems(displays);
            var active = displays.Find(d => d.running);
            if (active == observedDisplay) return;
            if (observedDisplay != null) observedDisplay.displayFocusChanged -= DisplayFocus;
            observedDisplay = active; displayFocus = null;
            if (observedDisplay != null) observedDisplay.displayFocusChanged += DisplayFocus;
        }
        private void DisplayFocus(bool focused) { displayFocus = focused; Trace("XR displayFocus=" + focused); CancelTouch(); press.Reset(); joints?.Reset(); }
        private void UpdateHead()
        {
            if (headCamera == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p)) headCamera.transform.localPosition = p;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion q)) headCamera.transform.localRotation = q;
            if (!placed && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked) {
                var yaw = Quaternion.Euler(0, headCamera.transform.eulerAngles.y, 0);
                panel.position = headCamera.transform.position + yaw * Vector3.forward * 1.7f; panel.rotation = yaw; placed = true;
            }
        }
        private void UpdatePointer(bool running)
        {
            if (ray == null) return;
            bool editing = (stateBits & ((int)ActivityProbeState.Editing | (int)ActivityProbeState.Ime)) != 0;
            bool? effective = EffectiveFocus;
            bool owned = useOwnedActivityJointRay && ActivityProbeInputPolicy.CanUseOwnedHand(paused, running, effective, stateBits);
            pointerBlocked = paused || editing || effective == false || (!Application.isFocused && !owned);
            ProbeRaySample sample = default;
            pointerTracked = !pointerBlocked && Application.isFocused && input.TryRead(ProbePointerMode.Auto, press.ActiveSourceId, out sample);
            if (!pointerTracked && !pointerBlocked && owned) pointerTracked = joints.TryRead(headCamera.transform, press.ActiveSourceId, out sample);
            pointerSource = pointerTracked ? sample.Source.ToString() : "none";
            var phase = press.Update(pointerTracked, (int)sample.Source, sample.Pressed);
            if (phase == PointerPhase.Cancel || !pointerTracked) CancelTouch();
            if (!pointerTracked) { ray.enabled = false; return; }
            Vector3 origin = sample.Position; Quaternion rotation = sample.Rotation;
            if (headCamera.transform.parent != null) { origin = headCamera.transform.parent.TransformPoint(origin); rotation = headCamera.transform.parent.rotation * rotation; }
            bool hit = Physics.Raycast(origin, rotation * Vector3.forward, out RaycastHit result, 5f);
            ray.enabled = true; ray.SetPosition(0, origin); ray.SetPosition(1, hit ? result.point : origin + rotation * Vector3.forward * 3f);
            bool onPage = hit && result.collider.gameObject == webRect.gameObject;
            if (onPage) {
                Vector3 point = webRect.InverseTransformPoint(result.point); Rect rect = webRect.rect, uv = image.uvRect;
                onPage = BrowserCoordinates.TryNormalize(point.x, point.y, rect.xMin, rect.yMin, rect.width, rect.height, out float x, out float y)
                    && BrowserCoordinates.TryMapTextureUv(x, y, uv.x, uv.y, uv.width, uv.height, out x, out y);
                if (onPage) lastPoint = new Vector2(x, y);
            }
            if (phase == PointerPhase.Down && hit) {
                var button = result.collider.GetComponent<VulkanProbeButton>();
                if (button != null) { Trace("button=" + button.name + " source=" + sample.Source); button.Press(); }
                else if (onPage && id != 0) { touching = true; Send(0); }
            } else if (touching) {
                if (!onPage) CancelTouch();
                else if (phase == PointerPhase.Up) { Send(1); touching = false; }
                else if (phase == PointerPhase.Move) Send(2);
            }
        }
        private void Send(int action) { if (id != 0) bridge.CallStatic("pointer", (long)id, action, lastPoint.x, lastPoint.y); }
        private void CancelTouch() { if (touching) Send(3); touching = false; }
        private void OnApplicationPause(bool value) { paused = value; Trace("applicationPause=" + value); if (value) { CancelTouch(); press.Reset(); joints?.Reset(); } }
        private void OnApplicationFocus(bool value) { Trace("applicationFocus=" + value); if (!value) { CancelTouch(); press.Reset(); joints?.Reset(); } }
        private void DeviceChanged(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            if (!(device is MetaAimHand hand)) return;
            Trace("handDevice id=" + device.deviceId + " change=" + change);
            if (Application.platform == RuntimePlatform.Android && (change == UnityEngine.InputSystem.InputDeviceChange.SoftReset || change == UnityEngine.InputSystem.InputDeviceChange.HardReset)) {
                CancelTouch(); press.Reset(); joints?.Reset(); ProbeRayInput.SynchronizeHandReset(hand); Trace("handResetSync id=" + device.deviceId);
            }
        }
        private void CameraRendered(Camera camera) { if (camera == headCamera && lastRenderFrame != Time.frameCount) { lastRenderFrame = Time.frameCount; renders++; } }
        private void PipelineRendered(ScriptableRenderContext context, Camera camera) { CameraRendered(camera); }
        private void OnEnable() { joints = new JointProbeRay(); joints.Refresh(); InputSystem.onDeviceChange += DeviceChanged; Camera.onPostRender += CameraRendered; RenderPipelineManager.endCameraRendering += PipelineRendered; if (started && panel != null) startAt = Time.unscaledTime + 3; }
        private void OnDisable() {
            InputSystem.onDeviceChange -= DeviceChanged; Camera.onPostRender -= CameraRendered; RenderPipelineManager.endCameraRendering -= PipelineRendered;
            if (observedDisplay != null) observedDisplay.displayFocusChanged -= DisplayFocus; observedDisplay = null; displayFocus = null;
            Close(); joints?.Dispose(); joints = null; if (ray != null) ray.enabled = false;
        }
        private void OnDestroy() { Close(); bridge?.Dispose(); unityOwner?.Dispose(); if (panel != null) Destroy(panel.gameObject); if (ray != null) Destroy(ray.gameObject); if (rayMaterial != null) Destroy(rayMaterial); }
        private void BuildPanel()
        {
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) { var cameraObject = new GameObject("Main Camera", typeof(Camera)); cameraObject.tag = "MainCamera"; headCamera = cameraObject.GetComponent<Camera>(); }
            headCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            var root = new GameObject("AG1 Unity texture panel", typeof(RectTransform), typeof(Canvas)); panel = root.transform;
            panel.localScale = Vector3.one * 0.0012f; panel.position = new Vector3(0, 1.4f, 1.7f);
            root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; root.GetComponent<Canvas>().worldCamera = headCamera;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1080, 1300);
            var page = new GameObject("UNITY GPU texture", typeof(RectTransform), typeof(RawImage), typeof(BoxCollider)); page.transform.SetParent(panel, false);
            webRect = page.GetComponent<RectTransform>(); webRect.sizeDelta = new Vector2(1024, 768); page.GetComponent<BoxCollider>().size = new Vector3(1024, 768, 1);
            image = page.GetComponent<RawImage>(); image.uvRect = new Rect(0, 1, 1, -1); image.color = Color.white;
            status = Label("Diagnostics", 600, new Vector2(1100, 400), "AG1 preparing", 18);
            Button("Reopen", -400, Reopen); Button("Resize", -200, Resize); Button("Source ON/OFF", 0, ToggleSource);
            Button("Edit field", 200, () => { if (id != 0) bridge.CallStatic("requestIme", (long)id); }); Button("Close host", 400, Close);
            Button("Browse", 0, () => { if (id != 0) bridge.CallStatic("browse", (long)id); }, -535);
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube); marker.transform.SetParent(panel, false); marker.transform.localPosition = new Vector3(0, 850, 0); marker.transform.localScale = Vector3.one * 70;
            Destroy(marker.GetComponent<Collider>()); spinner = marker.transform;
            var line = new GameObject("AG1 Unity ray", typeof(LineRenderer)); ray = line.GetComponent<LineRenderer>(); ray.positionCount = 2; ray.useWorldSpace = true; ray.startWidth = ray.endWidth = 0.003f;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default"); if (shader != null) { rayMaterial = new Material(shader); ray.material = rayMaterial; }
            ray.startColor = ray.endColor = Color.magenta; ray.enabled = false;
        }
        private Text Label(string name, float y, Vector2 size, string value, int font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(panel, false); go.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, y); go.GetComponent<RectTransform>().sizeDelta = size;
            var t = go.GetComponent<Text>(); t.text = value; t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize = font; t.color = Color.white; t.raycastTarget = false; return t;
        }
        private void Button(string name, float x, Action action, float y = -450)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(VulkanProbeButton)); go.transform.SetParent(panel, false);
            go.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y); go.GetComponent<RectTransform>().sizeDelta = new Vector2(190, 70);
            go.GetComponent<BoxCollider>().size = new Vector3(190, 70, 1); go.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.4f); go.GetComponent<VulkanProbeButton>().Action = action;
            var t = Label(name + " label", y, new Vector2(190, 70), name, 20); t.rectTransform.anchoredPosition = new Vector2(x, y); t.alignment = TextAnchor.MiddleCenter;
        }
    }
}
