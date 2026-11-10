using System;
using System.Collections.Generic;

namespace T34400.VRWebView
{
    internal enum GpuRetirementState
    {
        Untracked,
        Active,
        Retiring,
        ReadyToRelease,
        ReleaseIssued
    }

    // Pure bookkeeping for a future pump adapter. Does not own or destroy a Texture,
    // stop a producer, submit render events, or establish GPU completion itself.
    // All operations must be serialized by the adapter (normally on Unity's main thread).
    internal sealed class GpuRetirementLedger
    {
        internal sealed class Generation
        {
            internal ulong Number { get; }
            internal ulong NativeId { get; }
            internal Generation(ulong number, ulong nativeId) { Number = number; NativeId = nativeId; }
        }

        internal sealed class NativeCheck
        {
            internal Generation Target { get; }
            internal ulong Submission { get; }
            internal NativeCheck(Generation target, ulong submission) { Target = target; Submission = submission; }
        }

        internal sealed class ReleasePermit
        {
            internal Generation Target { get; }
            internal ReleasePermit(Generation target) { Target = target; }
        }

        private sealed class Entry
        {
            internal GpuRetirementState State = GpuRetirementState.Active;
            internal ulong RetiredAfterSubmission;
            internal NativeCheck Check;
            internal ReleasePermit Permit;
        }

        private readonly Dictionary<Generation, Entry> entries = new Dictionary<Generation, Entry>();
        private readonly HashSet<ulong> nativeIds = new HashSet<ulong>();
        private ulong nextGeneration;
        private ulong submission;

        internal int TrackedCount => entries.Count;

        internal Generation Register(ulong nativeId)
        {
            if (nativeId == 0) throw new ArgumentOutOfRangeException(nameof(nativeId));
            if (nativeIds.Contains(nativeId)) throw new InvalidOperationException("Native target is already tracked");
            var generation = new Generation(checked(nextGeneration + 1), nativeId);
            entries.Add(generation, new Entry());
            nativeIds.Add(nativeId);
            nextGeneration = generation.Number;
            return generation;
        }

        internal GpuRetirementState StateOf(Generation generation)
        {
            return generation != null && entries.TryGetValue(generation, out var entry)
                ? entry.State : GpuRetirementState.Untracked;
        }

        internal bool RequestRetirement(Generation generation)
        {
            if (generation == null || !entries.TryGetValue(generation, out var entry)
                || entry.State != GpuRetirementState.Active) return false;
            entry.State = GpuRetirementState.Retiring;
            entry.RetiredAfterSubmission = submission;
            return true;
        }

        // Call only after the adapter successfully submits its render event.
        // A submission number is NOT a GPU fence or a completion observation.
        internal void RecordRenderEventSubmission()
        {
            submission = checked(submission + 1);
        }

        internal NativeCheck BeginNativeCheck(Generation generation)
        {
            if (generation == null || !entries.TryGetValue(generation, out var entry)
                || entry.State != GpuRetirementState.Retiring || submission <= entry.RetiredAfterSubmission) return null;
            entry.Check = new NativeCheck(generation, submission);
            return entry.Check;
        }

        // Only VrAlive == 0 for this exact, current check can authorize release.
        // Missing/failed queries are not absence. Unexpected values retain the target.
        internal bool ObserveNativeAlive(NativeCheck check, int nativeAlive)
        {
            if (check == null || !entries.TryGetValue(check.Target, out var entry)
                || entry.State != GpuRetirementState.Retiring || !ReferenceEquals(entry.Check, check)
                || check.Submission != submission) return false;
            entry.Check = null; // A reply, including an unknown value, is consumed once.
            if (nativeAlive == 0) entry.State = GpuRetirementState.ReadyToRelease;
            return true;
        }

        // One permit per generation. A failed/interrupted adapter keeps the permit
        // and the tracked entry; a timeout or shutdown must never silently drop it.
        internal ReleasePermit ClaimRelease(Generation generation)
        {
            if (generation == null || !entries.TryGetValue(generation, out var entry)
                || entry.State != GpuRetirementState.ReadyToRelease) return null;
            entry.Permit = new ReleasePermit(generation);
            entry.State = GpuRetirementState.ReleaseIssued;
            return entry.Permit;
        }

        // Adapter calls this only after its destruction request is accepted.
        // Unity's deferred Destroy has not necessarily freed the object yet.
        internal bool ConfirmRelease(ReleasePermit permit)
        {
            if (permit == null || !entries.TryGetValue(permit.Target, out var entry)
                || entry.State != GpuRetirementState.ReleaseIssued || !ReferenceEquals(entry.Permit, permit)) return false;
            entries.Remove(permit.Target);
            nativeIds.Remove(permit.Target.NativeId);
            return true;
        }

        internal Generation[] RetiringGenerations()
        {
            var result = new List<Generation>();
            foreach (var item in entries)
                if (item.Value.State == GpuRetirementState.Retiring) result.Add(item.Key);
            return result.ToArray();
        }
    }
}
