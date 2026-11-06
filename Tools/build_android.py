"""Build the Android probe and refresh the AAR in the embedded UPM package."""
import argparse
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gradle", help="Use an existing verified Gradle 8.9 executable instead of the wrapper")
    args = parser.parse_args()
    wrapper = ROOT / "Android~" / ("gradlew.bat" if sys.platform == "win32" else "gradlew")
    command = [args.gradle or str(wrapper), "--no-daemon", ":webview:assembleRelease", "--console=plain"]
    subprocess.run(command, cwd=ROOT / "Android~", check=True)
    source = ROOT / "Android~/webview/build/outputs/aar/webview-release.aar"
    target = ROOT / "Packages/com.t34400.vrwebview/Runtime/Plugins/Android/vrwebview-probe.aar"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    subprocess.run([sys.executable, str(ROOT / "Tools/generate_meta.py")], check=True)
    print("Updated", target)


if __name__ == "__main__":
    main()
