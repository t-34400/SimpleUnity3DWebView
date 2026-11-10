from pathlib import Path
import json
import sys
import tarfile
import tempfile
import unittest
import shutil
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import check_distribution as distribution
from build_upm import build


class DistributionTests(unittest.TestCase):
    def test_core_and_browser_profiles_resolve_without_diagnostics(self):
        result=distribution.check()
        self.assertFalse(result['diagnostics_in_consumer'])
        self.assertNotIn('T34400.VRWebView.Browser.Sample',result['profiles']['core_only'])
        self.assertIn('T34400.VRWebView.Browser.Sample',result['profiles']['core_and_browser'])

    def test_archive_is_reproducible_and_excludes_project_and_probe_aar(self):
        with tempfile.TemporaryDirectory() as temp:
            a=build(Path(temp)/'a.tgz');b=build(Path(temp)/'b.tgz')
            self.assertEqual(a['sha256'],b['sha256'])
            names=set(a['files'])
            self.assertIn('Runtime/Plugins/Android/vrwebview-runtime.aar',names)
            self.assertIn('Samples~/Browser/README.md',names)
            self.assertFalse(any('vrwebview-probe.aar' in n or n.startswith(('ProjectSettings/','Assets/','Development')) for n in names))
            with tarfile.open(Path(temp)/'a.tgz') as archive:
                metadata=json.load(archive.extractfile('package/package.json'))
                self.assertEqual(metadata['name'],'com.t34400.vrwebview')

    def test_missing_sample_reference_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            core=Path(temp)/'package';shutil.copytree(distribution.PACKAGE,core)
            p=core/'Samples~/Browser/UnityUI/T34400.VRWebView.Browser.UnityUI.Sample.asmdef'
            data=json.loads(p.read_text());data['references'].append('T34400.Missing');p.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError,'Missing local assembly'):
                distribution.check(core,development=None)

    def test_unknown_files_and_links_cannot_enter_release_archive(self):
        with tempfile.TemporaryDirectory() as temp:
            package=Path(temp)/'package';package.mkdir();(package/'.env').write_text('private')
            with self.assertRaisesRegex(ValueError,'Unexpected consumer'):
                build(Path(temp)/'bad.tgz',package)
            (package/'.env').unlink();(package/'README.md').symlink_to(Path(temp)/'outside')
            with self.assertRaisesRegex(ValueError,'Unsupported package entry'):
                build(Path(temp)/'link.tgz',package)

    def test_readme_relative_links_resolve_for_both_install_paths(self):
        import re
        for readme in [distribution.ROOT/'README.md',distribution.PACKAGE/'README.md',distribution.PACKAGE/'Samples~/Browser/README.md']:
            for target in re.findall(r'\]\(([^)]+)\)',readme.read_text()):
                if '://' not in target and not target.startswith('#'):
                    self.assertTrue((readme.parent/target.split('#')[0]).exists(),(readme,target))
