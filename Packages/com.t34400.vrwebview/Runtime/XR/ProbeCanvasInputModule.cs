using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using InputSystem = UnityEngine.InputSystem.InputSystem;

namespace T34400.VRWebView
{
    /// <summary>Quest UI ray adapter: one Quest ray drives a group of world-space uGUI Canvases through
    /// normal EventSystem handlers. Applications may supply their own XR UI module.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(EventSystem))]
    public sealed class ProbeCanvasInputModule : PointerInputModule
    {
        public Canvas targetCanvas;
        public Camera headCamera;
        public bool enableDiagnostics = true;
        public ProbePointerMode pointerMode = ProbePointerMode.Auto;
        private readonly ProbeRayInput rayInput = new ProbeRayInput();
        private readonly PointerPressState press = new PointerPressState();
        private readonly List<GraphicRaycaster> raycasters = new List<GraphicRaycaster>();
        private BrowserRayPointerEventData pointer;
        private Plane capturePlane;
        private Vector3 pressRayOrigin;
        private ProbePointerVisual visual;
        private Color feedback;
        private float feedbackUntil;
        private Vector3 previousWorldPoint, currentWorldPoint, pressWorldPoint;
        private int previousSource;
        private bool haveSource, havePosition, placed;
        private float nextTick;
        private const int PointerId = -1;

        protected override void OnEnable()
        {
            base.OnEnable();
            InputSystem.onDeviceChange += DeviceChanged;
        }
        protected override void OnDisable()
        {
            InputSystem.onDeviceChange -= DeviceChanged;
            CancelPointer();
            visual?.Hide();
            base.OnDisable();
        }
        protected override void OnDestroy()
        {
            visual?.Dispose(); visual = null;
            base.OnDestroy();
        }
        public override void DeactivateModule() { CancelPointer(); base.DeactivateModule(); }
        public override bool IsPointerOverGameObject(int pointerId) => pointerId == PointerId && pointer != null && pointer.pointerEnter != null;
        private void OnApplicationFocus(bool focused)
        {
            if (enableDiagnostics) Debug.Log("[VRWebViewSample] BS1 event=focus active=" + focused, this);
            if (!focused) { CancelPointer(); visual?.Hide(); }
        }
        private void DeviceChanged(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            if (device is MetaAimHand hand && (change == UnityEngine.InputSystem.InputDeviceChange.SoftReset
                    || change == UnityEngine.InputSystem.InputDeviceChange.HardReset)) {
                CancelPointer(); ProbeRayInput.SynchronizeHandReset(hand);
            }
        }
        public override void Process()
        {
            if (headCamera == null || targetCanvas == null) return;
            UpdateHead();
            // Selected InputField keeps polling its native keyboard while the overlay
            // owns input. Losing XR focus cancels the ray, not the field selection.
            if (eventSystem.currentSelectedGameObject != null)
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, GetBaseEventData(), ExecuteEvents.updateSelectedHandler);
            ProbeRaySample sample = default;
            bool tracked = Application.isFocused && rayInput.TryRead(pointerMode, press.ActiveSourceId, out sample);
            Vector3 origin = sample.Position; Quaternion rotation = sample.Rotation;
            if (headCamera.transform.parent != null) {
                origin = headCamera.transform.parent.TransformPoint(origin);
                rotation = headCamera.transform.parent.rotation * rotation;
            }
            var ray = new Ray(origin, rotation * Vector3.forward);
            ProcessRay(tracked, ray, sample.Pressed, (int)sample.Source, sample.Scroll, rotation);
            if (visual == null) visual = new ProbePointerVisual();
            var currentHit = pointer != null ? pointer.pointerCurrentRaycast : default;
            visual.Show(tracked && pointer != null && pointer.HasWorldRay, ray, currentHit, headCamera,
                press.ActiveSourceId.HasValue, Time.unscaledTime < feedbackUntil ? (Color?)feedback : null);
            if (Time.unscaledTime >= nextTick) {
                nextTick = Time.unscaledTime + 1;
                if (enableDiagnostics) Debug.Log("[VRWebViewSample] BS1 event=ray tracked=" + tracked + " pressed=" + sample.Pressed
                    + " source=" + sample.Source + " appFocus=" + Application.isFocused + " frames=" + Time.frameCount
                    + " hit=" + Kind(currentHit.gameObject) + " distance=" + currentHit.distance.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), this);
            }
        }
        /// <summary>Explicit ray entry for rig tests/alternate diagnostic sources. Call
        /// once per input update; tracked must be false when the source loses permission.</summary>
        public void ProcessRay(bool tracked, Ray ray, bool pressed, int sourceId, Vector2 scroll, Quaternion? worldRotation = null)
        {
            if (!tracked || targetCanvas == null || headCamera == null || !Finite(ray.origin.x) || !Finite(ray.origin.y)
                || !Finite(ray.origin.z) || !Finite(ray.direction.x) || !Finite(ray.direction.y) || !Finite(ray.direction.z) || ray.direction.sqrMagnitude < .5f)
                { CancelPointer(); return; }
            if (haveSource && sourceId != previousSource) CancelPointer();
            previousSource = sourceId; haveSource = true;
            if (pointer == null) pointer = new BrowserRayPointerEventData(eventSystem) { pointerId = PointerId, button = PointerEventData.InputButton.Left };
            pointer.Reset(); pointer.WorldRay = ray; pointer.HasWorldRay = true;
            pointer.HasWorldRotation = worldRotation.HasValue; pointer.WorldRotation = worldRotation ?? Quaternion.LookRotation(ray.direction, Vector3.up);
            RaycastResult chosen = default; Vector3 chosenPoint = default; Vector2 chosenScreen = default;
            float nearest = float.PositiveInfinity; int sorting = int.MinValue;
            // Intersect each window at its own depth using the hand/controller ray.
            // Project only that plane's hit for uGUI's graphic/mask tests. A single
            // projection onto the root plane misidentifies targets at another depth.
            targetCanvas.GetComponentsInChildren(false, raycasters);
            foreach (var raycaster in raycasters) {
                if (!BrowserWorldRaycast.TryRaycast(raycaster, ray, headCamera, out var hit)) continue;
                if (hit.distance > nearest + .0001f || (Mathf.Abs(hit.distance - nearest) <= .0001f && hit.sortingOrder < sorting)) continue;
                if (Mathf.Abs(hit.distance - nearest) <= .0001f && hit.sortingOrder == sorting && chosen.gameObject != null) continue;
                chosen = hit; chosenPoint = hit.worldPosition; chosenScreen = hit.screenPosition; nearest = hit.distance; sorting = hit.sortingOrder;
            }
            if (chosen.gameObject == null) {
                var plane = press.ActiveSourceId.HasValue ? capturePlane : new Plane(targetCanvas.transform.forward, targetCanvas.transform.position);
                chosenPoint = plane.Raycast(ray, out float distance) && distance >= 0 ? ray.GetPoint(distance) : ray.GetPoint(3);
                var screen = headCamera.WorldToScreenPoint(chosenPoint);
                if (screen.z <= 0 || !Finite(screen.x) || !Finite(screen.y)) { CancelPointer(); return; }
                chosenScreen = new Vector2(screen.x, screen.y);
            }
            currentWorldPoint = chosenPoint;
            Vector3 previousScreen = havePosition ? headCamera.WorldToScreenPoint(previousWorldPoint) : new Vector3(chosenScreen.x, chosenScreen.y, 0);
            pointer.delta = chosenScreen - new Vector2(previousScreen.x, previousScreen.y);
            previousWorldPoint = currentWorldPoint;
            if (press.ActiveSourceId.HasValue) {
                Vector3 pressScreen = headCamera.WorldToScreenPoint(pressWorldPoint);
                pointer.pressPosition = new Vector2(pressScreen.x, pressScreen.y);
            }
            pointer.position = chosenScreen; havePosition = true; pointer.scrollDelta = scroll; pointer.pointerCurrentRaycast = chosen;
            ProcessMove(pointer);
            var phase = press.Update(true, sourceId, pressed);
            if (phase == PointerPhase.Cancel) { CancelPointer(); return; }
            if (phase == PointerPhase.Down) Press();
            else if (phase == PointerPhase.Up) Release();
            if (pressed) ProcessDrag(pointer);
            if (!pressed && scroll.sqrMagnitude > .01f && pointer.pointerCurrentRaycast.gameObject != null)
                ExecuteEvents.ExecuteHierarchy(pointer.pointerCurrentRaycast.gameObject, pointer, ExecuteEvents.scrollHandler);
        }
        // Cursor.lockState belongs to the desktop mouse and must not disable a VR ray.
        protected override void ProcessMove(PointerEventData data) { HandlePointerExitAndEnter(data, data.pointerCurrentRaycast.gameObject); }
        protected override void ProcessDrag(PointerEventData data)
        {
            if (data.pointerDrag == null) return;
            bool spatial = data is BrowserRayPointerEventData ray && ray.HasWorldRay
                && ExecuteEvents.CanHandleEvent<IBrowserSpatialDragHandler>(data.pointerDrag)
                && (ray.WorldRay.origin - pressRayOrigin).sqrMagnitude >= .0001f;
            if (!data.dragging && (!data.useDragThreshold || spatial || (data.pressPosition - data.position).sqrMagnitude >= eventSystem.pixelDragThreshold * eventSystem.pixelDragThreshold)) {
                ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler); data.dragging = true;
                if (ExecuteEvents.CanHandleEvent<IBrowserSpatialDragHandler>(data.pointerDrag)) data.eligibleForClick = false;
            }
            if (!data.dragging) return;
            if (data.pointerPress != data.pointerDrag) {
                ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                data.eligibleForClick = false; data.pointerPress = null; data.rawPointerPress = null;
            }
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
        }
        private void Press()
        {
            var over = pointer.pointerCurrentRaycast.gameObject;
            pointer.eligibleForClick = true; pointer.delta = Vector2.zero;
            pointer.dragging = false; pointer.useDragThreshold = true;
            pointer.pressPosition = pointer.position; pressWorldPoint = currentWorldPoint; pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            pressRayOrigin = pointer.WorldRay.origin;
            var canvas = over != null ? over.GetComponentInParent<Canvas>() : targetCanvas;
            capturePlane = new Plane(canvas.transform.forward, canvas.transform.position);
            DeselectIfSelectionChanged(over, pointer);
            var pressed = ExecuteEvents.ExecuteHierarchy(over, pointer, ExecuteEvents.pointerDownHandler);
            if (pressed == null) pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            float time = Time.unscaledTime;
            pointer.clickCount = pressed == pointer.lastPress && time - pointer.clickTime < .3f ? pointer.clickCount + 1 : 1;
            pointer.clickTime = time; pointer.pointerPress = pressed; pointer.rawPointerPress = over;
            pointer.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(over);
            if (pointer.pointerDrag != null) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.initializePotentialDrag);
            TracePointer("Down", over);
        }
        private void Release()
        {
            var over = pointer.pointerCurrentRaycast.gameObject;
            ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerUpHandler);
            var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(over);
            bool delivered = pointer.pointerPress != null && pointer.pointerPress == click && pointer.eligibleForClick;
            bool dragging = pointer.pointerDrag != null && pointer.dragging;
            if (enableDiagnostics) Debug.Log("[VRWebViewSample] BS1 event=activation press=" + Kind(pointer.pointerPress) + " release=" + Kind(over)
                + " click=" + delivered + " drag=" + dragging, this);
            if (delivered) ExecuteEvents.Execute(pointer.pointerPress, pointer, ExecuteEvents.pointerClickHandler);
            else if (dragging) ExecuteEvents.ExecuteHierarchy(over, pointer, ExecuteEvents.dropHandler);
            feedback = delivered || dragging ? Color.green : new Color(1, .4f, .15f); feedbackUntil = Time.unscaledTime + .18f;
            EndPress(); TracePointer("Up", over);
        }
        private void EndPress()
        {
            if (pointer.pointerDrag != null && pointer.dragging) ExecuteEvents.Execute(pointer.pointerDrag, pointer, ExecuteEvents.endDragHandler);
            pointer.eligibleForClick = false; pointer.dragging = false;
            pointer.pointerPress = null; pointer.rawPointerPress = null; pointer.pointerDrag = null;
        }
        public void CancelPointer()
        {
            press.Reset(); haveSource = false; havePosition = false; visual?.Hide();
            if (pointer == null) return;
            pointer.HasWorldRay = false;
            var pressed = pointer.pointerPress; var dragged = pointer.pointerDrag;
            // BrowserPointerSource implements Cancel: never turn focus loss into a
            // touch-up/click. Ordinary uGUI controls still need Up to clear visuals.
            if (pressed != null && !ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.cancelHandler))
                ExecuteEvents.Execute(pressed, pointer, ExecuteEvents.pointerUpHandler);
            if (dragged != null && dragged != pressed) ExecuteEvents.Execute(dragged, pointer, ExecuteEvents.cancelHandler);
            bool active = pressed != null || dragged != null;
            EndPress(); HandlePointerExitAndEnter(pointer, null); pointer.pointerCurrentRaycast = default;
            if (active) TracePointer("Cancel", pressed);
        }
        private void UpdateHead()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position)) headCamera.transform.localPosition = position;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation)) headCamera.transform.localRotation = rotation;
            if (!placed && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked) {
                Quaternion yaw = Quaternion.Euler(0, headCamera.transform.eulerAngles.y, 0);
                targetCanvas.transform.position = headCamera.transform.position + yaw * Vector3.forward * 1.6f;
                targetCanvas.transform.rotation = yaw; placed = true;
            }
        }
        private static string Kind(GameObject target) {
            return target == null ? "none" : target.GetComponentInParent<BrowserPointerSource>() != null ? "page"
                : target.GetComponentInParent<InputField>() != null ? "address" : target.GetComponentInParent<Button>() != null ? "button"
                : target.GetComponentInParent<Scrollbar>() != null ? "scrollbar"
                : ExecuteEvents.GetEventHandler<IBrowserSpatialDragHandler>(target) != null ? "spatial" : "other";
        }
        private void TracePointer(string phase, GameObject target) {
            if (enableDiagnostics) Debug.Log("[VRWebViewSample] BS1 event=pointer phase=" + phase + " target=" + Kind(target), this);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
