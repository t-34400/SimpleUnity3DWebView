using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

namespace T34400.VRWebView
{
    /// <summary>Quest/Vulkan experiment, not the production browser API.</summary>
    public sealed class VulkanWebViewProbe : MonoBehaviour
    {
        public int width = 1024, height = 768;
        public bool flipImageVertically;
        public Camera headCamera;
        private ulong id;
        private RenderTexture texture;
        private AndroidJavaClass bridge;
        private VulkanProbePump pump;
        private Text status;
        private RawImage image;
        private RectTransform webRect;
        private Transform panel;
        private LineRenderer ray;
        private Material rayMaterial;
        private bool paused, wasTrigger, wasPrimary, wasSecondary, touching;
        private bool hasPointer, placed, controllerTracked;
        private Vector2 lastPointer;
        private float nextStatus;
        private string failure;
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        private void Start()
        {
            BuildPanel();
            Open();
        }
        private void Open()
        {
            if (!isActiveAndEnabled || paused || id != 0) return;
            failure = null;
            if (Application.platform != RuntimePlatform.Android) { failure = "Android arm64 Vulkan device required. Editor preview has no native browser."; return; }
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan) { failure = "Select Vulkan in Android Graphics APIs."; return; }
            try
            {
                pump = VulkanProbePump.Ensure();
                width = Mathf.Clamp(width, 128, 2048); height = Mathf.Clamp(height, 128, 2048);
                var descriptor = new RenderTextureDescriptor(width, height) {
                    graphicsFormat = QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, depthBufferBits = 0,
                    msaaSamples = 1, useMipMap = false, autoGenerateMips = false, volumeDepth = 1,
                    dimension = TextureDimension.Tex2D
                };
                texture = new RenderTexture(descriptor) { name = "VRWebView Vulkan target", filterMode = FilterMode.Bilinear };
                if (!texture.Create()) throw new InvalidOperationException("RenderTexture creation failed");
                id = VulkanProbeNative.VrCreate(width, height, texture.GetNativeTexturePtr());
                if (id == 0) { failure = VulkanProbeNative.Status(0); Destroy(texture); texture = null; return; }
                bridge = new AndroidJavaClass("com.t34400.vrwebview.VulkanProbe");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("open", activity, (long)id, width, height);
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
            CancelPointer();
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
        public void ReturnFocus() { CancelPointer(); if (id != 0) bridge.CallStatic("releaseFocus", (long)id); }
        public void Reload() { if (id != 0) bridge.CallStatic("reload", (long)id); }
        public void Flip()
        {
            CancelPointer(); flipImageVertically = !flipImageVertically;
            image.uvRect = new Rect(0, flipImageVertically ? 1 : 0, 1, flipImageVertically ? -1 : 1);
        }
        private void OnApplicationPause(bool value)
        {
            paused = value;
            if (value) Close(); else if (image != null) Open();
        }
        // Keyboard focus changes do not imply application suspension. Keep the WebView
        // and InputConnection alive when the system keyboard takes input focus.
        private void OnApplicationFocus(bool value) { if (!value) CancelPointer(); }
        private void OnDisable() { Close(); }
        private void OnEnable() { if (image != null) Open(); }
        private void OnDestroy()
        {
            Close();
            if (panel != null) Destroy(panel.gameObject);
            if (ray != null) Destroy(ray.gameObject);
            if (rayMaterial != null) Destroy(rayMaterial);
        }
        private void Update()
        {
            UpdateHead();
            UpdateRay();
            if (status == null || Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + 0.5f;
            SubsystemManager.GetSubsystems(displays);
            bool xrRunning = displays.Exists(display => display.running);
            string native = failure ?? (id == 0 ? "Probe closed" : VulkanProbeNative.Status(id));
            string host = id == 0 || bridge == null ? "" : bridge.CallStatic<string>("status");
            status.text = native + "\n" + host + "\nXR running=" + xrRunning + "  Unity frames=" + Time.frameCount
                + "  rightTracked=" + controllerTracked + "  flipY=" + flipImageVertically + "\nRight trigger: point/drag   Stick: scroll   A: IME   B: Unity focus";
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
            var hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool tracked = hand.isValid && hand.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
            controllerTracked = tracked;
            bool trigger = false, primary = false, secondary = false;
            if (tracked)
            {
                hand.TryGetFeatureValue(CommonUsages.triggerButton, out trigger);
                hand.TryGetFeatureValue(CommonUsages.primaryButton, out primary);
                hand.TryGetFeatureValue(CommonUsages.secondaryButton, out secondary);
            }
            if (!tracked || !hand.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 origin)
                    || !hand.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                CancelPointer(); ray.enabled = false; wasTrigger = false; wasPrimary = false; wasSecondary = false; return;
            }
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
                lastPointer = new Vector2(local.x / webRect.rect.width + 0.5f, 0.5f - local.y / webRect.rect.height);
                if (flipImageVertically) lastPointer.y = 1f - lastPointer.y;
            }
            if (trigger && !wasTrigger && hit)
            {
                var button = result.collider.GetComponent<VulkanProbeButton>();
                if (button != null) button.Press();
                else if (hasPointer && id != 0) { touching = true; SendPointer(0); }
            }
            else if (touching)
            {
                // Cancel a captured drag on ray loss; never strand a native pressed pointer.
                if (!hasPointer) CancelPointer();
                else if (!trigger) { SendPointer(1); touching = false; }
                else SendPointer(2);
            }
            if (hasPointer && id != 0 && !touching && hand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis)
                    && Mathf.Abs(axis.y) > 0.2f) bridge.CallStatic("scroll", (long)id, -axis.y);
            if (primary && !wasPrimary) RequestIme();
            if (secondary && !wasSecondary) ReturnFocus();
            wasTrigger = trigger; wasPrimary = primary; wasSecondary = secondary;
        }
        private void SendPointer(int action) { if (id != 0) bridge.CallStatic("pointer", (long)id, action, lastPointer.x, lastPointer.y); }
        private void CancelPointer() { if (touching) SendPointer(3); touching = false; }
        private void BuildPanel()
        {
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) { var cameraObject = new GameObject("Main Camera", typeof(Camera)); cameraObject.tag = "MainCamera"; headCamera = cameraObject.GetComponent<Camera>(); }
            headCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            var canvasObject = new GameObject("Vulkan probe panel", typeof(RectTransform), typeof(Canvas));
            panel = canvasObject.transform; panel.position = new Vector3(0, 1.45f, 1.6f); panel.localScale = Vector3.one * 0.0012f;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = headCamera;
            ((RectTransform)panel).sizeDelta = new Vector2(1024, 1100);
            var browser = new GameObject("GPU browser", typeof(RectTransform), typeof(RawImage), typeof(BoxCollider));
            browser.transform.SetParent(panel, false); webRect = browser.GetComponent<RectTransform>(); webRect.sizeDelta = new Vector2(1024, 768);
            browser.GetComponent<BoxCollider>().size = new Vector3(1024, 768, 1);
            image = browser.GetComponent<RawImage>(); image.color = Color.white;
            image.uvRect = new Rect(0, flipImageVertically ? 1 : 0, 1, flipImageVertically ? -1 : 1);
            status = Label("Status", new Vector2(0, 550), new Vector2(1024, 320), "Waiting for Vulkan probe", 16);
            Button("IME", -420, RequestIme); Button("Focus", -252, ReturnFocus); Button("Reload", -84, Reload);
            Button("Recreate", 84, Recreate); Button("Resize", 252, Resize); Button("Flip Y", 420, Flip);
            var rayObject = new GameObject("Right controller ray", typeof(LineRenderer)); ray = rayObject.GetComponent<LineRenderer>();
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
        private void Button(string name, float x, Action action)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(VulkanProbeButton));
            owner.transform.SetParent(panel, false);
            var rect = owner.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(x, -434); rect.sizeDelta = new Vector2(156, 60);
            owner.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.4f, 1);
            owner.GetComponent<BoxCollider>().size = new Vector3(156, 60, 2);
            owner.GetComponent<VulkanProbeButton>().Action = action;
            var label = Label(name + " label", new Vector2(x, -434), new Vector2(156, 60), name, 24);
            label.alignment = TextAnchor.MiddleCenter;
        }
    }
}
