using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using InputSystem = UnityEngine.InputSystem.InputSystem;

namespace T34400.VRWebView
{
    /// <summary>HC2: keyboard boot flag and same-Activity editor comparison, without GPU-copy sessions.</summary>
    public sealed class HostComparisonProbe : MonoBehaviour
    {
        public Camera headCamera;
        // Optional initial-menu accessibility aid. Never reactivate after a trial/focus loss.
        public bool gazeFallback = false;
        // Probe-only XR Hands 1.7.3 compatibility hook; disable for baseline comparison.
        public bool synchronizeHandReset = true;
        private AndroidJavaClass bridge;
        private AndroidJavaObject owner;
        private TouchScreenKeyboard keyboard;
        private Transform panel, spinner;
        private Text status;
        private LineRenderer ray;
        private Material rayMaterial;
        private readonly ProbeRayInput input = new ProbeRayInput();
        private readonly PointerPressState press = new PointerPressState();
        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private string startup = "menu", lastKeyboard, lastHands;
        private float startupAt, keyboardUntil, nextPulse, dwellSince;
        private bool placed, paused, nativeActive;
        private bool gazeSuspended, pointerTracked, pointerBlocked, gazeAllowed;
        private long renders;
        private int lastRenderedFrame = -1;
        private VulkanProbeButton dwellTarget, blockedDwell;

        private void Start()
        {
            BuildPanel();
            OpenBridge(true);
        }
        private void OpenBridge(bool readIntent)
        {
            if (bridge != null) return;
            if (Application.platform != RuntimePlatform.Android) { Trace("Android player required"); return; }
            bridge = new AndroidJavaClass("com.t34400.vrwebview.HostComparison");
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                owner = player.GetStatic<AndroidJavaObject>("currentActivity");
            WithActivity(activity => {
                bridge.CallStatic("start", activity);
                startup = readIntent ? bridge.CallStatic<string>("startupCase", activity) : "menu";
            });
            startupAt = Time.unscaledTime + 3f;
            Trace("start startupCase=" + startup + " renderer=" + SystemInfo.graphicsDeviceType + " handResetSync=" + synchronizeHandReset);
        }
        private void WithActivity(Action<AndroidJavaObject> action)
        {
            if (owner != null) action(owner);
        }
        private void Trace(string value) { Debug.Log("[VRWebViewHost] HC2 frame=" + Time.frameCount + " " + value, this); }
        public void RunKeyboard()
        {
            if (Application.platform != RuntimePlatform.Android) return;
            CloseKeyboard();
            // Meta's documented system keyboard path. No WebView, DOM copying,
            // or focus reclamation. Optional hand-reset synchronization is independent.
            keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default);
            keyboardUntil = Time.unscaledTime + 20f; lastKeyboard = null;
            Trace("TouchScreenKeyboard.Open type=Default returned=" + (keyboard != null));
        }
        public void CloseKeyboard()
        {
            if (keyboard == null) return;
            keyboard.active = false; keyboard = null;
            Trace("keyboard close requested");
        }
        public void RunNativeEditor() { if (bridge != null) WithActivity(a => bridge.CallStatic("openNativeEditor", a)); }
        public void RunMatchedEditor() { if (bridge != null) WithActivity(a => bridge.CallStatic("openMatchedEditor", a)); }
        public void RunNativeWebView() { if (bridge != null) WithActivity(a => bridge.CallStatic("openNativeWebView", a)); }
        public void RunOpaque() { if (bridge != null) WithActivity(a => bridge.CallStatic("launch", a, false)); }
        public void RunTransparent() { if (bridge != null) WithActivity(a => bridge.CallStatic("launch", a, true)); }
        public void ToggleHandResetSync()
        {
            synchronizeHandReset = !synchronizeHandReset;
            Trace("handResetSyncEnabled=" + synchronizeHandReset);
        }
        private void RunCase(string mode)
        {
            gazeSuspended = true; dwellTarget = null;
            Trace("run case=" + mode);
            switch (mode) {
                case "keyboard": RunKeyboard(); break;
                case "native": RunNativeEditor(); break;
                case "native-webinfo": RunMatchedEditor(); break;
                case "webview": RunNativeWebView(); break;
                case "activity": RunOpaque(); break;
                case "transparent": RunTransparent(); break;
            }
        }
        private void Update()
        {
            UpdateHead();
            SubsystemManager.GetSubsystems(displays);
            bool running = displays.Exists(d => d.running);
            if (startup != "menu" && bridge != null && !paused && placed && running && Application.isFocused && Time.unscaledTime >= startupAt)
            { string mode = startup; startup = "menu"; RunCase(mode); }
            if (spinner != null) spinner.localRotation = Quaternion.Euler(0, Time.unscaledTime * 60f, Time.unscaledTime * 35f);
            if (bridge != null) nativeActive = bridge.CallStatic<bool>("isTrialActive");
            if (keyboard != null && Time.unscaledTime >= keyboardUntil) CloseKeyboard();
            UpdatePointer();
            if (Time.unscaledTime < nextPulse) return;
            nextPulse = Time.unscaledTime + 1f;
            if (bridge != null) bridge.CallStatic("pulse", (long)Time.frameCount, renders, running, Application.isFocused);
            string keyboardState = "keyboard instance=none visible=" + TouchScreenKeyboard.visible;
            if (keyboard != null)
                keyboardState = "keyboard active=" + keyboard.active + " visible=" + TouchScreenKeyboard.visible
                    + " status=" + keyboard.status + " textLength=" + (keyboard.text ?? "").Length;
            if (keyboardState != lastKeyboard) { Trace(keyboardState); lastKeyboard = keyboardState; }
            string hands = "R " + input.RightHandDetails + " L " + input.LeftHandDetails + " " + input.RootDetails();
            if (hands != lastHands) { Trace(hands); lastHands = hands; }
            Trace("tick renders=" + renders + " appFocus=" + Application.isFocused + " paused=" + paused + " xrRunning=" + running + " nativeTrial=" + nativeActive
                + " pointerTracked=" + pointerTracked + " pointerBlocked=" + pointerBlocked + " gazeAllowed=" + gazeAllowed + " handResetSync=" + synchronizeHandReset);
            if (status != null) status.text = "HC2  Unity frames=" + Time.frameCount + "  XR running=" + running
                + "\nappFocus=" + Application.isFocused + " paused=" + paused + " nativeTrial=" + nativeActive
                + " handResetSync=" + synchronizeHandReset
                + "\n" + keyboardState + "\nGPU-copy inactive. Compare boot OFF/ON in separate builds."
                + "\nPinch/trigger to select. ADB trials do not require hand input."
                + "\nNative trials return after 20s on the Android UI thread."
                + "\nOptional gaze input is disabled after a trial or focus loss."
                + "\nRead logs; no screen transcription is needed.";
        }
        private void UpdateHead()
        {
            if (headCamera == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p)) headCamera.transform.localPosition = p;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion q)) headCamera.transform.localRotation = q;
            if (!placed && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            {
                Quaternion yaw = Quaternion.Euler(0, headCamera.transform.eulerAngles.y, 0);
                panel.position = headCamera.transform.position + yaw * Vector3.forward * 1.6f;
                panel.rotation = yaw; placed = true;
            }
        }
        private void UpdatePointer()
        {
            if (ray == null || headCamera == null) return;
            bool tracked = input.TryRead(ProbePointerMode.Auto, press.ActiveSourceId, out var sample);
            bool blocked = paused || !Application.isFocused || nativeActive || TouchScreenKeyboard.visible
                || (keyboard != null && keyboard.active);
            pointerTracked = tracked; pointerBlocked = blocked;
            gazeAllowed = gazeFallback && !gazeSuspended;
            var phase = press.Update(tracked && !blocked, (int)sample.Source, sample.Pressed);
            if (blocked || (!tracked && !gazeAllowed)) { ray.enabled = false; dwellTarget = null; return; }
            Vector3 origin = headCamera.transform.position, direction = headCamera.transform.forward;
            if (tracked)
            {
                origin = sample.Position; Quaternion rotation = sample.Rotation;
                if (headCamera.transform.parent != null) {
                    origin = headCamera.transform.parent.TransformPoint(origin);
                    rotation = headCamera.transform.parent.rotation * rotation;
                }
                direction = rotation * Vector3.forward;
            }
            bool hit = Physics.Raycast(origin, direction, out RaycastHit result, 5f);
            ray.enabled = true; ray.SetPosition(0, origin); ray.SetPosition(1, hit ? result.point : origin + direction * 3f);
            var target = hit ? result.collider.GetComponent<VulkanProbeButton>() : null;
            if (target != blockedDwell) blockedDwell = null;
            if (tracked)
            {
                dwellTarget = null;
                if (phase == PointerPhase.Down && target != null) { blockedDwell = target; PressButton(target, "hand-or-controller"); }
            }
            else if (target != null && target != blockedDwell)
            {
                if (target != dwellTarget) { dwellTarget = target; dwellSince = Time.unscaledTime; }
                else if (Time.unscaledTime - dwellSince >= 2f) { blockedDwell = target; dwellTarget = null; PressButton(target, "head-gaze"); }
            }
            else dwellTarget = null;
        }
        private void PressButton(VulkanProbeButton target, string source)
        {
            Trace("button=" + target.name + " source=" + source); target.Press();
        }
        private void OnApplicationFocus(bool value) { Trace("applicationFocus=" + value); if (!value) { press.Reset(); dwellTarget = null; gazeSuspended = true; } }
        private void OnApplicationPause(bool value) { paused = value; Trace("applicationPause=" + value); }
        private void OnEnable()
        {
            InputSystem.onDeviceChange += DeviceChanged;
            Camera.onPostRender += CameraRendered;
            RenderPipelineManager.endCameraRendering += PipelineCameraRendered;
            if (panel != null) OpenBridge(false);
        }
        private void OnDisable()
        {
            InputSystem.onDeviceChange -= DeviceChanged;
            Camera.onPostRender -= CameraRendered;
            RenderPipelineManager.endCameraRendering -= PipelineCameraRendered;
            Shutdown();
        }
        private void PipelineCameraRendered(ScriptableRenderContext context, Camera camera) { CameraRendered(camera); }
        private void CameraRendered(Camera camera)
        {
            if (camera == headCamera && lastRenderedFrame != Time.frameCount) { renders++; lastRenderedFrame = Time.frameCount; }
        }
        private void DeviceChanged(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            if (!(device is MetaAimHand hand)) return;
            string side = hand == MetaAimHand.right ? "R" : hand == MetaAimHand.left ? "L" : "other";
            Trace("handDevice side=" + side + " id=" + device.deviceId + " change=" + change + " enabled=" + device.enabled);
            if (synchronizeHandReset && Application.platform == RuntimePlatform.Android
                && (change == UnityEngine.InputSystem.InputDeviceChange.SoftReset
                    || change == UnityEngine.InputSystem.InputDeviceChange.HardReset))
            {
                // Clear pending gestures; a newly tracked source must release before pressing.
                press.Reset(); dwellTarget = null;
                ProbeRayInput.SynchronizeHandReset(hand);
                Trace("handResetSync side=" + side + " id=" + device.deviceId + " sample=untracked");
            }
        }
        private void Shutdown()
        {
            CloseKeyboard();
            if (bridge != null) { WithActivity(a => bridge.CallStatic("stop", a)); bridge.Dispose(); bridge = null; }
            owner?.Dispose(); owner = null;
        }
        private void OnDestroy()
        {
            Shutdown();
            if (panel != null) Destroy(panel.gameObject);
            if (ray != null) Destroy(ray.gameObject);
            if (rayMaterial != null) Destroy(rayMaterial);
        }
        private void BuildPanel()
        {
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) {
                var cameraObject = new GameObject("Main Camera", typeof(Camera)); cameraObject.tag = "MainCamera";
                headCamera = cameraObject.GetComponent<Camera>();
            }
            headCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            var owner = new GameObject("Host comparison", typeof(RectTransform), typeof(Canvas));
            panel = owner.transform; panel.localScale = Vector3.one * 0.0013f; panel.position = new Vector3(0, 1.4f, 1.6f);
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = headCamera;
            owner.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 850);
            status = Label("Status", new Vector2(0, 160), new Vector2(1000, 320), "HC2 preparing", 22);
            Button("Unity keyboard", -330, -120, () => RunCase("keyboard"));
            Button("Unity EditText", 0, -120, () => RunCase("native"));
            Button("Close keyboard", 330, -120, CloseKeyboard);
            Button("Unity WebView", -330, -240, () => RunCase("webview"));
            Button("Other Activity", 0, -240, () => RunCase("activity"));
            Button("Transparent Activity", 330, -240, () => RunCase("transparent"));
            Button("Toggle reset sync", -330, -350, ToggleHandResetSync);
            Button("Recenter", 0, -350, () => placed = false);
            Button("Matched EditText", 330, -350, () => RunCase("native-webinfo"));
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Frame continuity marker";
            cube.transform.SetParent(panel, false); cube.transform.localPosition = new Vector3(0, 400, 0); cube.transform.localScale = Vector3.one * 65;
            Destroy(cube.GetComponent<Collider>()); spinner = cube.transform;
            var rayObject = new GameObject("Host probe ray", typeof(LineRenderer)); ray = rayObject.GetComponent<LineRenderer>();
            ray.positionCount = 2; ray.startWidth = ray.endWidth = 0.003f; ray.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader != null) { rayMaterial = new Material(shader); ray.material = rayMaterial; }
            ray.startColor = ray.endColor = Color.cyan; ray.enabled = false;
        }
        private Text Label(string name, Vector2 position, Vector2 size, string value, int fontSize)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Text)); owner.transform.SetParent(panel, false);
            var rect = owner.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = owner.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.color = Color.white; text.text = value; text.raycastTarget = false; return text;
        }
        private void Button(string name, float x, float y, Action action)
        {
            var owner = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(VulkanProbeButton));
            owner.transform.SetParent(panel, false); var rect = owner.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(300, 80);
            owner.GetComponent<BoxCollider>().size = new Vector3(300, 80, 1);
            owner.GetComponent<Image>().color = new Color(0.1f, 0.25f, 0.4f);
            owner.GetComponent<VulkanProbeButton>().Action = action;
            Label(name + " label", new Vector2(x, y), new Vector2(300, 80), name, 22).alignment = TextAnchor.MiddleCenter;
        }
    }
}
