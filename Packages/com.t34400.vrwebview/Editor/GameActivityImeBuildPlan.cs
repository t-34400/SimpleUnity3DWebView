using System;
using System.Text.RegularExpressions;

namespace T34400.VRWebView.Editor
{
    /// <summary>Pure generated-Gradle edit plan, verified without starting Unity.</summary>
    internal static class GameActivityImeBuildPlan
    {
        private const string Begin = "// BEGIN VRWEBVIEW GAME IME DIAGNOSTIC";
        private const string End = "// END VRWEBVIEW GAME IME DIAGNOSTIC";
        private const string Original = "implementation 'androidx.games:games-activity:4.4.0'";
        private const string Replacement = "implementation(name: 'games-activity-4.4.0-vrwebview-ime', ext: 'aar')";
        private const string Apply = "apply from: 'vrwebview-gameime/apply.gradle'";

        public static string Edit(string source, bool enabled)
        {
            int begin = source.IndexOf(Begin, StringComparison.Ordinal), end = source.IndexOf(End, StringComparison.Ordinal);
            if (begin >= 0 || end >= 0) {
                if (begin < 0 || end < begin || source.IndexOf(Begin, begin + Begin.Length, StringComparison.Ordinal) >= 0
                    || source.IndexOf(End, end + End.Length, StringComparison.Ordinal) >= 0
                    || source.Substring(begin + Begin.Length, end - begin - Begin.Length).Trim() != Apply
                    || Regex.Matches(source, Regex.Escape(Replacement)).Count != 1)
                    throw new InvalidOperationException("VRWebView IME diagnostic found an incomplete or changed generated integration block.");
                source = source.Remove(begin, end + End.Length - begin).Replace(Replacement, Original).TrimEnd('\r', '\n') + "\n";
            } else if (source.IndexOf(Replacement, StringComparison.Ordinal) >= 0) {
                throw new InvalidOperationException("VRWebView IME diagnostic found an unmarked replacement dependency.");
            }
            if (!enabled) return source;
            var declarations = Regex.Matches(source, @"\bimplementation\s+['""]androidx\.games:games-activity:([^'""\s]+)['""]");
            if (declarations.Count != 1 || declarations[0].Groups[1].Value != "4.4.0")
                throw new InvalidOperationException("VRWebView IME diagnostic supports exactly one explicit games-activity:4.4.0 dependency. Disable the diagnostic or review the changed dependency.");
            var match = declarations[0];
            source = source.Remove(match.Index, match.Length).Insert(match.Index, Replacement);
            return source.TrimEnd('\r', '\n') + "\n" + Begin + "\n" + Apply + "\n" + End + "\n";
        }
    }
}
