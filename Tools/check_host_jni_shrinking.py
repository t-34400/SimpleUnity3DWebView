"""Shrink the packaged AAR with its consumer rules and verify the Unity host JNI ABI.

This uses R8's classfile backend for inspection, not a Unity APK or Android runtime.
"""
import argparse
from pathlib import Path
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SIGNATURES = (
    "public static java.lang.String startupCase(android.app.Activity);",
    "public static void pulse(long, long, boolean, boolean);",
    "public static boolean isTrialActive();",
    "public static void start(android.app.Activity);",
    "public static void stop(android.app.Activity);",
    "public static void openNativeEditor(android.app.Activity);",
    "public static void openMatchedEditor(android.app.Activity);",
    "public static void openNativeWebView(android.app.Activity);",
    "public static void launch(android.app.Activity, boolean);",
)

VULKAN_SIGNATURES = (
    "public static void openImeGuard(android.app.Activity, long, int, int);",
    "public static void setImeWindowGuard(long, boolean);",
    "public static void openImeProxy(android.app.Activity, long, int, int);",
    "public static void setImeProxy(long, boolean);",
    "public static void requestNativeIme(long);",
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--java", default="java")
    parser.add_argument("--javap", default="javap")
    parser.add_argument("--r8", type=Path, required=True, help="R8/d8.jar from Android SDK build-tools")
    parser.add_argument("--android-jar", type=Path, required=True)
    parser.add_argument("--jdk-home", type=Path, required=True, help="Classfile backend lambda/metafactory library")
    args = parser.parse_args()
    aar = ROOT / "Packages/com.t34400.vrwebview/Runtime/Plugins/Android/vrwebview-probe.aar"
    with tempfile.TemporaryDirectory(prefix="vrwebview-host-r8-") as temp:
        work = Path(temp)
        with zipfile.ZipFile(aar) as archive:
            (work / "classes.jar").write_bytes(archive.read("classes.jar"))
            (work / "consumer.pro").write_bytes(archive.read("proguard.txt"))
        output = work / "minified.jar"
        subprocess.run([args.java, "-cp", str(args.r8), "com.android.tools.r8.R8",
                        "--release", "--classfile", "--no-desugaring",
                        "--lib", str(args.android_jar), "--lib", str(args.jdk_home),
                        "--pg-conf", str(work / "consumer.pro"),
                        "--output", str(output), str(work / "classes.jar")], check=True)
        signatures = subprocess.check_output([args.javap, "-classpath", str(output),
                                             "com.t34400.vrwebview.HostComparison"], text=True)
        for signature in SIGNATURES:
            if signature not in signatures:
                raise AssertionError("Host JNI method removed or renamed: " + signature)
        gpu = subprocess.check_output([args.javap, "-classpath", str(output),
                                       "com.t34400.vrwebview.VulkanProbe"], text=True)
        for signature in VULKAN_SIGNATURES:
            if signature not in gpu:
                raise AssertionError("Proxy JNI method removed or renamed: " + signature)
    print("PASS R8 shrinking/obfuscation retains 9 host and 5 GPU IME JNI signatures using packaged consumer rules")
    print("NOT RUN: Unity APK minification/build, Android execution, Quest IME")


if __name__ == "__main__":
    main()
