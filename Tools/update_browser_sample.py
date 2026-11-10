"""Refresh one imported Browser sample in place, preserving scene script GUIDs.

Close Unity first. --apply makes a complete backup outside the project before
copying package files. Existing .meta files and application-only files are kept.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import shutil
import uuid

ROOT = Path(__file__).resolve().parents[1]


def inside(path, root):
    return path == root or root in path.parents


def reject_links(path):
    for value in [path, *path.parents]:
        if value.is_symlink():
            raise ValueError('Symbolic links are not accepted: ' + str(value))


def refresh(project, source, backup_root, target=None, apply=False):
    project, source, backup_root = map(Path, (project, source, backup_root))
    for path in (project, source, backup_root):
        reject_links(path)
    project, source, backup_root = project.resolve(), source.resolve(), backup_root.resolve()
    if not (source / 'UnityUI/BrowserDesktopPanel.cs').is_file():
        raise ValueError('Browser package sample source is missing')
    samples = project / 'Assets/Samples' 
    if target is None:
        candidates = list(samples.rglob('BrowserDesktopPanel.cs')) if samples.exists() else []
        if len(candidates) != 1:
            raise ValueError('Expected exactly one imported Browser sample; use --target for one existing sample directory')
        target = candidates[0].parent.parent
    else:
        target = Path(target)
        if not target.is_absolute():
            target = project / target
    reject_links(target)
    target = target.resolve()
    if not inside(target, samples) or not (target / 'UnityUI/BrowserDesktopPanel.cs').is_file():
        raise ValueError('Target must be an existing Browser sample under Assets/Samples')
    if inside(backup_root, project) or inside(backup_root, source):
        raise ValueError('Place backups outside the Unity project, such as Downloads')
    for path in target.rglob('*'):
        if path.is_symlink():
            raise ValueError('Imported sample contains a symbolic link')
    writes = []
    for path in sorted(source.rglob('*')):
        if path.is_symlink():
            raise ValueError('Sample source contains a symbolic link')
        if not path.is_file():
            continue
        destination = target / path.relative_to(source)
        reject_links(destination)
        if destination.exists() and not destination.is_file():
            raise ValueError('Destination is not a regular file')
        if destination.exists() and path.suffix == '.meta':
            continue
        if not destination.exists() or destination.read_bytes() != path.read_bytes():
            writes.append((path, destination))
    report = dict(target=str(target), changed_files=len(writes), applied=False, backup=None,
                  retained='Existing metadata GUIDs and application-only files')
    if not apply or not writes:
        return report
    backup_root.mkdir(parents=True, exist_ok=True)
    backup = backup_root / ('VRWebView-BrowserSampleBackup-' + datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])
    shutil.copytree(target, backup)
    # Backup must succeed completely before the first mutation.
    try:
        for source_file, destination in writes:
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source_file, destination)
    except OSError as error:
        raise OSError('Sample update stopped; the complete original copy is preserved at ' + str(backup)) from error
    report.update(applied=True, backup=str(backup))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=ROOT)
    parser.add_argument('--target', type=Path)
    parser.add_argument('--backup-dir', type=Path, required=True)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    try:
        print(json.dumps(refresh(args.project, ROOT / 'Packages/com.t34400.vrwebview/Samples~/Browser', args.backup_dir, args.target, args.apply), indent=2))
    except (OSError, ValueError) as error:
        parser.exit(1, 'ERROR: ' + str(error) + '\n')


if __name__ == '__main__':
    main()
