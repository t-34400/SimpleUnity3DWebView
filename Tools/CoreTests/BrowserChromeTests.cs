using System;
using System.Collections.Generic;
using T34400.VRWebView.Samples.Browser;
internal static class BrowserChromeTests {
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("popup opens under its right-aligned toolbar anchor", () => {
            var rect = BrowserPopupGeometry.Place(1280, 1000, 1200, 144, 350, 500, true);
            Check(rect.X == 850 && rect.Y == 150 && rect.Width == 350 && rect.Height == 500, "Toolbar anchor");
        });
        yield return ("context popup flips above an anchor near the bottom edge", () => {
            var rect = BrowserPopupGeometry.Place(800, 600, 700, 560, 350, 220, false);
            Check(rect.X == 442 && rect.Y == 334, "Context popup escaped window");
        });
        yield return ("short windows keep the menu below its toolbar and scroll excess content", () => {
            var rect = BrowserPopupGeometry.Place(800, 600, 780, 144, 350, 560, true);
            Check(rect.Y == 150 && rect.Height == 442 && rect.Y + rect.Height == 592, "Dropdown covered its toolbar");
        });
        yield return ("oversized popup content stays inside the window", () => {
            var rect = BrowserPopupGeometry.Place(640, 480, -100, -100, 1000, 1000, true);
            Check(rect.X == 8 && rect.Y == 8 && rect.Width == 624 && rect.Height == 464, "Popup limits");
        });
        yield return ("popup rejects nonfinite geometry instead of losing its dismiss target", () => {
            bool rejected = false; try { BrowserPopupGeometry.Place(800, 600, float.NaN, 1, 100, 100, false); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid popup accepted");
        });
        yield return ("saving an existing bookmark edits it without toggling removal", () => {
            var library = new BrowserLibrary(); library.SaveBookmark("https://EXAMPLE.org:443/", "Original");
            library.SaveBookmark("https://example.org/", "Renamed");
            Check(library.Data.bookmarks.Count == 1 && library.FindBookmark("https://example.org/").title == "Renamed", "Bookmark duplicated or deleted");
            Check(library.RemoveBookmark("https://example.org/") && !library.RemoveBookmark("https://example.org/"), "Explicit remove");
        });
        yield return ("bookmark editor bounds names and rejects non-web targets", () => {
            var library = new BrowserLibrary(); library.SaveBookmark("https://example.org/", new string('x', 300));
            Check(library.FindBookmark("https://example.org/").title.Length == 256, "Name unbounded");
            Check(!library.SaveBookmark("javascript:bad", "Bad") && library.Data.bookmarks.Count == 1, "Non-web bookmark");
        });
        yield return ("old sessions keep the default fixed tilt without a schema bump", () => {
            var state = BrowserLibrary.Sanitize(new BrowserSavedState { poseVersion = 0, fixedTiltDegrees = 0 });
            Check(state.poseVersion == 0 && state.fixedTiltDegrees == 5 && state.schema == 1, "Legacy pose default");
        });
        yield return ("saved fixed tilt preserves zero and clamps invalid inputs", () => {
            var zero = BrowserLibrary.Sanitize(new BrowserSavedState { poseVersion = 1, fixedTiltDegrees = 0 });
            var high = BrowserLibrary.Sanitize(new BrowserSavedState { poseVersion = 1, fixedTiltDegrees = 100 });
            var invalid = BrowserLibrary.Sanitize(new BrowserSavedState { poseVersion = 1, fixedTiltDegrees = float.NaN });
            Check(zero.fixedTiltDegrees == 0 && high.fixedTiltDegrees == 10 && invalid.fixedTiltDegrees == 5, "Saved tilt");
        });
    }
}
