import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_layer_boundaries as layers


class LayerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name, references in layers.POLICY.items():
            self.write(name, references)
        for name, references in layers.ENGINE_POLICY.items():
            self.write(name, references, engine=True)

    def write(self, name, references=(), engine=False):
        (self.root / (name + '.asmdef')).write_text(json.dumps(dict(name=name, references=list(references), noEngineReferences=not engine)))

    def test_valid_scope_does_not_claim_whole_package_xr_cleanup(self):
        self.write('Legacy.Probe', ['Unity.XR.Hands'])
        result = layers.check(self.root)
        self.assertEqual(result['whole_package_xr_dependency_removal'], 'not_implemented')
        self.assertIn('Legacy.Probe', result['outside_pure_boundary_scope'])

    def test_core_cannot_depend_on_presentation_or_xr(self):
        for dependency in ['T34400.VRWebView.Presentation', 'Unity.XR.Hands']:
            with self.subTest(dependency=dependency):
                self.write('T34400.VRWebView.Core', [dependency])
                with self.assertRaisesRegex(ValueError, 'Forbidden'): layers.check(self.root)

    def test_runtime_presentation_cannot_depend_on_sample(self):
        self.write('T34400.VRWebView.Presentation', ['T34400.VRWebView.Browser.Sample'])
        with self.assertRaisesRegex(ValueError, 'Forbidden'): layers.check(self.root)

    def test_pure_assembly_cannot_enable_unity_engine_references(self):
        self.write('T34400.VRWebView.Core', engine=True)
        with self.assertRaisesRegex(ValueError, 'disable engine'): layers.check(self.root)

    def test_cycles_outside_new_scope_are_also_rejected(self):
        self.write('Legacy.A', ['Legacy.B']); self.write('Legacy.B', ['Legacy.A'])
        with self.assertRaisesRegex(ValueError, 'cycle'): layers.check(self.root)

    def test_unity_runtime_cannot_reference_xr_or_diagnostics(self):
        for dependency in ['Unity.XR.Hands', 'T34400.VRWebView.VulkanProbe']:
            with self.subTest(dependency=dependency):
                self.write('T34400.VRWebView.Unity', [dependency], engine=True)
                with self.assertRaisesRegex(ValueError, 'Forbidden'): layers.check(self.root)

    def test_unity_ui_cannot_reference_browser_sample_or_android_backend(self):
        for dependency in ['T34400.VRWebView.Browser.Sample', 'T34400.VRWebView.Android']:
            with self.subTest(dependency=dependency):
                self.write('T34400.VRWebView.UnityUI', [dependency], engine=True)
                with self.assertRaisesRegex(ValueError, 'Forbidden'): layers.check(self.root)
