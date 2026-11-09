using System;
using System.Collections.Generic;
using T34400.VRWebView;
internal static class UnityImeShowGateTests
{
    private static void Check(bool value) { if (!value) throw new Exception("IME order gate assertion failed"); }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("IME order probe claims at most one show after two stable reads", () => {
            var gate = new UnityImeShowGate(0);
            Check(gate.TryRead(0)); gate.Observe(0, true, 1, 0x80001, 0x2000006); Check(!gate.ClaimShow());
            Check(gate.TryRead(.1)); gate.Observe(.1, true, 1, 0x80001, 0x2000006); Check(gate.ClaimShow());
            Check(!gate.ClaimShow() && !gate.TryRead(1) && gate.Claimed);
        });
        yield return ("IME order probe never shows on TYPE_NULL or a broken readiness sequence", () => {
            var gate = new UnityImeShowGate(0);
            Check(gate.TryRead(0)); gate.Observe(0, false, 1, 0, 0); Check(!gate.ClaimShow());
            Check(gate.TryRead(.1)); gate.Observe(.1, true, 1, 1, 6); Check(!gate.ClaimShow());
            Check(gate.TryRead(.2)); gate.Observe(.2, false, 1, 1, 6); Check(!gate.ClaimShow());
            Check(gate.TryRead(.3)); gate.Observe(.3, true, 1, 1, 6); Check(!gate.ClaimShow());
            Check(gate.TryRead(.4)); gate.Observe(.4, true, 1, 1, 6); Check(gate.ClaimShow());
        });
        yield return ("IME order probe requires the same view and editor fingerprint", () => {
            for (int changed = 0; changed < 3; changed++) {
                var gate = new UnityImeShowGate(0);
                Check(gate.TryRead(0)); gate.Observe(0, true, 1, 1, 6);
                Check(gate.TryRead(.1)); gate.Observe(.1, true, changed == 0 ? 2 : 1, changed == 1 ? 0x80001 : 1, changed == 2 ? 0x2000006 : 6);
                Check(!gate.ClaimShow());
            }
        });
        yield return ("IME order probe rejects duplicate or unsolicited observations", () => {
            var gate = new UnityImeShowGate(0);
            gate.Observe(0, true, 1, 1, 6); Check(!gate.ClaimShow());
            Check(gate.TryRead(0) && !gate.TryRead(.1)); gate.Observe(.1, true, 1, 1, 6);
            gate.Observe(.2, true, 1, 1, 6); Check(!gate.ClaimShow());
        });
        yield return ("IME order probe cancellation discards delayed replies", () => {
            var gate = new UnityImeShowGate(0);
            Check(gate.TryRead(0)); gate.Observe(0, true, 1, 1, 6); Check(gate.TryRead(.1));
            gate.Cancel(); gate.Observe(.2, true, 1, 1, 6);
            Check(gate.Finished && !gate.Ready && !gate.ClaimShow() && !gate.TryRead(1));
        });
        yield return ("IME order probe times out without issuing or replaying a show", () => {
            var gate = new UnityImeShowGate(10);
            Check(gate.TryRead(10)); gate.Observe(10, true, 1, 1, 6); Check(!gate.TryRead(10.01));
            Check(gate.TryRead(10.1)); gate.Observe(12, true, 1, 1, 6);
            Check(gate.TimedOut && !gate.ClaimShow() && !gate.TryRead(13));
        });
    }
}
