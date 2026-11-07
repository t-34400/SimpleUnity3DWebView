using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace T34400.VRWebView
{
    /// <summary>AG1 geometric ray, only consumed under the owned Window + XR focus policy.</summary>
    internal sealed class JointProbeRay : IDisposable
    {
        private readonly List<XRHandSubsystem> systems = new List<XRHandSubsystem>();
        private readonly List<XRHandSubsystem> subscribed = new List<XRHandSubsystem>();
        private readonly ProbePinch right = new ProbePinch(), left = new ProbePinch();
        private double rightAt = double.NegativeInfinity, leftAt = double.NegativeInfinity;
        private XRHandSubsystem latest;
        internal string Diagnostics => "jointsRunning=" + (latest != null && latest.running)
            + " ageRms=" + Age(rightAt) + " ageLms=" + Age(leftAt);
        private static string Age(double at) => double.IsInfinity(at) ? "unknown" : ((Time.realtimeSinceStartupAsDouble - at) * 1000).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        internal void Refresh()
        {
            SubsystemManager.GetSubsystems(systems);
            foreach (var s in systems) if (!subscribed.Contains(s)) { s.updatedHands += Updated; subscribed.Add(s); }
        }
        private void Updated(XRHandSubsystem s, XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType type)
        {
            if (type != XRHandSubsystem.UpdateType.Dynamic) return;
            latest = s;
            var r = XRHandSubsystem.UpdateSuccessFlags.RightHandRootPose | XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;
            var l = XRHandSubsystem.UpdateSuccessFlags.LeftHandRootPose | XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints;
            rightAt = (flags & r) == r ? Time.realtimeSinceStartupAsDouble : double.NegativeInfinity;
            leftAt = (flags & l) == l ? Time.realtimeSinceStartupAsDouble : double.NegativeInfinity;
        }
        internal bool TryRead(Transform head, int? captured, out ProbeRaySample sample)
        {
            sample = default;
            if (latest == null || !latest.running) return false;
            var r = TryHand(latest.rightHand, true, rightAt, head, out var rs);
            var l = TryHand(latest.leftHand, false, leftAt, head, out var ls);
            if (captured == (int)ProbePointerSource.LeftHandJoints && l) { sample = ls; return true; }
            if (r) { sample = rs; return true; }
            if (l) { sample = ls; return true; }
            return false;
        }
        private bool TryHand(XRHand hand, bool isRight, double at, Transform head, out ProbeRaySample sample)
        {
            sample = default; var pinch = isRight ? right : left;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb)
                || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index)
                || !hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose knuckle)
                || !hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out Pose little)) { pinch.Reset(); return false; }
            Vector3 point = (thumb.position + index.position) * 0.5f;
            Vector3 shoulder = head.localPosition + head.localRotation * new Vector3(isRight ? 0.15f : -0.15f, -0.15f, 0);
            Vector3 direction = point - shoulder;
            Vector3 normal = Vector3.Cross(knuckle.position - hand.rootPose.position, little.position - hand.rootPose.position);
            // Avoid palm/back-of-hand facing the headset, including typical menu gestures.
            if (!Finite(point) || !Finite(direction) || !Finite(normal) || direction.sqrMagnitude < 0.01f
                || normal.sqrMagnitude < 0.00000001f
                || Mathf.Abs(Vector3.Dot(normal.normalized, (head.localPosition - hand.rootPose.position).normalized)) > 0.8f
                || !pinch.Update(Vector3.Distance(thumb.position, index.position), Time.realtimeSinceStartupAsDouble - at)) { pinch.Reset(); return false; }
            sample.Source = isRight ? ProbePointerSource.RightHandJoints : ProbePointerSource.LeftHandJoints;
            sample.Position = point; sample.Rotation = Quaternion.LookRotation(direction.normalized, Vector3.up); sample.Pressed = pinch.Pressed;
            return true;
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        internal void Reset() { rightAt = leftAt = double.NegativeInfinity; right.Reset(); left.Reset(); }
        public void Dispose() { foreach (var s in subscribed) s.updatedHands -= Updated; subscribed.Clear(); latest = null; Reset(); }
    }
}
