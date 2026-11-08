using System;
using System.Collections.Generic;
using System.Linq;
using T34400.VRWebView;

// These scenarios supply synthetic native observations; no GPU, Unity or JNI is run.
internal static class GpuRetirementTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    private static GpuRetirementLedger.NativeCheck NextCheck(GpuRetirementLedger ledger, GpuRetirementLedger.Generation generation)
    {
        ledger.RecordRenderEventSubmission();
        var check = ledger.BeginNativeCheck(generation);
        Check(check != null, "Retiring generation was not queryable after submission");
        return check;
    }

    private static GpuRetirementLedger.ReleasePermit PrepareRelease(GpuRetirementLedger ledger, GpuRetirementLedger.Generation generation)
    {
        Check(ledger.RequestRetirement(generation), "Retirement request missing");
        Check(ledger.ObserveNativeAlive(NextCheck(ledger, generation), 0), "Native absence was not accepted");
        var permit = ledger.ClaimRelease(generation);
        Check(permit != null, "Release permit missing");
        return permit;
    }

    internal static IEnumerable<(string Name, Action Run)> Scenarios()
    {
        yield return ("GPU failed native creation cannot register a generation", () => {
            var ledger = new GpuRetirementLedger();
            Reject<ArgumentOutOfRangeException>(() => ledger.Register(0));
            Check(ledger.TrackedCount == 0 && ledger.StateOf(null) == GpuRetirementState.Untracked, "Failed creation retained a native owner");
            Check(ledger.ClaimRelease(null) == null && !ledger.ConfirmRelease(null), "An unknown resource got release authority");
        });
        yield return ("GPU active targets cannot be released by a render submission alone", () => {
            var ledger = new GpuRetirementLedger(); var active = ledger.Register(17);
            ledger.RecordRenderEventSubmission();
            Check(ledger.BeginNativeCheck(active) == null && !ledger.ObserveNativeAlive(null, 0), "Active target accepted a retirement query");
            Check(ledger.ClaimRelease(active) == null && ledger.StateOf(active) == GpuRetirementState.Active, "Active target was released");
            Check(ledger.RetiringGenerations().Length == 0 && ledger.TrackedCount == 1, "Active ownership disappeared");
        });
        yield return ("GPU close request and arbitrarily many live observations retain the target", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(19);
            ledger.RequestRetirement(generation);
            for (int pass = 0; pass < 100; pass++) {
                Check(ledger.ObserveNativeAlive(NextCheck(ledger, generation), 1), "Live observation rejected");
                Check(ledger.ClaimRelease(generation) == null && ledger.TrackedCount == 1, "Elapsed pump passes released an in-use target");
            }
            Check(ledger.StateOf(generation) == GpuRetirementState.Retiring, "Close was confused with producer/GPU completion");
        });
        yield return ("GPU retirement requires a subsequent successful render-event submission", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(23);
            ledger.RecordRenderEventSubmission(); ledger.RequestRetirement(generation);
            Check(ledger.BeginNativeCheck(generation) == null, "A pre-retirement submission authorized a new check");
            Check(ledger.ClaimRelease(generation) == null, "Missing render event was treated as completion");
            Check(ledger.ObserveNativeAlive(NextCheck(ledger, generation), 0), "Post-retirement native absence rejected");
            Check(ledger.StateOf(generation) == GpuRetirementState.ReadyToRelease, "Native absence did not authorize release");
        });
        yield return ("GPU native absence issues one release permit and keeps ownership until acknowledgement", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(29);
            var permit = PrepareRelease(ledger, generation);
            Check(ReferenceEquals(permit.Target, generation), "Release selected the wrong generation");
            Check(ledger.StateOf(generation) == GpuRetirementState.ReleaseIssued && ledger.TrackedCount == 1, "Claim silently dropped resource ownership");
            Check(ledger.ClaimRelease(generation) == null, "Duplicate release permit");
            Check(ledger.ConfirmRelease(permit), "Destruction acknowledgement rejected");
            Check(!ledger.ConfirmRelease(permit) && ledger.TrackedCount == 0, "Destruction acknowledgement replayed");
        });
        yield return ("GPU delayed native result from an earlier pump pass cannot authorize release", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(31);
            ledger.RequestRetirement(generation); var delayed = NextCheck(ledger, generation);
            ledger.RecordRenderEventSubmission();
            Check(!ledger.ObserveNativeAlive(delayed, 0), "Old pass authorized destruction");
            Check(ledger.StateOf(generation) == GpuRetirementState.Retiring, "Delayed reply changed ownership");
            var current = ledger.BeginNativeCheck(generation);
            Check(ledger.ObserveNativeAlive(current, 0) && ledger.ClaimRelease(generation) != null, "Fresh native check did not recover");
        });
        yield return ("GPU replaced query in the same pass invalidates the previous reply", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(37);
            ledger.RequestRetirement(generation); var old = NextCheck(ledger, generation);
            var current = ledger.BeginNativeCheck(generation);
            Check(!ledger.ObserveNativeAlive(old, 0), "Replaced native query authorized destruction");
            Check(ledger.ObserveNativeAlive(current, 1), "Current live observation rejected");
            Check(ledger.ClaimRelease(generation) == null, "Live target was made releasable");
        });
        yield return ("GPU unexpected native status and duplicate replies do not mean absence", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(41);
            ledger.RequestRetirement(generation);
            foreach (int value in new[] { -1, 2, int.MaxValue, 1 }) {
                var check = NextCheck(ledger, generation);
                Check(ledger.ObserveNativeAlive(check, value), "Current reply was not consumed");
                Check(!ledger.ObserveNativeAlive(check, 0), "A consumed reply was changed into absence");
                Check(ledger.StateOf(generation) == GpuRetirementState.Retiring && ledger.ClaimRelease(generation) == null, "Unknown or live status freed a resource");
            }
        });
        yield return ("GPU generations retire out of order while a replacement stays active", () => {
            var ledger = new GpuRetirementLedger(); var first = ledger.Register(43); var second = ledger.Register(47);
            ledger.RequestRetirement(first); ledger.RequestRetirement(second); var replacement = ledger.Register(53);
            ledger.RecordRenderEventSubmission();
            var firstCheck = ledger.BeginNativeCheck(first); var secondCheck = ledger.BeginNativeCheck(second);
            Check(ledger.ObserveNativeAlive(secondCheck, 0) && ledger.ObserveNativeAlive(firstCheck, 1), "Interleaved native observations failed");
            Check(ledger.ConfirmRelease(ledger.ClaimRelease(second)), "Second generation could not finish first");
            Check(ledger.StateOf(first) == GpuRetirementState.Retiring && ledger.StateOf(replacement) == GpuRetirementState.Active, "One generation modified another");
            Check(ledger.RetiringGenerations().Single() == first && ledger.TrackedCount == 2, "Pending generation was lost");
        });
        yield return ("GPU duplicate close preserves an outstanding check and never repeats a release", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(59);
            ledger.RequestRetirement(generation); var check = NextCheck(ledger, generation);
            Check(!ledger.RequestRetirement(generation), "Close request accepted twice");
            Check(ledger.ObserveNativeAlive(check, 0), "Duplicate close invalidated a valid native check");
            Check(!ledger.RequestRetirement(generation), "Ready generation returned to retiring");
            var permit = ledger.ClaimRelease(generation);
            Check(!ledger.RequestRetirement(generation) && ledger.ConfirmRelease(permit), "Duplicate close affected a claimed release");
            Check(!ledger.RequestRetirement(generation) && ledger.ClaimRelease(generation) == null, "Released generation became current again");
        });
        yield return ("GPU native ID reuse cannot revive old generation callbacks or release permits", () => {
            var ledger = new GpuRetirementLedger(); var old = ledger.Register(61);
            ledger.RequestRetirement(old); var oldCheck = NextCheck(ledger, old);
            ledger.ObserveNativeAlive(oldCheck, 0); var oldPermit = ledger.ClaimRelease(old); ledger.ConfirmRelease(oldPermit);
            var current = ledger.Register(61);
            Check(current.Number > old.Number && !ReferenceEquals(current, old), "Managed generation was reused");
            Check(!ledger.RequestRetirement(old) && !ledger.ObserveNativeAlive(oldCheck, 0) && !ledger.ConfirmRelease(oldPermit), "Old callback affected a reused native ID");
            Check(ledger.StateOf(old) == GpuRetirementState.Untracked && ledger.StateOf(current) == GpuRetirementState.Active && ledger.TrackedCount == 1, "Replacement ownership was lost");
        });
        yield return ("GPU foreign ledger handles and replies cannot affect identical native IDs", () => {
            var left = new GpuRetirementLedger(); var right = new GpuRetirementLedger();
            var own = left.Register(67); var other = right.Register(67);
            left.RequestRetirement(own); right.RequestRetirement(other);
            var leftCheck = NextCheck(left, own); var rightCheck = NextCheck(right, other);
            Check(left.StateOf(other) == GpuRetirementState.Untracked && !left.RequestRetirement(other), "Foreign generation was accepted");
            Check(!left.ObserveNativeAlive(rightCheck, 0), "Foreign reply was accepted");
            right.ObserveNativeAlive(rightCheck, 0); var foreignPermit = right.ClaimRelease(other);
            Check(!left.ConfirmRelease(foreignPermit) && left.ClaimRelease(other) == null, "Foreign permit was accepted");
            Check(left.ObserveNativeAlive(leftCheck, 0) && left.ClaimRelease(own) != null, "Rejected foreign callback damaged the valid check");
        });
        yield return ("GPU failed or interrupted destruction keeps its claimed generation tracked", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(71);
            var permit = PrepareRelease(ledger, generation);
            for (int i = 0; i < 20; i++) ledger.RecordRenderEventSubmission();
            Check(ledger.TrackedCount == 1 && ledger.StateOf(generation) == GpuRetirementState.ReleaseIssued, "Unacknowledged resource disappeared");
            Reject<InvalidOperationException>(() => ledger.Register(71));
            Check(ledger.ClaimRelease(generation) == null && ledger.BeginNativeCheck(generation) == null, "Failure created another destruction request");
            Check(ledger.ConfirmRelease(permit), "Retained original permit could not be acknowledged");
        });
        yield return ("GPU duplicate native ID cannot overwrite any tracked ownership phase", () => {
            var ledger = new GpuRetirementLedger(); var generation = ledger.Register(73);
            Reject<InvalidOperationException>(() => ledger.Register(73));
            ledger.RequestRetirement(generation); Reject<InvalidOperationException>(() => ledger.Register(73));
            ledger.ObserveNativeAlive(NextCheck(ledger, generation), 0); Reject<InvalidOperationException>(() => ledger.Register(73));
            var permit = ledger.ClaimRelease(generation); Reject<InvalidOperationException>(() => ledger.Register(73));
            Check(ledger.TrackedCount == 1 && ledger.ConfirmRelease(permit), "Duplicate registration damaged retained ownership");
        });
        yield return ("GPU retirement snapshots cannot erase or replace ledger ownership", () => {
            var ledger = new GpuRetirementLedger(); var first = ledger.Register(79); var second = ledger.Register(83);
            ledger.RequestRetirement(first); ledger.RequestRetirement(second);
            var snapshot = ledger.RetiringGenerations(); snapshot[0] = null;
            var fresh = ledger.RetiringGenerations();
            Check(fresh.Length == 2 && fresh.Contains(first) && fresh.Contains(second), "Snapshot mutation modified the owner");
            Check(ledger.TrackedCount == 2, "Snapshot mutation removed a resource");
        });
    }
}
