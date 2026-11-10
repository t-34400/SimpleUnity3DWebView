from pathlib import Path
import sys
import tempfile
import unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from update_browser_sample import refresh


class SampleUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / 'Project'
        self.source = self.project / 'Packages/com.example/Samples~/Browser'
        self.target = self.project / 'Assets/Samples/VRWebView/old/Browser'
        self.backups = self.root / 'Downloads'
        for path in [self.source, self.target]:
            (path / 'UnityUI').mkdir(parents=True)
        (self.source / 'UnityUI/BrowserDesktopPanel.cs').write_text('updated')
        (self.source / 'UnityUI/BrowserDesktopPanel.cs.meta').write_text('package-guid')
        (self.source / 'Feature.cs').write_text('new feature')
        (self.target / 'UnityUI/BrowserDesktopPanel.cs').write_text('user modification')
        (self.target / 'UnityUI/BrowserDesktopPanel.cs.meta').write_text('scene-guid')
        (self.target / 'Application.cs').write_text('application-only')

    def test_apply_backs_up_user_edits_and_preserves_scene_guids(self):
        report = refresh(self.project, self.source, self.backups, apply=True)
        backup = Path(report['backup'])
        self.assertEqual((backup / 'UnityUI/BrowserDesktopPanel.cs').read_text(), 'user modification')
        self.assertEqual((self.target / 'UnityUI/BrowserDesktopPanel.cs').read_text(), 'updated')
        self.assertEqual((self.target / 'UnityUI/BrowserDesktopPanel.cs.meta').read_text(), 'scene-guid')
        self.assertEqual((self.target / 'Application.cs').read_text(), 'application-only')
        self.assertTrue((self.target / 'Feature.cs').is_file())
        self.assertFalse(refresh(self.project, self.source, self.backups, apply=True)['applied'])

    def test_preview_does_not_create_backup_or_modify_target(self):
        report = refresh(self.project, self.source, self.backups)
        self.assertFalse(report['applied'])
        self.assertFalse(self.backups.exists())
        self.assertEqual((self.target / 'UnityUI/BrowserDesktopPanel.cs').read_text(), 'user modification')

    def test_multiple_samples_require_an_explicit_target(self):
        other = self.project / 'Assets/Samples/Other/UnityUI'
        other.mkdir(parents=True)
        (other / 'BrowserDesktopPanel.cs').write_text('another copy')
        with self.assertRaisesRegex(ValueError, 'exactly one'):
            refresh(self.project, self.source, self.backups)
        self.assertEqual(refresh(self.project, self.source, self.backups, self.target)['target'], str(self.target))

    def test_backups_inside_assets_and_targets_outside_samples_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'outside'):
            refresh(self.project, self.source, self.target / 'backup', apply=True)
        with self.assertRaisesRegex(ValueError, 'Assets/Samples'):
            refresh(self.project, self.source, self.backups, self.source)

    def test_link_destination_is_rejected_before_any_mutation(self):
        outside = self.root / 'outside'
        outside.write_text('private')
        (self.target / 'Feature.cs').symlink_to(outside)
        with self.assertRaisesRegex(ValueError, 'symbolic link'):
            refresh(self.project, self.source, self.backups, apply=True)
        self.assertEqual(outside.read_text(), 'private')
        self.assertFalse(self.backups.exists())
