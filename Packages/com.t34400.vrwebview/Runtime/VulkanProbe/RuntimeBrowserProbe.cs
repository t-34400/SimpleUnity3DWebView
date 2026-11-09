using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using InputSystem = UnityEngine.InputSystem.InputSystem;

namespace T34400.VRWebView
{
    /// <summary>RT1 integration harness. All browser operations use the production controller/view/backend.</summary>
    public sealed class RuntimeBrowserProbe : MonoBehaviour
    {
        public Camera headCamera;
        private BrowserController controller;
        private BrowserPointerSource pointer;
        private RectTransform webRect;
        private Transform panel;
        private LineRenderer ray;
        private Material material;
        private Text status;
        private string html, failure;
        private GameObject composition;
        private readonly ProbeRayInput input = new ProbeRayInput();
        private readonly PointerPressState press = new PointerPressState();
        private Vector2 lastPoint;
        private bool placed;
        private float nextStatus;
        private async void Start()
        {
            Build();
            try {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var content = new AndroidJavaClass("com.t34400.vrwebview.RuntimeProbeContent"))
                    html = content.CallStatic<string>("page", activity);
                await controller.Session.InitializeAsync();
                if (this == null || !isActiveAndEnabled || controller == null) return;
                await controller.Session.LoadHtmlAsync(html);
                Debug.Log("[VRWebViewRuntime] RT1 ready: production BrowserController / BrowserView / AndroidBrowserBackend", this);
            } catch (OperationCanceledException) { }
            catch (Exception error) { failure = error.GetType().Name; Debug.LogException(error, this); }
        }
        private void OnEnable() { InputSystem.onDeviceChange += DeviceChanged; }
        private void DeviceChanged(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            if (device is MetaAimHand hand && (change == UnityEngine.InputSystem.InputDeviceChange.SoftReset
                    || change == UnityEngine.InputSystem.InputDeviceChange.HardReset)) {
                ResetPointer(); ProbeRayInput.SynchronizeHandReset(hand);
            }
        }
        private void OnApplicationFocus(bool focused) { if (!focused) ResetPointer(); }
        private void ResetPointer() { pointer?.Capture.Cancel(); press.Reset(); if (ray != null) ray.enabled = false; }
        private void OnDisable() {
            InputSystem.onDeviceChange -= DeviceChanged; ResetPointer();
            if (composition != null) composition.SetActive(false);
        }
        private void OnDestroy() {
            if (composition != null) Destroy(composition);
            if (panel != null) Destroy(panel.gameObject);
            if (ray != null) Destroy(ray.gameObject);
            if (material != null) Destroy(material);
        }
        private void Update()
        {
            if (panel == null || headCamera == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position)) headCamera.transform.localPosition = position;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation)) headCamera.transform.localRotation = rotation;
            if (!placed && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked) {
                Quaternion yaw = Quaternion.Euler(0, headCamera.transform.eulerAngles.y, 0);
                panel.position = headCamera.transform.position + yaw * Vector3.forward * 1.6f; panel.rotation = yaw; placed = true;
            }
            bool available = input.TryRead(ProbePointerMode.Auto, press.ActiveSourceId, out var sample);
            var phase = press.Update(available, (int)sample.Source, sample.Pressed);
            if (phase == PointerPhase.Cancel) pointer.Capture.Cancel();
            ray.enabled = available;
            if (available) {
                Vector3 origin = sample.Position; Quaternion aim = sample.Rotation;
                if (headCamera.transform.parent != null) { origin = headCamera.transform.parent.TransformPoint(origin); aim = headCamera.transform.parent.rotation * aim; }
                Vector3 direction = aim * Vector3.forward;
                bool hit = Physics.Raycast(origin, direction, out RaycastHit result, 5f);
                ray.SetPosition(0, origin); ray.SetPosition(1, hit ? result.point : origin + direction * 3);
                bool overPage = hit && result.collider.gameObject == webRect.gameObject;
                if (overPage) {
                    Vector3 local = webRect.InverseTransformPoint(result.point); Rect rect = webRect.rect;
                    overPage = BrowserCoordinates.TryNormalize(local.x, local.y, rect.xMin, rect.yMin, rect.width, rect.height, out float x, out float y);
                    if (overPage) lastPoint = new Vector2(x, y); // Top-left view coordinates; BrowserView maps UV/origin.
                }
                if (phase == PointerPhase.Down && hit && !overPage) result.collider.GetComponent<VulkanProbeButton>()?.Press();
                if (phase.HasValue) {
                    if (overPage && pointer.InputEnabled) pointer.Capture.Send((int)sample.Source, phase.Value, lastPoint.x, lastPoint.y);
                    else pointer.Capture.Cancel();
                }
                if (sample.ReturnFocus && controller.Backend != null && controller.Session?.State == BrowserState.Ready) controller.EndEditing();
            }
            if (Time.unscaledTime >= nextStatus) {
                nextStatus = Time.unscaledTime + 1;
                string state = failure ?? controller.Session?.State.ToString() ?? "closed";
                status.text = "RT1 production backend: " + state + "\nPinch Text -> Japanese IME -> Done/Back -> video.\n" + (controller.Backend?.GraphicsStatus ?? "");
                Debug.Log("[VRWebViewRuntime] RT1 state=" + state + " frames=" + Time.frameCount + " focus=" + Application.isFocused, this);
            }
        }
        private void Build()
        {
            if (headCamera == null) headCamera = Camera.main;
            var canvas = new GameObject("RT1 production browser canvas", typeof(RectTransform), typeof(Canvas)); panel = canvas.transform;
            panel.position = new Vector3(0, 1.45f, 1.6f); panel.localScale = Vector3.one * 0.0012f;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; canvas.GetComponent<Canvas>().worldCamera = headCamera;
            ((RectTransform)panel).sizeDelta = new Vector2(1024, 1100);
            var page = new GameObject("Production BrowserView", typeof(RectTransform), typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView), typeof(BoxCollider));
            page.transform.SetParent(panel, false); webRect = page.GetComponent<RectTransform>(); webRect.sizeDelta = new Vector2(1024, 768);
            page.GetComponent<BoxCollider>().size = new Vector3(1024, 768, 1); pointer = page.GetComponent<BrowserPointerSource>();
            composition = new GameObject("Production browser composition"); composition.SetActive(false);
            controller = composition.AddComponent<BrowserController>(); controller.Configure(page.GetComponent<BrowserView>(), "", true); composition.SetActive(true);
            var label = new GameObject("RT1 status", typeof(RectTransform), typeof(Text)); label.transform.SetParent(panel, false);
            label.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 510); label.GetComponent<RectTransform>().sizeDelta = new Vector2(1024, 230);
            status = label.GetComponent<Text>(); status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); status.fontSize = 22; status.color = Color.white;
            var button = new GameObject("End editing", typeof(RectTransform), typeof(Image), typeof(BoxCollider), typeof(VulkanProbeButton));
            button.transform.SetParent(panel, false); button.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -440);
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(360, 60); button.GetComponent<BoxCollider>().size = new Vector3(360, 60, 1);
            button.GetComponent<Image>().color = new Color(.1f, .25f, .4f, 1);
            button.GetComponent<VulkanProbeButton>().Action = () => { if (controller.Session?.State == BrowserState.Ready) controller.EndEditing(); };
            var caption = new GameObject("End editing label", typeof(RectTransform), typeof(Text)); caption.transform.SetParent(button.transform, false);
            caption.GetComponent<RectTransform>().sizeDelta = new Vector2(360, 60); var text = caption.GetComponent<Text>(); text.text = "End editing";
            text.font = status.font; text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            ray = new GameObject("RT1 application ray", typeof(LineRenderer)).GetComponent<LineRenderer>(); ray.positionCount = 2;
            ray.startWidth = ray.endWidth = .003f; ray.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default"); if (shader != null) { material = new Material(shader); ray.material = material; }
            ray.startColor = ray.endColor = Color.cyan; ray.enabled = false;
        }
    }
}
