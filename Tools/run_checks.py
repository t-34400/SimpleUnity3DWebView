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
from check_layer_boundaries import check as check_layers
from check_distribution import check as check_distribution, DEVELOPMENT
from build_upm import build as build_upm

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "Packages/com.t34400.vrwebview"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    print(json.dumps(check_layers(PACKAGE)))
    print(json.dumps(check_distribution()))
    with tempfile.TemporaryDirectory(prefix="vrwebview-checks-") as temp:
        artifacts = Path(temp) / "dotnet"
        subprocess.run([args.dotnet, "build", str(ROOT / "Tools/CoreTests/CoreTests.csproj"),
                        "--artifacts-path", str(artifacts), "--ignore-failed-sources", "-v", "minimal"], check=True)
        subprocess.run([args.dotnet, str(artifacts / "bin/CoreTests/debug/CoreTests.dll")], check=True)
        subprocess.run([args.dotnet, "build", str(ROOT / "Tools/UnitySyntaxChecks/UnitySyntaxChecks.csproj"),
                        "--artifacts-path", str(artifacts), "--ignore-failed-sources", "-v", "minimal"], check=True)
        syntax = [args.dotnet, str(artifacts / "bin/UnitySyntaxChecks/debug/UnitySyntaxChecks.dll")]
        subprocess.run([*syntax, str(PACKAGE)], check=True)
        subprocess.run([*syntax, str(DEVELOPMENT)], check=True)
        fixture = Path(temp) / "syntax-fixture"
        fixture.mkdir()
        if subprocess.run([*syntax, str(fixture)], capture_output=True).returncode != 1:
            raise ValueError("Empty syntax scan was accepted")
        (fixture / "Invalid.cs").write_text("class Invalid {", encoding="utf-8")
        if subprocess.run([*syntax, str(fixture)], capture_output=True).returncode != 1:
            raise ValueError("Invalid C# syntax was accepted")
        for symbol in ("UNITY_EDITOR", "UNITY_ANDROID"):
            (fixture / "Invalid.cs").write_text("#if " + symbol + "\nclass Invalid {\n#endif\n", encoding="utf-8")
            if subprocess.run([*syntax, str(fixture)], capture_output=True).returncode != 1:
                raise ValueError("Conditional invalid C# syntax was accepted: " + symbol)
        subprocess.run([sys.executable, str(ROOT / "Tools/export_unitypackage.py"), "--output", str(Path(temp) / "VRWebView.unitypackage")], check=True)
        build_upm(Path(temp) / "VRWebView.tgz")
    subprocess.run([sys.executable, str(ROOT / "Tools/check_gameactivity_compat.py")], check=True)
    ids = set()
    for path in [p for package in (PACKAGE, DEVELOPMENT) for p in (package, *package.rglob("*"))]:
        if path.suffix == ".meta":
            continue
        meta = path.with_name(path.name + ".meta")
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M)
        if not match or match[1] in ids:
            raise ValueError("Invalid/duplicate GUID: " + str(meta))
        ids.add(match[1])
        if path.suffix in {".asmdef", ".json"}:
            json.loads(path.read_text())
    aar = DEVELOPMENT / "Plugins/Android/vrwebview-probe.aar"
    provenance = json.loads(aar.with_suffix(".build.json").read_text())
    assert provenance["aarSha256"] == hashlib.sha256(aar.read_bytes()).hexdigest()
    runtime_aar = PACKAGE / "Runtime/Plugins/Android/vrwebview-runtime.aar"
    runtime_provenance = json.loads(runtime_aar.with_suffix(".build.json").read_text())
    assert runtime_provenance["aarSha256"] == hashlib.sha256(runtime_aar.read_bytes()).hexdigest()
    assert runtime_provenance["sources"] == provenance["sources"]
    with zipfile.ZipFile(runtime_aar) as runtime:
        assert runtime.testzip() is None
        assert runtime.read("assets/vrwebview-end-editing.js") == (ROOT / "Android~/runtime/src/main/assets/vrwebview-end-editing.js").read_bytes()
        with zipfile.ZipFile(__import__('io').BytesIO(runtime.read('classes.jar'))) as classes:
            assert all('Probe' not in name for name in classes.namelist()), "Diagnostic class in production AAR"
            assert 'com/t34400/vrwebview/BrowserHost.class' in classes.namelist()
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
