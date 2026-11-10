using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using T34400.VRWebView;
using T34400.VRWebView.Samples.Browser;
internal static class BrowserDisplayQualityTests
{
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("display modes map to mip and anisotropic sampling without changing page dimensions", () => {
            Check(!BrowserDisplayQualityPolicy.UsesMipmaps(BrowserDisplayQuality.Standard) && BrowserDisplayQualityPolicy.Anisotropy(BrowserDisplayQuality.Standard) == 1, "Standard changed");
            Check(BrowserDisplayQualityPolicy.UsesMipmaps(BrowserDisplayQuality.ReduceShimmer) && BrowserDisplayQualityPolicy.Anisotropy(BrowserDisplayQuality.ReduceShimmer) == 1, "Mip-only mode");
            Check(BrowserDisplayQualityPolicy.UsesMipmaps(BrowserDisplayQuality.ImproveObliqueViews) && BrowserDisplayQualityPolicy.Anisotropy(BrowserDisplayQuality.ImproveObliqueViews) == 4, "Oblique mode");
            bool rejected = false; try { BrowserDisplayQualityPolicy.Validate((BrowserDisplayQuality)99); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected, "Invalid public mode accepted");
        });
        yield return ("legacy and unknown saved display modes keep the standard baseline", () => {
            Check(BrowserLibrary.Sanitize(new BrowserSavedState()).displayQuality == 0, "Legacy default");
            Check(BrowserLibrary.Sanitize(new BrowserSavedState { displayQuality = -1 }).displayQuality == 0, "Negative mode");
            Check(BrowserLibrary.Sanitize(new BrowserSavedState { displayQuality = 999 }).displayQuality == 0, "Future mode");
        });
        yield return ("quality preference survives state serialization independently of tabs and placement", () => {
            var state = new BrowserSavedState { displayQuality = 2 }; state.bookmarks.Add(new BrowserSavedPage("https://example.org/", "Example"));
            var codec = new JsonSerializerOptions { IncludeFields = true };
            var restored = BrowserLibrary.Sanitize(JsonSerializer.Deserialize<BrowserSavedState>(JsonSerializer.Serialize(state, codec), codec));
            Check(restored.schema == 1 && restored.displayQuality == 2 && restored.bookmarks.Single().title == "Example", "Round trip");
        });
        yield return ("GPU plan records the native copy before each active mip generation on every frame", () => {
            var plan = new BrowserMipCommandPlan<object>(); object a = new object(), b = new object(); plan.Add(1, a); plan.Add(2, b);
            var commands = new List<string>();
            for (int frame = 0; frame < 2; frame++) {
                commands.Clear(); int generated = plan.Record(() => commands.Add("copy"), target => commands.Add(ReferenceEquals(target, a) ? "a" : "b"));
                Check(generated == 2 && commands[0] == "copy" && commands.Skip(1).ToHashSet().SetEquals(new[] { "a", "b" }), "Copy/mip ordering or stale frame");
            }
        });
        yield return ("hidden and retired generations are withdrawn from mip work before the next frame", () => {
            var plan = new BrowserMipCommandPlan<object>(); object old = new object(), current = new object(); plan.Add(1, old); plan.Remove(1); plan.Add(2, current); plan.Remove(1);
            var generated = new List<object>(); plan.Record(() => { }, generated.Add);
            Check(generated.Count == 1 && ReferenceEquals(generated[0], current), "Stale retirement withdrew the new output");
            plan.Remove(2); int copies = 0; Check(plan.Record(() => copies++, _ => throw new Exception("Hidden output sampled")) == 0 && copies == 1, "Retirement-only event lost");
        });
        yield return ("a frame cannot include targets added after its copy was recorded", () => {
            var plan = new BrowserMipCommandPlan<object>(); object old = new object(), next = new object(); plan.Add(1, old);
            int generated = plan.Record(() => { plan.Remove(1); plan.Add(2, next); }, _ => throw new Exception("Wrong frame target"));
            Check(generated == 0 && plan.Count == 1, "Reentrant target changed frame membership");
            generated = plan.Record(() => { }, target => Check(ReferenceEquals(target, next), "Next frame target")); Check(generated == 1, "New target missed");
        });
    }
}
