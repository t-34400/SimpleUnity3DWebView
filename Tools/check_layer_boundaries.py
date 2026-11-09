"""Check the implemented pure upper-layer boundaries, not the unfinished package migration."""
import json
from pathlib import Path

POLICY = {
    'T34400.VRWebView.Core': set(),
    'T34400.VRWebView.Presentation': {'T34400.VRWebView.Core'},
    'T34400.VRWebView.Browser.Sample': {'T34400.VRWebView.Core'},
}
ENGINE_POLICY = {
    'T34400.VRWebView.Editor': {'T34400.VRWebView.Android', 'T34400.VRWebView.VulkanProbe', 'Unity.XR.OpenXR', 'Unity.XR.OpenXR.Editor', 'T34400.VRWebView.Bootstrap', 'T34400.VRWebView.Unity', 'T34400.VRWebView.UnityUI', 'UnityEngine.UI'},
    'T34400.VRWebView.Rendering': set(),
    'T34400.VRWebView.Android': {'T34400.VRWebView.Core', 'T34400.VRWebView.Presentation', 'T34400.VRWebView.Unity', 'T34400.VRWebView.Rendering'},
    'T34400.VRWebView.Bootstrap': {'T34400.VRWebView.Presentation', 'T34400.VRWebView.Core', 'T34400.VRWebView.Unity', 'T34400.VRWebView.UnityUI', 'T34400.VRWebView.Android'},
    'T34400.VRWebView.Browser.Android.Sample': {'T34400.VRWebView.Core', 'T34400.VRWebView.Presentation', 'T34400.VRWebView.Unity', 'T34400.VRWebView.Android', 'T34400.VRWebView.Browser.Sample', 'T34400.VRWebView.Browser.UnityUI.Sample'},
    'T34400.VRWebView.Unity': {'T34400.VRWebView.Core', 'T34400.VRWebView.Presentation'},
    'T34400.VRWebView.UnityUI': {'T34400.VRWebView.Core', 'T34400.VRWebView.Presentation', 'T34400.VRWebView.Unity', 'UnityEngine.UI'},
    'T34400.VRWebView.Browser.UnityUI.Sample': {'T34400.VRWebView.Core', 'T34400.VRWebView.Presentation', 'T34400.VRWebView.Unity', 'T34400.VRWebView.UnityUI', 'T34400.VRWebView.Browser.Sample', 'UnityEngine.UI'},
}


# Public Bind signatures and uGUI scene composition require defining assemblies
# directly. Transitive paths do not supply C# compiler references.
REQUIRED_DIRECT_REFERENCES = {
    'T34400.VRWebView.Editor': {'UnityEngine.UI'},
    'T34400.VRWebView.Bootstrap': {'T34400.VRWebView.Presentation'},
}


def check(package):
    assemblies = {}
    for path in Path(package).rglob('*.asmdef'):
        value = json.loads(path.read_text())
        name = value['name']
        if name in assemblies:
            raise ValueError('Duplicate assembly: ' + name)
        assemblies[name] = value
    for name, allowed in {**POLICY, **ENGINE_POLICY}.items():
        value = assemblies.get(name)
        if value is None:
            raise ValueError('Missing upper-layer assembly: ' + name)
        if name in POLICY and value.get('noEngineReferences') is not True:
            raise ValueError('Pure assembly must disable engine references: ' + name)
        if name in ENGINE_POLICY and value.get('noEngineReferences') is True:
            raise ValueError('Unity assembly must allow engine references: ' + name)
        unexpected = set(value.get('references', [])) - allowed
        if unexpected:
            raise ValueError('Forbidden upper-layer reference: ' + name + ' -> ' + ', '.join(sorted(unexpected)))
    for name, required in REQUIRED_DIRECT_REFERENCES.items():
        missing = required - set(assemblies[name].get('references', []))
        if missing:
            raise ValueError('Missing direct assembly reference: ' + name + ' -> ' + ', '.join(sorted(missing)))
    visiting, complete = set(), set()

    def visit(name):
        if name in visiting:
            raise ValueError('Assembly cycle at: ' + name)
        if name in complete:
            return
        visiting.add(name)
        for reference in assemblies[name].get('references', []):
            if reference in assemblies:
                visit(reference)
        visiting.remove(name)
        complete.add(name)

    for name in assemblies:
        visit(name)
    return dict(pure_boundary_scope=sorted(POLICY), local_assembly_cycles=False, cycle_scope='local_name_references',
                unity_boundary_scope=sorted(ENGINE_POLICY),
                required_direct_reference_scope=sorted(REQUIRED_DIRECT_REFERENCES),
                outside_boundary_scope=sorted(set(assemblies) - set(POLICY) - set(ENGINE_POLICY)),
                outside_pure_boundary_scope=sorted(set(assemblies) - set(POLICY)),
                whole_package_xr_dependency_removal='not_implemented')


if __name__ == '__main__':
    print(json.dumps(check(Path(__file__).resolve().parents[1] / 'Packages/com.t34400.vrwebview'), indent=2))
