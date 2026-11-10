using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace T34400.VRWebView
{
    public enum ProbePointerMode { Auto, RightController, RightHand, LeftHand }
    internal enum ProbePointerSource { None, RightController, RightHand, LeftHand, RightHandJoints, LeftHandJoints }
    internal struct ProbeRaySample
    {
        internal ProbePointerSource Source;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal bool Pressed, RequestIme, ReturnFocus;
        internal Vector2 Scroll;
    }

    // Only this probe input adapter knows about XR Hands. Rendering and Android
    // receive the same single-pointer stream for controllers and hand aim.
    internal sealed class ProbeRayInput
    {
        internal bool ControllerTracked { get; private set; }
        internal bool RightHandPresent { get; private set; }
        internal bool LeftHandPresent { get; private set; }
        internal bool RightHandTracked { get; private set; }
        internal bool LeftHandTracked { get; private set; }
        internal string RightHandDetails => DescribeHand(MetaAimHand.right);
        internal string LeftHandDetails => DescribeHand(MetaAimHand.left);
        internal static void SynchronizeHandReset(MetaAimHand hand)
        {
            if (hand == null || !hand.added) return;
            // Probe-only XR Hands 1.7.3 compatibility hook. Input System resets
            // controls, but the SDK's m_WasTracked/m_PreviousFlags/pinch caches
            // otherwise survive. Submit an untracked reset sample through its
            // public updater; the real provider supplies the next tracked pose.
            // Never force isTracked/Valid true, use reflection, or alter focus policy.
            hand.UpdateHand(false, MetaAimFlags.None, Pose.identity, 0f, 0f, 0f, 0f);
        }
        private readonly List<XRHandSubsystem> handSubsystems = new List<XRHandSubsystem>();
        internal string RootDetails()
        {
            SubsystemManager.GetSubsystems(handSubsystems);
            foreach (var subsystem in handSubsystems)
                if (subsystem.running) return "roots R=" + subsystem.rightHand.isTracked + " L=" + subsystem.leftHand.isTracked;
            return "roots subsystem=not-running";
        }

        private static string DescribeHand(MetaAimHand hand)
        {
            if (hand == null || !hand.added) return "absent";
            var flags = (MetaAimFlags)(uint)hand.aimFlags.ReadValue();
            return "enabled=" + hand.enabled + " rawTracked=" + hand.isTracked.isPressed
                + " flags=0x" + ((ulong)flags).ToString("X")
                + " background=" + hand.canRunInBackground;
        }

        internal bool TryRead(ProbePointerMode mode, int? capturedSource, out ProbeRaySample sample)
        {
            ControllerTracked = TryController(out var controller);
            RightHandPresent = MetaAimHand.right != null && MetaAimHand.right.added;
            LeftHandPresent = MetaAimHand.left != null && MetaAimHand.left.added;
            RightHandTracked = TryHand(MetaAimHand.right, ProbePointerSource.RightHand, out var right);
            LeftHandTracked = TryHand(MetaAimHand.left, ProbePointerSource.LeftHand, out var left);
            sample = default;
            if (mode == ProbePointerMode.Auto)
            {
                // A second source cannot steal a held gesture from its tracked owner.
                if (capturedSource == (int)ProbePointerSource.RightController && ControllerTracked) { sample = controller; return true; }
                if (capturedSource == (int)ProbePointerSource.RightHand && RightHandTracked) { sample = right; return true; }
                if (capturedSource == (int)ProbePointerSource.LeftHand && LeftHandTracked) { sample = left; return true; }
                if (ControllerTracked) { sample = controller; return true; }
                if (RightHandTracked) { sample = right; return true; }
                if (LeftHandTracked) { sample = left; return true; }
                return false;
            }
            if (mode == ProbePointerMode.RightController && ControllerTracked) { sample = controller; return true; }
            if (mode == ProbePointerMode.RightHand && RightHandTracked) { sample = right; return true; }
            if (mode == ProbePointerMode.LeftHand && LeftHandTracked) { sample = left; return true; }
            return false;
        }
        private static bool TryController(out ProbeRaySample sample)
        {
            sample = default;
            var hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (!hand.isValid || (hand.characteristics & InputDeviceCharacteristics.Controller) == 0
                    || !hand.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked
                    || !hand.TryGetFeatureValue(CommonUsages.devicePosition, out sample.Position)
                    || !hand.TryGetFeatureValue(CommonUsages.deviceRotation, out sample.Rotation)) return false;
            sample.Source = ProbePointerSource.RightController;
            hand.TryGetFeatureValue(CommonUsages.triggerButton, out sample.Pressed);
            hand.TryGetFeatureValue(CommonUsages.primaryButton, out sample.RequestIme);
            hand.TryGetFeatureValue(CommonUsages.secondaryButton, out sample.ReturnFocus);
            hand.TryGetFeatureValue(CommonUsages.primary2DAxis, out sample.Scroll);
            return true;
        }
        private static bool TryHand(MetaAimHand hand, ProbePointerSource source, out ProbeRaySample sample)
        {
            sample = default;
            if (hand == null || !hand.added || !hand.isTracked.isPressed) return false;
            var flags = (MetaAimFlags)(uint)hand.aimFlags.ReadValue();
            var required = MetaAimFlags.Computed | MetaAimFlags.Valid;
            if ((flags & required) != required || (flags & MetaAimFlags.SystemGesture) != 0) return false;
            sample.Source = source;
            sample.Position = hand.devicePosition.ReadValue();
            sample.Rotation = hand.deviceRotation.ReadValue();
            sample.Pressed = hand.indexPressed.isPressed;
            return true;
        }
    }
}
