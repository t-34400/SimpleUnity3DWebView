"""Maintainer-only reproducible patch of the verified Google GameActivity 4.4.0 AAR."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE_SHA = '3a8b0887ac55a0a6ab56021ced7353a250539eac811278b74bccd8a5b678d9c9'
CLASSES = {
    'com/google/androidgamesdk/gametextinput/InputConnection.class': ('connection', '56689dabd6edf7ea80a4ed69bfc58b9b4454017497f18cd1c82fce06a1e3424c'),
    'com/google/androidgamesdk/GameActivity$InputEnabledSurfaceView.class': ('surface', '2e9f3968d489976994819d29032c6268a6689e53f362c44a73899554bbd84b5f')}


def digest(data): return hashlib.sha256(data).hexdigest()


def archive(entries):
    data = io.BytesIO()
    with zipfile.ZipFile(data, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, raw in sorted(entries.items()):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            z.writestr(info, raw)
    return data.getvalue()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-aar', type=Path, required=True)
    p.add_argument('--android-jar', type=Path, required=True)
    p.add_argument('--asm', type=Path, action='append', required=True, help='ASM 9.6 core and commons jars')
    p.add_argument('--jdk', type=Path, required=True)
    p.add_argument('--output-dir', type=Path, default=ROOT / 'Packages/com.t34400.vrwebview/Editor/GameActivityImeCompat')
    a = p.parse_args()
    raw = a.source_aar.read_bytes()
    if digest(raw) != SOURCE_SHA: raise ValueError('Expected the exact verified Google games-activity 4.4.0 AAR')
    with zipfile.ZipFile(io.BytesIO(raw)) as z: entries = {n: z.read(n) for n in z.namelist()}
    with zipfile.ZipFile(io.BytesIO(entries['classes.jar'])) as z: classes = {n: z.read(n) for n in z.namelist()}
    for name, (_, expected) in CLASSES.items():
        if digest(classes[name]) != expected: raise ValueError('Unexpected pinned Google class: ' + name)
    preserved_entries = {n:digest(raw) for n,raw in entries.items() if n not in ('classes.jar','proguard.txt')}
    preserved_classes = {n:digest(raw) for n,raw in classes.items() if n not in CLASSES}
    sources = ROOT / 'Android~/gameactivity-compat'
    import os
    sep = os.pathsep
    with tempfile.TemporaryDirectory(prefix='vrwebview-gameime-build-') as temp:
        work = Path(temp)
        original = work / 'original.jar'; original.write_bytes(entries['classes.jar'])
        helper = work / 'helper'; helper.mkdir()
        patcher = work / 'patcher'; patcher.mkdir()
        run = lambda cmd: subprocess.run([str(x) for x in cmd], check=True)
        run([a.jdk/'bin/javac', '--release', '8', '-cp', sep.join(map(str, [original, a.android_jar])), '-d', helper,
             sources/'src/com/t34400/vrwebview/gameime/ImeStateMonitor.java'])
        run([a.jdk/'bin/javac', '--release', '8', '-cp', sep.join(map(str,a.asm)), '-d', patcher,
             sources/'build-tools/PatchGameActivity.java'])
        for name, (kind, _) in CLASSES.items():
            before = work / (kind+'.before.class'); before.write_bytes(classes[name])
            after = work / (kind+'.after.class')
            run([a.jdk/'bin/java', '-cp', sep.join(map(str,[patcher,*a.asm])), 'PatchGameActivity', kind, before, after])
            classes[name] = after.read_bytes()
        additions = {p.relative_to(helper).as_posix(): p.read_bytes() for p in sorted(helper.rglob('*.class'))}
        if any(n in classes for n in additions): raise ValueError('Helper class collision')
        classes.update(additions)
    entries['classes.jar'] = archive(classes)
    # These Google types/members are also reached through the unchanged native
    # library. Preserve their ABI independently of a host's shrinker defaults.
    entries['proguard.txt'] += b'\n-keep class com.google.androidgamesdk.GameActivity { *; }\n-keep class com.google.androidgamesdk.GameActivity$* { *; }\n-keep class com.google.androidgamesdk.gametextinput.** { *; }\n-keep class com.t34400.vrwebview.gameime.** { *; }\n'
    entries['META-INF/vrwebview-gameime.json'] = json.dumps(dict(revision=3, originalAarSha256=SOURCE_SHA,
        notice='Diagnostic modification: extracted text/monitor notifications on the original connection; native entries unmodified'), sort_keys=True).encode()
    result = archive(entries)
    a.output_dir.mkdir(parents=True, exist_ok=True)
    output = a.output_dir / 'games-activity-4.4.0-vrwebview-ime.aar'
    output.write_bytes(result)
    inputs = [Path(__file__).resolve(), *sorted(sources.rglob('*.java'))]
    record = dict(schemaVersion=1, revision=3, googleCoordinate='androidx.games:games-activity:4.4.0',
        sourceUrl='https://dl.google.com/dl/android/maven2/androidx/games/games-activity/4.4.0/games-activity-4.4.0.aar',
        originalAarSha256=SOURCE_SHA, aarSha256=digest(result),
        sources={p.relative_to(ROOT).as_posix(): digest(p.read_bytes()) for p in inputs},
        changedGoogleClasses={n: dict(before=expected,after=digest(classes[n])) for n,(_,expected) in CLASSES.items()},
        preservedEntries=preserved_entries, preservedClasses=preserved_classes,
        addedClasses=sorted(additions), nativeEntriesPreserved=True, runtimeValidation='Quest not performed by this build')
    output.with_suffix('.build.json').write_text(json.dumps(record,indent=2)+'\n')
    print('Built', output, 'SHA256', digest(result))


if __name__ == '__main__': main()
