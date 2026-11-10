"""Build a deterministic consumer UPM tarball; no project settings or diagnostics."""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import tarfile
from check_distribution import PACKAGE, ROOT, entries, check


def build(output, package=PACKAGE):
    output, package = Path(output), Path(package)
    if output.resolve().is_relative_to(ROOT.resolve()):
        raise ValueError('Write release archives outside the source project')
    paths=list(entries(package))
    output.parent.mkdir(parents=True,exist_ok=True)
    manifest={}
    with output.open('wb') as raw, gzip.GzipFile(filename='',mode='wb',fileobj=raw,mtime=0) as compressed:
        with tarfile.open(fileobj=compressed,mode='w',format=tarfile.PAX_FORMAT) as archive:
            for path in paths:
                rel=path.relative_to(package).as_posix();info=tarfile.TarInfo('package/'+rel)
                if path.is_dir():info.type=tarfile.DIRTYPE;info.mode=0o755;archive.addfile(info)
                else:
                    data=path.read_bytes();info.size=len(data);info.mode=0o644;archive.addfile(info,io.BytesIO(data));manifest[rel]=hashlib.sha256(data).hexdigest()
    with tarfile.open(output,'r:gz') as archive:
        files={m.name.removeprefix('package/'):m for m in archive.getmembers() if m.isfile()}
        if set(files)!=set(manifest):raise ValueError('UPM archive inventory differs')
        for name,member in files.items():
            if hashlib.sha256(archive.extractfile(member).read()).hexdigest()!=manifest[name]:raise ValueError('UPM payload differs: '+name)
    return dict(archive=output.name,sha256=hashlib.sha256(output.read_bytes()).hexdigest(),files=manifest)


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    try:
        check();record=build(args.output)
        args.output.with_suffix('.manifest.json').write_text(json.dumps(record,indent=2)+'\n')
        print('Built',args.output,'SHA256',record['sha256'],'files',len(record['files']))
    except (OSError,ValueError) as error:parser.exit(1,'ERROR: '+str(error)+'\n')


if __name__=='__main__':main()
