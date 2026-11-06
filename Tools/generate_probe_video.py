"""Generate a synthetic, numbered H.264/AAC video; requires FFmpeg with drawtext/libx264."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Android~/webview/src/main/assets"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    args = parser.parse_args()
    target = ASSETS / "vrwebview-probe-video.mp4"
    command = [args.ffmpeg, "-hide_banner", "-loglevel", "warning", "-y",
               "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
               "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000",
               "-t", "8", "-vf", "drawtext=text='VIDEO frame %{n}':fontsize=54:fontcolor=white:box=1:boxcolor=black@0.8:x=40:y=40",
               "-af", "volume=0.08", "-c:v", "libx264", "-preset", "medium", "-crf", "30",
               "-pix_fmt", "yuv420p", "-profile:v", "main", "-level", "3.1", "-g", "30",
               "-c:a", "aac", "-b:a", "64k", "-movflags", "+faststart", str(target)]
    subprocess.run(command, check=True)
    metadata = {"schemaVersion": 1, "description": "Synthetic FFmpeg testsrc2, burned-in frame number, and 880 Hz tone",
                "durationSeconds": 8, "width": 1280, "height": 720, "frameRate": 30,
                "videoCodec": "H.264 Main", "audioCodec": "AAC", "generatedContentLicense": "CC0-1.0",
                "sha256": hashlib.sha256(target.read_bytes()).hexdigest(), "bytes": target.stat().st_size}
    target.with_suffix(".json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print("Generated synthetic probe media:", target.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
