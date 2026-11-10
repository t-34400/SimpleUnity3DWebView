"""Check the opt-in GameActivity patch payload and its maintainer source provenance."""
from pathlib import Path
import hashlib
import io
import json
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def sha(data): return hashlib.sha256(data).hexdigest()


def main():
    p=ROOT/'Packages/com.t34400.vrwebview/Editor/GameActivityImeCompat/games-activity-4.4.0-vrwebview-ime.aar'
    assert 'Android: Android\n    second:\n      enabled: 0' in p.with_name(p.name+'.meta').read_text(), 'Diagnostic AAR must not be auto imported'
    record=json.loads(p.with_suffix('.build.json').read_text())
    assert record['revision']==3, 'Expected active-state notification GameActivity revision'
    assert record['originalAarSha256']=='3a8b0887ac55a0a6ab56021ced7353a250539eac811278b74bccd8a5b678d9c9'
    assert sha(p.read_bytes())==record['aarSha256'], 'Stale/corrupt GameActivity diagnostic AAR'
    for name,expected in record['sources'].items():
        assert sha((ROOT/name).read_bytes())==expected, 'Stale GameActivity patch source: '+name
    with zipfile.ZipFile(p) as z:
        assert z.testzip() is None
        assert set(z.namelist())==set(record['preservedEntries'])|{'classes.jar','proguard.txt','META-INF/vrwebview-gameime.json'}
        for name,expected in record['preservedEntries'].items(): assert sha(z.read(name))==expected, name
        with zipfile.ZipFile(io.BytesIO(z.read('classes.jar'))) as classes:
            assert set(classes.namelist())==set(record['preservedClasses'])|set(record['changedGoogleClasses'])|set(record['addedClasses'])
            for name,expected in record['preservedClasses'].items(): assert sha(classes.read(name))==expected,name
            for name,hashes in record['changedGoogleClasses'].items(): assert sha(classes.read(name))==hashes['after'],name
        assert b'-keep class com.google.androidgamesdk.gametextinput.**' in z.read('proguard.txt')
    print('PASS GameActivity opt-in AAR provenance, preserved Google/native entries and class inventory')


if __name__=='__main__': main()
