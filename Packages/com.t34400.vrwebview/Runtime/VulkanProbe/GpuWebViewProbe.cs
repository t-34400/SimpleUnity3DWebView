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
    /// <summary>Quest GPU experiment, not the production browser API.</summary>
    public class GpuWebViewProbe : MonoBehaviour
    {
        public int width = 1024, height = 768;
        public bool flipImageVertically = true;
        public ProbePointerMode pointerMode = ProbePointerMode.Auto;
        public bool restoreBrowserFocus = true;
        public Camera headCamera;
        private ulong id;
        private RenderTexture texture;
        private AndroidJavaClass bridge;
        private VulkanProbePump pump;
        private Text status;
        private RawImage image;
        private Text recoveryLabel;
        private RectTransform webRect;
        private Transform panel;
        private LineRenderer ray;
        private Material rayMaterial;
        private bool paused, wasPrimary, wasSecondary, touching;
        private bool hasPointer, placed, pointerTracked, pointerPressed;
        private ProbePointerSource pointerSource;
        private readonly ProbeRayInput input = new ProbeRayInput();
        private readonly PointerPressState press = new PointerPressState();
        private Vector2 lastPointer;
        private float nextStatus;
        private string failure;
        private string lastInputDetails;
        private int focusEvents, pauseEvents;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        protected void Start()
        {
            BuildPanel();
            Open();
        }
        private void Open()
        {
            if (!isActiveAndEnabled || paused || id != 0) return;
            failure = null;
            if (Application.platform != RuntimePlatform.Android) { failure = "Android arm64 Vulkan or OpenGL ES 3 device required. Editor preview has no native browser."; return; }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3) { failure = "Select Vulkan or OpenGL ES 3 in Android Graphics APIs."; return; }
            try
            {
                pump = VulkanProbePump.Ensure();
                width = Mathf.Clamp(width, 128, 2048); height = Mathf.Clamp(height, 128, 2048);
                var descriptor = new RenderTextureDescriptor(width, height) {
                    graphicsFormat = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0,
                    msaaSamples = 1, useMipMap = false, autoGenerateMips = false, volumeDepth = 1,
                    dimension = TextureDimension.Tex2D
                };
                texture = new RenderTexture(descriptor) { name = "VRWebView GPU target", filterMode = FilterMode.Bilinear };
                if (!texture.Create()) throw new InvalidOperationException("RenderTexture creation failed");
                id = VulkanProbeNative.VrCreate(width, height, texture.GetNativeTexturePtr());
                if (id == 0) { failure = VulkanProbeNative.Status(0); Destroy(texture); texture = null; return; }
                bridge = new AndroidJavaClass("com.t34400.vrwebview.VulkanProbe");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("open", activity, (long)id, width, height);
                bridge.CallStatic("setFocusRecovery", (long)id, restoreBrowserFocus);
                image.texture = texture;
            }
            catch (Exception error)
            {
                failure = "Probe creation failed: " + error.GetType().Name;
                Debug.LogError(failure, this);
                if (id != 0) { VulkanProbeNative.VrAbandon(id); pump.Retire(id, texture); id = 0; texture = null; }
                else if (texture != null) { Destroy(texture); texture = null; }
                bridge?.Dispose(); bridge = null;
            }
        }
        private void Close()
        {
            if (id != 0) TraceFocus("close host=" + id);
            ResetInput();
            if (id != 0)
            {
                try { bridge?.CallStatic("close", (long)id); }
                catch (Exception error) { Debug.LogError("Probe close failed: " + error.GetType().Name, this); }
                // Java stops the producer and acknowledges nativeStop on its UI thread.
                // If that acknowledgement fails, retain the target rather than free in-use GPU memory.
                pump.Retire(id, texture); id = 0; texture = null;
            }
            if (image != null) image.texture = null;
            bridge?.Dispose(); bridge = null;
        }
        public void Recreate() { Close(); placed = false; Open(); }
        public void Resize()
        {
            Close(); width = width == 1024 ? 800 : 1024; height = width == 1024 ? 768 : 600; Open();
        }
        public void RequestIme() { if (id != 0) bridge.CallStatic("requestIme", (long)id); }
        public void ArmFocusReturn() { if (id != 0) bridge.CallStatic("armFocusReturn", (long)id); }
        public void ToggleFocusRecovery()
        {
            restoreBrowserFocus = !restoreBrowserFocus;
            if (recoveryLabel != null) recoveryLabel.text = "Restore " + (restoreBrowserFocus ? "ON" : "OFF");
            if (id != 0) bridge.CallStatic("setFocusRecovery", (long)id, restoreBrowserFocus);
            TraceFocus("focusRecovery enabled=" + restoreBrowserFocus);
        }
        public void ReturnFocus() { CancelPointer(); if (id != 0) bridge.CallStatic("releaseFocus", (long)id); }
        public void Reload() { if (id != 0) bridge.CallStatic("reload", (long)id); }
        public void Flip()
        {
            CancelPointer(); flipImageVertically = !flipImageVertically;
            image.uvRect = new Rect(0, flipImageVertically ? 1 : 0, 1, flipImageVertically ? -1 : 1);
        }
        protected void OnApplicationPause(bool value)
        {
            pauseEvents++;
            TraceFocus("applicationPause=" + value);
            paused = value;
            if (value) Close(); else if (image != null) Open();
        }
        // Keyboard focus changes do not imply application suspension. Keep the WebView
        // and InputConnection alive when the system keyboard takes input focus.
        protected void OnApplicationFocus(bool value)
        {
            focusEvents++;
            TraceFocus("applicationFocus=" + value);
            if (!value) ResetInput();
        }
        protected void OnDisable() { InputSystem.onDeviceChange -= OnHandDeviceChange; Close(); }
        protected void OnEnable() { InputSystem.onDeviceChange += OnHandDeviceChange; if (image != null) Open(); }
        private void OnHandDeviceChange(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            if (device is MetaAimHand)
                TraceFocus("handDevice side=" + (device == MetaAimHand.right ? "R" : device == MetaAimHand.left ? "L" : "other")
                    + " id=" + device.deviceId + " change=" + change
                    + " enabled=" + device.enabled);
        }
        protected void OnDestroy()
        {
            Close();
            if (panel != null) Destroy(panel.gameObject);
            if (ray != null) Destroy(ray.gameObject);
            if (rayMaterial != null) Destroy(rayMaterial);
        }
        protected void Update()
        {
            UpdateHead();
            UpdateRay();
            if (status == null || Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + 0.5f;
            SubsystemManager.GetSubsystems(displays);
            bool xrRunning = displays.Exists(display => display.running);
            string native = failure ?? (id == 0 ? "Probe closed" : VulkanProbeNative.Status(id));
            string host = id == 0 || bridge == null ? "" : bridge.CallStatic<string>("status");
            string roots = input.RootDetails();
            string details = "source=" + pointerSource + " R " + input.RightHandDetails + " L " + input.LeftHandDetails + " " + roots;
            if (details != lastInputDetails) { TraceFocus(details); lastInputDetails = details; }
            status.text = native + "\n" + host + "\nXR running=" + xrRunning + "  Unity frames=" + Time.frameCount
                + "\ninput=HI3 source=" + pointerSource + " tracked=" + pointerTracked + " pressed=" + pointerPressed
                + " flipY=" + flipImageVertically + "\nhandAim R=" + input.RightHandPresent + "/" + input.RightHandTracked
                + " L=" + input.LeftHandPresent + "/" + input.LeftHandTracked + " controller=" + input.ControllerTracked
                + "\nappFocus=" + Application.isFocused + " paused=" + paused + " focus/pauseEvents=" + focusEvents + "/" + pauseEvents
                + "\nR " + input.RightHandDetails + "\n" + roots
                + "\nPinch: click/drag. Focus +5s: arm recovery before selecting a field.";
        }
        private void TraceFocus(string value)
        {
            Debug.Log("[VRWebViewFocus] unity frame=" + Time.frameCount + " " + value, this);
        }
        private void UpdateHead()
        {
            if (headCamera == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position)) headCamera.transform.localPosition = position;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation)) headCamera.transform.localRotation = rotation;
            if (!placed && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            {
                Quaternion yaw = Quaternion.Euler(0, headCamera.transform.eulerAngles.y, 0);
                panel.position = headCamera.transform.position + yaw * Vector3.forward * 1.6f;
                panel.rotation = yaw; placed = true;
            }
        }
        private void UpdateRay()
        {
            pointerTracked = input.TryRead(pointerMode, press.ActiveSourceId, out var sample);
            pointerSource = pointerTracked ? sample.Source : ProbePointerSource.None;
            pointerPressed = pointerTracked && sample.Pressed;
            var phase = press.Update(pointerTracked, (int)sample.Source, pointerPressed);
            if (phase == PointerPhase.Cancel) CancelPointer();
            if (!pointerTracked)
            {
                ray.enabled = false; wasPrimary = wasSecondary = false; return;
            }
            Vector3 origin = sample.Position;
            Quaternion rotation = sample.Rotation;
            if (headCamera.transform.parent != null)
            {
                origin = headCamera.transform.parent.TransformPoint(origin);
                rotation = headCamera.transform.parent.rotation * rotation;
            }
            Vector3 direction = rotation * Vector3.forward;
            ray.enabled = true; ray.SetPosition(0, origin);
            bool hit = Physics.Raycast(origin, direction, out RaycastHit result, 5f);
            ray.SetPosition(1, hit ? result.point : origin + direction * 3f);
            hasPointer = hit && result.collider.gameObject == webRect.gameObject;
            if (hasPointer)
            {
                Vector3 local = webRect.InverseTransformPoint(result.point);
                Rect rect = webRect.rect, uv = image.uvRect;
                hasPointer = BrowserCoordinates.TryNormalize(local.x, local.y, rect.xMin, rect.yMin,
                        rect.width, rect.height, out float x, out float y)
                    && BrowserCoordinates.TryMapTextureUv(x, y, uv.x, uv.y, uv.width, uv.height, out x, out y);
                // Raw hardware-Canvas buffer rows are top-first. The sampled UV is
                // the Android viewport coordinate; do not invert it a second time.
                if (hasPointer) lastPointer = new Vector2(x, y);
            }
            if (phase == PointerPhase.Down && hit)
            {
                var button = result.collider.GetComponent<VulkanProbeButton>();
                if (button != null) button.Press();
                else if (hasPointer && id != 0) { touching = true; SendPointer(0); }
            }
            else if (touching)
            {
                if (!hasPointer) CancelPointer();
                else if (phase == PointerPhase.Up) { SendPointer(1); touching = false; }
                else if (phase == PointerPhase.Move) SendPointer(2);
            }
            if (hasPointer && id != 0 && !touching && Mathf.Abs(sample.Scroll.y) > 0.2f)
                bridge.CallStatic("scroll", (long)id, -sample.Scroll.y);
            if (sample.RequestIme && !wasPrimary) RequestIme();
            if (sample.ReturnFocus && !wasSecondary) ReturnFocus();
            wasPrimary = sample.RequestIme; wasSecondary = sample.ReturnFocus;
        }
        private void ResetInput()
        {
            CancelPointer(); press.Reset(); wasPrimary = wasSecondary = false;
        }
        private void SendPointer(int action) { if (id != 0) bridge.CallStatic("pointer", (long)id, action, lastPointer.x, lastPointer.y); }
        private void CancelPointer() { if (touching) SendPointer(3); touching = false; }
        private void BuildPanel()
        {
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) { var cameraObject = new GameObject("Main Camera", typeof(Camera)); cameraObject.tag = "MainCamera"; headCamera = cameraObject.GetComponent<Camera>(); }
            headCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            var canvasObject = new GameObject("GPU probe panel", typeof(RectTransform), typeof(Canvas));
            panel = canvasObject.transform; panel.position = new Vector3(0, 1.45f, 1.6f); panel.localScale = Vector3.one * 0.0012f;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = headCamera;
            ((RectTransform)panel).sizeDelta = new Vector2(1024, 1100);
            var browser = new GameObject("GPU browser", typeof(RectTransform), typeof(RawImage), typeof(BoxCollider));
            browser.transform.SetParent(panel, false); webRect = browser.GetComponent<RectTransform>(); webRect.sizeDelta = new Vector2(1024, 768);
            browser.GetComponent<BoxCollider>().size = new Vector3(1024, 768, 1);
            image = browser.GetComponent<RawImage>(); image.color = Color.white;
            image.uvRect = new Rect(0, flipImageVertically ? 1 : 0, 1, flipImageVertically ? -1 : 1);
            status = Label("Status", new Vector2(0, 620), new Vector2(1024, 460), "Waiting for GPU probe", 16);
            Button("IME", -420, RequestIme); Button("Focus", -252, ReturnFocus); Button("Reload", -84, Reload);
            Button("Recreate", 84, Recreate); Button("Resize", 252, Resize); Button("Flip Y", 420, Flip);
            Button("Focus +5s", 0, ArmFocusReturn, -510, 320);
            recoveryLabel = Button("Restore " + (restoreBrowserFocus ? "ON" : "OFF"), -350, ToggleFocusRecovery, -510, 300);
            var rayObject = new GameObject("Browser input ray", typeof(LineRenderer)); ray = rayObject.GetComponent<LineRenderer>();
            ray.positionCount = 2; ray.startWidth = ray.endWidth = 0.003f; ray.useWorldSpace = true;
            var rayShader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (rayShader != null) { rayMaterial = new Material(rayShader); ray.material = rayMaterial; }
            ray.startColor = ray.endColor = Color.cyan; ray.enabled = false;
        }
        private Text Label(string name, Vector2 position, Vector2 size, string value, int fontSize)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Text)); owner.transform.SetParent(panel, false);
            var rect = owner.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = owner.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.color = Color.white; text.text = value; text.raycastTarget = false;
            return text;
        }
        private Text Button(string name, float x, Action action, float y = -434, float width = 156)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(VulkanProbeButton));
            owner.transform.SetParent(panel, false);
            var rect = owner.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, 60);
            owner.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.4f, 1);
            owner.GetComponent<BoxCollider>().size = new Vector3(width, 60, 2);
            owner.GetComponent<VulkanProbeButton>().Action = action;
            var label = Label(name + " label", new Vector2(x, y), new Vector2(width, 60), name, 24);
            label.alignment = TextAnchor.MiddleCenter;
            return label;
        }
    }
}
