"""Export the embedded UPM package under Assets/VRWebView without staging duplicate GUIDs in Unity."""
import argparse
import gzip
import io
from pathlib import Path
import re
import tarfile

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / "Packages/com.t34400.vrwebview"


def export(output):
    assets = []
    for path in [PACKAGE, *sorted(PACKAGE.rglob("*"))]:
        rel = path.relative_to(PACKAGE)
        if path.suffix == ".meta" or (rel.parts and rel.parts[0] in {"Tests", "package.json"}):
            continue
        parts = list(rel.parts)
        if parts:
            parts[0] = {"Samples~": "Samples", "Documentation~": "Documentation"}.get(parts[0], parts[0])
        if any("~" in p for p in parts):
            raise ValueError("Unsupported ignored directory: " + str(rel))
        meta = path.with_name(path.name + ".meta")
        if not meta.exists():
            raise ValueError("Missing metadata: " + str(path))
        raw_meta = meta.read_bytes()
        match = re.search(rb"^guid: ([0-9a-f]{32})\r?$", raw_meta, re.M)
        if not match:
            raise ValueError("Invalid GUID: " + str(meta))
        assets.append((match[1].decode(), path, raw_meta, "/".join(["Assets", "VRWebView", *parts])))
    guids = [a[0] for a in assets]
    if len(guids) != len(set(guids)):
        raise ValueError("Duplicate GUIDs in package")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("wb") as file, gzip.GzipFile(filename="", mode="wb", fileobj=file, mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w") as archive:
            for guid, path, meta, pathname in assets:
                payloads = {"asset.meta": meta, "pathname": pathname.encode("utf-8")}
                if path.is_file():
                    payloads["asset"] = path.read_bytes()
                for name, payload in payloads.items():
                    entry = tarfile.TarInfo(f"{guid}/{name}")
                    entry.size = len(payload); entry.mode = 0o644
                    archive.addfile(entry, io.BytesIO(payload))
    # Validate the generated tarball against the source, including GUIDs and binary bytes.
    with tarfile.open(output, "r:gz") as archive:
        for guid, path, meta, pathname in assets:
            assert archive.extractfile(f"{guid}/pathname").read().decode() == pathname
            assert archive.extractfile(f"{guid}/asset.meta").read() == meta
            if path.is_file():
                assert archive.extractfile(f"{guid}/asset").read() == path.read_bytes()
    return len(assets)


if __name__ == "__main__":
    downloads = Path("/mnt/c/Users/tmiyoshi/Downloads") if Path("/mnt/c").exists() else Path.home() / "Downloads"
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=downloads / "VRWebView.unitypackage")
    args = parser.parse_args()
    print(f"Exported {export(args.output)} assets: {args.output}")
