"""Validate consumer/development boundaries and each optional sample's local assemblies."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = ROOT / 'Packages/com.t34400.vrwebview'
DEVELOPMENT = ROOT / 'Development~/com.t34400.vrwebview.diagnostics'
ALLOWED_TOP = {'Runtime', 'Editor', 'Samples~', 'Documentation~', 'Tests', 'Licenses',
               'README.md', 'CHANGELOG.md', 'LICENSE', 'NOTICE', 'ThirdPartyNotices.md', 'package.json'}


def entries(package):
    package = Path(package)
    for path in sorted(package.rglob('*')):
        if path.is_symlink() or not (path.is_file() or path.is_dir()):
            raise ValueError('Unsupported package entry: ' + str(path))
        relative = path.relative_to(package)
        top = relative.parts[0].removesuffix('.meta')
        if top not in ALLOWED_TOP or any(p in {'.git', '.env', '__pycache__', 'build', '.gradle'} for p in relative.parts):
            raise ValueError('Unexpected consumer package entry: ' + str(relative))
        if '\\' in str(relative):
            raise ValueError('Nonportable package path')
        yield path


def assemblies(package, sample=False):
    result = {}
    for path in Path(package).rglob('*.asmdef'):
        parts = path.relative_to(package).parts
        if 'Tests' in parts or 'Tests~' in parts:
            continue
        if 'Samples~' in parts and not (sample and parts[:2] == ('Samples~', 'Browser')):
            continue
        value = json.loads(path.read_text())
        if value['name'] in result: raise ValueError('Duplicate assembly: ' + value['name'])
        result[value['name']] = value
    return result


def check(package=PACKAGE, development=DEVELOPMENT, project_manifest=ROOT/'Packages/manifest.json'):
    package = Path(package)
    development = Path(development) if development is not None else None
    list(entries(package))
    metadata = json.loads((package/'package.json').read_text())
    if metadata['name'] != 'com.t34400.vrwebview': raise ValueError('Unexpected package identity')
    if metadata.get('license') != 'Apache-2.0': raise ValueError('Unexpected project license')
    if any('diagnostic' in name for name in metadata.get('dependencies', {})): raise ValueError('Consumer package depends on diagnostics')
    if [s['path'] for s in metadata.get('samples', [])] != ['Samples~/Browser']: raise ValueError('Unexpected consumer samples')
    for required in ('README.md','LICENSE','NOTICE','CHANGELOG.md','ThirdPartyNotices.md','Documentation~/GettingStarted.md','Documentation~/API.md'):
        if not (package/required).is_file(): raise ValueError('Missing distribution document: '+required)
    for path in (package/'Runtime').rglob('*'):
        if path.name in {'vrwebview-probe.aar','AndroidImeProbe.cs','AndroidGpuProbe.cs','GpuWebViewProbe.cs','HostComparisonProbe.cs','UnityInputFieldProbe.cs'}:
            raise ValueError('Diagnostic implementation in consumer runtime: '+path.name)
    profiles={}
    for sample in (False, True):
        scope=assemblies(package,sample)
        for name,definition in scope.items():
            for ref in definition.get('references',[]):
                if ref.startswith('T34400.') and ref not in scope: raise ValueError('Missing local assembly: '+name+' -> '+ref)
                if 'Diagnostics' in ref: raise ValueError('Consumer assembly references diagnostics')
        profiles['core_and_browser' if sample else 'core_only']=sorted(scope)
    # Confirm that the development project, but not the public package, opts in.
    if development is not None:
        dev=json.loads((development/'package.json').read_text())
        if dev['dependencies'].get(metadata['name'])!=metadata['version']: raise ValueError('Development version differs from core')
        scopes={**assemblies(package),**assemblies(development)}
        for name,definition in scopes.items():
            for ref in definition.get('references',[]):
                if ref.startswith('T34400.') and ref not in scopes: raise ValueError('Development assembly missing: '+name+' -> '+ref)
        manifest_path=Path(project_manifest)
        manifest=json.loads(manifest_path.read_text())
        local=manifest['dependencies'].get(dev['name'],'')
        if not local.startswith('file:') or (manifest_path.parent/local[5:]).resolve()!=development.resolve(): raise ValueError('Invalid development project dependency')
    return dict(package=metadata['name'],version=metadata['version'],profiles=profiles,diagnostics_in_consumer=False)


if __name__=='__main__': print(json.dumps(check(),indent=2))
