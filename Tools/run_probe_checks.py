"""Check native helper behavior, bundled video decoding, and AAR media provenance."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
JAVA = ROOT / "Android~/webview/src/main/java/com/t34400/vrwebview"
ASSETS = ROOT / "Android~/webview/src/main/assets"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--javac", default="javac")
    parser.add_argument("--java", default="java")
    parser.add_argument("--ffmpeg", default="ffmpeg")
    parser.add_argument("--ffprobe", default="ffprobe")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="vrwebview-probe-tests-") as temp:
        subprocess.run([args.javac, "--release", "11", "-d", temp,
                        str(JAVA / "ProbeGeometry.java"), str(JAVA / "ProbeRange.java"),
                        str(JAVA / "ProbeMediaStream.java"), str(ROOT / "Tools/ProbeTests/MediaStreamTests.java"),
                        str(JAVA / "ImeCallMetadata.java"), str(ROOT / "Tools/ProbeTests/ImeCallMetadataTests.java"),
                        str(JAVA / "FocusCallTrace.java"), str(ROOT / "Tools/ProbeTests/FocusCallTraceTests.java"),
                        str(ROOT / "Android~/runtime/src/main/java/com/t34400/vrwebview/ImeWindowFocusGuard.java"), str(ROOT / "Tools/ProbeTests/ImeWindowFocusGuardTests.java"),
                        str(JAVA / "ProbeFocusRecovery.java"),
                        str(JAVA / "BorrowedInputConnection.java"), str(JAVA / "ProxyTrialState.java"), str(JAVA / "HostFailureMetadata.java"),
                        str(ROOT / "Tools/ProbeTests/ProxyConnectionTests.java"),
                        str(JAVA / "HostTrialState.java"),
                        str(JAVA / "HostEditorProfile.java"),
                        str(JAVA / "ActivityGpuLifetime.java"),
                        str(JAVA / "ActivityGpuTouchPolicy.java"),
                        str(ROOT / "Tools/ProbeTests/ProbeTests.java")], check=True)
        subprocess.run([args.java, "-cp", temp, "com.t34400.vrwebview.ProbeTests"], check=True)
        subprocess.run([args.java, "-cp", temp, "com.t34400.vrwebview.ProxyConnectionTests"], check=True)
        subprocess.run([args.java, "-cp", temp, "com.t34400.vrwebview.ImeCallMetadataTests"], check=True)
        subprocess.run([args.java, "-cp", temp, "com.t34400.vrwebview.FocusCallTraceTests"], check=True)
        subprocess.run([args.java, "-cp", temp, "com.t34400.vrwebview.ImeWindowFocusGuardTests"], check=True)
        subprocess.run([args.java, "-cp", temp, "com.t34400.vrwebview.MediaStreamTests",
                        str(ASSETS / "vrwebview-probe-video.mp4")], check=True)
    video = ASSETS / "vrwebview-probe-video.mp4"
    metadata = json.loads(video.with_suffix(".json").read_text())
    assert hashlib.sha256(video.read_bytes()).hexdigest() == metadata["sha256"]
    assert video.stat().st_size == metadata["bytes"]
    info = json.loads(subprocess.check_output([args.ffprobe, "-v", "error", "-count_frames",
                      "-show_streams", "-show_format", "-of", "json", str(video)]))
    stream = next(s for s in info["streams"] if s["codec_type"] == "video")
    audio = next(s for s in info["streams"] if s["codec_type"] == "audio")
    assert stream["codec_name"] == "h264" and stream["width"] == 1280 and stream["height"] == 720
    assert stream["r_frame_rate"] == "30/1" and int(stream["nb_read_frames"]) == 240
    assert audio["codec_name"] == "aac" and 7.9 <= float(info["format"]["duration"]) <= 8.1
    subprocess.run([args.ffmpeg, "-hide_banner", "-v", "error", "-xerror", "-i", str(video), "-f", "null", "-"], check=True)
    aar = ROOT / "Packages/com.t34400.vrwebview/Runtime/Plugins/Android/vrwebview-probe.aar"
    with zipfile.ZipFile(aar) as archive:
        for name in ("vrwebview-gpu-probe.html", "vrwebview-proxy-probe.html", "vrwebview-probe-video.mp4", "vrwebview-probe-video.json"):
            assert archive.read("assets/" + name) == (ASSETS / name).read_bytes()
        with tempfile.TemporaryDirectory(prefix="vrwebview-probe-aar-") as temp:
            jar = Path(temp) / "classes.jar"; jar.write_bytes(archive.read("classes.jar"))
            with zipfile.ZipFile(jar) as classes:
                assert "com/t34400/vrwebview/GpuProbeActivity.class" in classes.namelist()
                assert "com/t34400/vrwebview/GpuProbePreview.class" in classes.namelist()
                for name in ("HostComparison", "HostComparisonActivity", "OpaqueHostActivity", "TransparentHostActivity", "HostTrialState", "HostProbeEnvironment", "HostProbeEditText", "HostEditorProfile", "HostEditorProfileStore", "ActivityGpuProbe", "ActivityGpuHostActivity", "ActivityGpuLifetime", "ActivityGpuTouchPolicy", "ImeProxyProbe", "BorrowedInputConnection", "ProxyTrialState", "HostFailureMetadata", "ProbeMediaStream", "ProbeVideoTelemetry", "ProbeEditorTelemetry", "ImeCallMetadata", "FocusCallTrace", "RuntimeProbeContent"):
                    assert "com/t34400/vrwebview/" + name + ".class" in classes.namelist(), name
    print("PASS H.264/AAC media decode, 240 numbered source frames, and exact AAR assets/classes")
    print("NOT RUN: Android GPU rendering/playback/IME; Unity import/build; Quest/OpenXR runtime")


if __name__ == "__main__":
    main()
