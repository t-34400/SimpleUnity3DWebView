"""Build the Android probe and refresh the AAR in the embedded UPM package."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gradle", help="Use an existing verified Gradle 8.9 executable instead of the wrapper")
    parser.add_argument("--offline", action="store_true", help="Use already cached Gradle dependencies")
    args = parser.parse_args()
    wrapper = ROOT / "Android~" / ("gradlew.bat" if sys.platform == "win32" else "gradlew")
    command = [args.gradle or str(wrapper), "--no-daemon", ":webview:assembleRelease", ":webview:lintRelease", "--console=plain"]
    if args.offline:
        command.append("--offline")
    subprocess.run(command, cwd=ROOT / "Android~", check=True)
    source = ROOT / "Android~/webview/build/outputs/aar/webview-release.aar"
    target = ROOT / "Packages/com.t34400.vrwebview/Runtime/Plugins/Android/vrwebview-probe.aar"
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    inputs = {}
    for path in sorted((ROOT / "Android~").rglob("*")):
        rel = path.relative_to(ROOT / "Android~")
        if not path.is_file() or any(p in {"build", ".gradle"} for p in rel.parts):
            continue
        if path.name == "local.properties":
            continue
        inputs[path.relative_to(ROOT).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
    provenance = {"schemaVersion": 1, "aarSha256": hashlib.sha256(target.read_bytes()).hexdigest(),
                  "sources": inputs, "runtimeValidation": "Not performed by the build"}
    target.with_suffix(".build.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    subprocess.run([sys.executable, str(ROOT / "Tools/generate_meta.py")], check=True)
    print("Updated", target)


if __name__ == "__main__":
    main()
