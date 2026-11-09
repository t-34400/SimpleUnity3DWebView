"""Build the Android runtime and probes and refresh the AAR in the embedded UPM package."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gradle", help="Use an existing verified Gradle 8.9 executable instead of the wrapper")
    parser.add_argument("--ndk", help="Android NDK r27c path (or ANDROID_NDK_HOME)")
    parser.add_argument("--cmake", help="CMake executable (default SDK CMake 3.22.1)")
    parser.add_argument("--offline", action="store_true", help="Use already cached Gradle dependencies")
    args = parser.parse_args()
    sdk = Path(os.environ.get("ANDROID_HOME", os.environ.get("ANDROID_SDK_ROOT", "")))
    ndk = Path(args.ndk or os.environ.get("ANDROID_NDK_HOME", str(sdk / "ndk/27.2.12479018")))
    cmake = args.cmake or str(sdk / "cmake/3.22.1/bin" / ("cmake.exe" if sys.platform == "win32" else "cmake"))
    if not (ndk / "build/cmake/android.toolchain.cmake").is_file():
        parser.error("Install NDK 27.2.12479018 or pass --ndk")
    ndk_revision = next((line.split('=', 1)[1].strip() for line in (ndk / 'source.properties').read_text().splitlines()
                         if line.startswith('Pkg.Revision')), 'unknown')
    # Native build intermediates must not enter patches or provenance.
    with tempfile.TemporaryDirectory(prefix="vrwebview-native-") as native_build:
        subprocess.run([cmake, "-S", str(ROOT / "Android~/webview/src/main/cpp"), "-B", native_build,
                        "-G", "Ninja", "-DCMAKE_BUILD_TYPE=Release", "-DANDROID_ABI=arm64-v8a",
                        "-DANDROID_PLATFORM=android-29", "-DANDROID_STL=c++_static",
                        "-DCMAKE_TOOLCHAIN_FILE=" + str(ndk / "build/cmake/android.toolchain.cmake"),
                        "-DCMAKE_MAKE_PROGRAM=" + str(Path(cmake).with_name("ninja.exe" if sys.platform == "win32" else "ninja"))], check=True)
        subprocess.run([cmake, "--build", native_build], check=True)
        prebuilt = "windows-x86_64" if sys.platform == "win32" else "darwin-x86_64" if sys.platform == "darwin" else "linux-x86_64"
        strip = ndk / "toolchains/llvm/prebuilt" / prebuilt / "bin" / ("llvm-strip.exe" if sys.platform == "win32" else "llvm-strip")
        subprocess.run([str(strip), "--strip-debug", str(Path(native_build) / "libGfxPluginVRWebView.so")], check=True)
        target_native = ROOT / "Packages/com.t34400.vrwebview/Runtime/Plugins/Android/arm64-v8a/libGfxPluginVRWebView.so"
        target_native.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(Path(native_build) / "libGfxPluginVRWebView.so", target_native)
    wrapper = ROOT / "Android~" / ("gradlew.bat" if sys.platform == "win32" else "gradlew")
    command = [args.gradle or str(wrapper), "--no-daemon", ":runtime:assembleRelease", ":runtime:lintRelease", ":webview:assembleRelease", ":webview:lintRelease", "--console=plain"]
    if args.offline:
        command.append("--offline")
    # Keep machine-local Gradle state outside the transferable source workspace.
    with tempfile.TemporaryDirectory(prefix="vrwebview-gradle-") as project_cache:
        subprocess.run([*command, "--project-cache-dir", project_cache], cwd=ROOT / "Android~", check=True)
    source = ROOT / "Android~/webview/build/outputs/aar/webview-release.aar"
    target = ROOT / "Packages/com.t34400.vrwebview/Runtime/Plugins/Android/vrwebview-probe.aar"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    runtime_source = ROOT / "Android~/runtime/build/outputs/aar/runtime-release.aar"
    runtime_target = target.with_name("vrwebview-runtime.aar")
    shutil.copy2(runtime_source, runtime_target)
    inputs = {}
    for path in sorted((ROOT / "Android~").rglob("*")):
        rel = path.relative_to(ROOT / "Android~")
        if not path.is_file() or any(p in {"build", ".gradle"} for p in rel.parts):
            continue
        if path.name == "local.properties":
            continue
        inputs[path.relative_to(ROOT).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    provenance = {"schemaVersion": 1, "aarSha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                  "sources": inputs, "nativeLibrary": {"path": target_native.relative_to(ROOT).as_posix(),
                                    "sha256": hashlib.sha256(target_native.read_bytes()).hexdigest(),
                                    "abi": "arm64-v8a", "ndk": ndk_revision},
                  "runtimeValidation": "Not performed by the build"}
    for artifact in (target, runtime_target):
        record = dict(provenance, aarSha256=hashlib.sha256(artifact.read_bytes()).hexdigest())
        artifact.with_suffix(".build.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    subprocess.run([sys.executable, str(ROOT / "Tools/generate_meta.py")], check=True)
    print("Updated", target)


if __name__ == "__main__":
    main()
