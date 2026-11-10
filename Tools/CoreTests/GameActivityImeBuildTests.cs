using System;
using System.Collections.Generic;
using T34400.VRWebView.Editor;
internal static class GameActivityImeBuildTests
{
    private const string Original = "plugins { id 'com.android.library' }\ndependencies {\n    implementation 'androidx.games:games-activity:4.4.0'\n}\n";
    private static void Check(bool ok) { if (!ok) throw new Exception("GameActivity build plan assertion failed"); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Unknown Gradle edit was accepted"); }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("GameActivity diagnostic is opt-in and preserves unmodified builds", () => {
            Check(GameActivityImeBuildPlan.Edit(Original, false) == Original);
            Check(GameActivityImeBuildPlan.Edit("unrelated build script", false) == "unrelated build script");
        });
        yield return ("GameActivity diagnostic replaces exactly one dependency and is idempotent", () => {
            string once = GameActivityImeBuildPlan.Edit(Original, true);
            Check(!once.Contains("implementation 'androidx.games:games-activity:"));
            Check(once.Contains("implementation(name:") && once.Contains("apply from:"));
            Check(GameActivityImeBuildPlan.Edit(once, true) == once);
            string quoted = GameActivityImeBuildPlan.Edit(Original.Replace("'androidx.games:games-activity:4.4.0'", "\"androidx.games:games-activity:4.4.0\""), true);
            Check(quoted == once);
        });
        yield return ("GameActivity diagnostic can be removed from a reused generated project", () => {
            string patched = GameActivityImeBuildPlan.Edit(Original, true);
            Check(GameActivityImeBuildPlan.Edit(patched, false) == Original);
            Check(GameActivityImeBuildPlan.Edit(GameActivityImeBuildPlan.Edit(patched, false), true) == patched);
        });
        yield return ("GameActivity diagnostic rejects unknown duplicate or dynamic dependencies", () => {
            Reject(() => GameActivityImeBuildPlan.Edit(Original.Replace("4.4.0", "4.4.2"), true));
            Reject(() => GameActivityImeBuildPlan.Edit(Original.Replace("4.4.0", "4.+"), true));
            Reject(() => GameActivityImeBuildPlan.Edit(Original + Original, true));
            Reject(() => GameActivityImeBuildPlan.Edit("", true));
        });
        yield return ("GameActivity diagnostic refuses partial and externally changed integration blocks", () => {
            string patched = GameActivityImeBuildPlan.Edit(Original, true);
            Reject(() => GameActivityImeBuildPlan.Edit(patched.Replace("// END VRWEBVIEW GAME IME DIAGNOSTIC", ""), false));
            Reject(() => GameActivityImeBuildPlan.Edit(patched.Replace("apply from:", "other from:"), true));
            Reject(() => GameActivityImeBuildPlan.Edit(patched + "// END VRWEBVIEW GAME IME DIAGNOSTIC", true));
        });
    }
}
