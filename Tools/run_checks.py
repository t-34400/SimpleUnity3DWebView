"""Run executable core tests and package integrity checks; no Unity/Android runtime claim."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "Packages/com.t34400.vrwebview"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="vrwebview-checks-") as temp:
        artifacts = Path(temp) / "dotnet"
        subprocess.run([args.dotnet, "build", str(ROOT / "Tools/CoreTests/CoreTests.csproj"),
                        "--artifacts-path", str(artifacts), "--ignore-failed-sources", "-v", "minimal"], check=True)
        subprocess.run([args.dotnet, str(artifacts / "bin/CoreTests/debug/CoreTests.dll")], check=True)
        subprocess.run([sys.executable, str(ROOT / "Tools/export_unitypackage.py"), "--output", str(Path(temp) / "VRWebView.unitypackage")], check=True)
    ids = set()
    for path in [PACKAGE, *PACKAGE.rglob("*")]:
        if path.suffix == ".meta":
            continue
        meta = path.with_name(path.name + ".meta")
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M)
        if not match or match[1] in ids:
            raise ValueError("Invalid/duplicate GUID: " + str(meta))
        ids.add(match[1])
        if path.suffix in {".asmdef", ".json"}:
            json.loads(path.read_text())
    aar = PACKAGE / "Runtime/Plugins/Android/vrwebview-probe.aar"
    provenance = json.loads(aar.with_suffix(".build.json").read_text())
    assert provenance["aarSha256"] == hashlib.sha256(aar.read_bytes()).hexdigest()
    for name, expected in provenance["sources"].items():
        assert hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == expected, "Stale AAR: " + name
    with zipfile.ZipFile(aar) as archive:
        assert archive.testzip() is None
        assert "assets/vrwebview-ime-probe.html" in archive.namelist()
        assert "classes.jar" in archive.namelist()
        assert "proguard.txt" in archive.namelist()
    print("PASS package GUIDs, JSON, AAR contents, and unitypackage byte round trip")
    print("NOT RUN: Unity Editor tests/import/build; Quest IME, focus, rendering and performance")


if __name__ == "__main__":
    main()
