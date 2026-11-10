#if UNITY_ANDROID
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace T34400.VRWebView.Editor
{
    /// <summary>Opt-in version-checked compatibility integration, outside runtime/browser assemblies.</summary>
    internal sealed class GameActivityImeCompatPostprocess : IPostGenerateGradleAndroidProject, IPreprocessBuildWithReport
    {
        private const string Menu = "VRWebView/Compatibility/GameActivity IME/Enable Patch";
        private static string Key => "com.t34400.vrwebview.gameime.diagnostic:" + Application.dataPath;
        internal static bool Enabled => EditorPrefs.GetBool(Key, false);
        public int callbackOrder => 100002;

        [MenuItem(Menu)]
        private static void Toggle()
        {
            EditorPrefs.SetBool(Key, !Enabled);
            Debug.Log("VRWebView GameActivity IME compatibility patch " + (Enabled ? "enabled" : "disabled")
                + ". Rebuild your Android player. The setting is local to this project.");
        }
        [MenuItem(Menu, true)]
        private static bool Check() { UnityEditor.Menu.SetChecked(Menu, Enabled); return true; }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (Enabled && report.summary.platform == BuildTarget.Android
                && PlayerSettings.Android.applicationEntry != AndroidApplicationEntry.GameActivity)
                throw new BuildFailedException("VRWebView GameActivity IME compatibility requires GameActivity. Disable the compatibility patch for other entry points.");
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string script = Path.Combine(path, "build.gradle");
            if (!File.Exists(script)) {
                if (Enabled) throw new BuildFailedException("VRWebView IME compatibility could not find unityLibrary/build.gradle.");
                return;
            }
            string before = File.ReadAllText(script);
            string source;
            try { source = GameActivityImeBuildPlan.Edit(before, Enabled); }
            catch (InvalidOperationException error) { throw new BuildFailedException(error.Message); }
            string installed = Path.Combine(path, "libs/games-activity-4.4.0-vrwebview-ime.aar");
            if (!Enabled) {
                if (source != before) File.WriteAllText(script, source);
                // This exact file is owned by the compatibility integration in generated output.
                if (File.Exists(installed)) { MakeWritable(installed); File.Delete(installed); }
                return;
            }
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GameActivityImeCompatPostprocess).Assembly);
            if (package == null) throw new BuildFailedException("VRWebView IME compatibility could not locate its UPM package.");
            string assets = Path.Combine(package.resolvedPath, "Editor/GameActivityImeCompat");
            string aar = Path.Combine(assets, "games-activity-4.4.0-vrwebview-ime.aar");
            var record = JsonUtility.FromJson<BuildRecord>(File.ReadAllText(Path.ChangeExtension(aar, ".build.json")));
            if (record == null || record.googleCoordinate != "androidx.games:games-activity:4.4.0"
                || record.originalAarSha256 != "3a8b0887ac55a0a6ab56021ced7353a250539eac811278b74bccd8a5b678d9c9"
                || Hash(aar) != record.aarSha256)
                throw new BuildFailedException("VRWebView IME compatibility AAR/provenance check failed. Restore the package before building.");
            string output = Path.Combine(path, "vrwebview-gameime");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(Path.GetDirectoryName(installed));
            CopyGenerated(aar, installed);
            CopyGenerated(Path.Combine(assets, "apply.gradle"), Path.Combine(output, "apply.gradle"));
            File.WriteAllText(script, source);
            Debug.Log("VRWebView IME compatibility: patched Google 4.4.0 integration generated; original AAR hash will also be checked by Gradle. Rebuild and check text input on the target device.");
        }

        private static void MakeWritable(string path)
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
        private static void CopyGenerated(string source, string target)
        {
            if (File.Exists(target)) MakeWritable(target);
            File.Copy(source, target, true);
            MakeWritable(target);
        }
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable] private sealed class BuildRecord
        {
            public string googleCoordinate, originalAarSha256, aarSha256;
        }
    }
}
#endif
