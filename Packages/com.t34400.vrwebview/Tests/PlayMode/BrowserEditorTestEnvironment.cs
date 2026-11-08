#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Management;

namespace T34400.VRWebView.Tests
{
    // Only the CLI's explicit package PlayMode invocation opts into this environment.
    // Keep serialized project/Android settings intact: suspend the Editor runtime reference.
    [InitializeOnLoad]
    internal static class BrowserEditorTestEnvironment
    {
        internal const string Argument = "-vrwebviewEditorTestsWithoutXR";
        internal const string VerifiedMessage = "VRWEBVIEW_EDITOR_TESTS: XR auto-start isolation verified before scene load";
        private static XRGeneralSettings previous;
        private static bool suspended;

        static BrowserEditorTestEnvironment()
        {
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.quitting += Restore;
        }

        private static bool Requested()
        {
            if (!Application.isBatchMode) return false;
            var args = Environment.GetCommandLineArgs();
            return Array.IndexOf(args, Argument) >= 0 && Array.IndexOf(args, "-runTests") >= 0
                && Value(args, "-testPlatform") == "PlayMode"
                && Value(args, "-assemblyNames") == "T34400.VRWebView.PlayMode.Tests";
        }

        private static string Value(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        // XR Management 4.5.2 initializes at AfterAssembliesLoaded and starts at
        // BeforeSplashScreen. SubsystemRegistration runs before both callbacks.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Suspend()
        {
            if (!Requested()) return;
            if (!suspended) { previous = XRGeneralSettings.Instance; suspended = true; }
            XRGeneralSettings.Instance = null;
            Debug.Log("VRWEBVIEW_EDITOR_TESTS: XR runtime reference suspended before automatic initialization");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Verify()
        {
            if (!Requested()) return;
            if (!suspended || XRGeneralSettings.Instance != null)
                throw new InvalidOperationException("VRWebView Editor test XR isolation was not maintained");
            Debug.Log(VerifiedMessage);
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) Restore();
        }

        private static void Restore()
        {
            if (!suspended) return;
            // Never overwrite a newer reference installed by a different owner.
            if (XRGeneralSettings.Instance == null) XRGeneralSettings.Instance = previous;
            previous = null;
            suspended = false;
        }
    }
}
#endif
