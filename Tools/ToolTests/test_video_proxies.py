import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from make_video_proxies import inspect, make_proxies


@unittest.skipUnless(shutil.which('ffmpeg') and shutil.which('ffprobe'), 'FFmpeg media integration needs ffmpeg/ffprobe')
class VideoProxyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='vrwebview-proxy-test-')
        cls.root = Path(cls.temp.name)
        cls.original = cls.root / 'take with spaces.mp4'
        subprocess.run(['ffmpeg', '-v', 'error', '-nostdin', '-f', 'lavfi', '-i',
                        'testsrc2=size=320x180:rate=24:duration=1.5', '-vf', 'setsar=2',
                        '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-output_ts_offset', '5',
                        '-metadata', 'location=+35.0000+139.0000/', str(cls.original)], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_real_encode_preserves_timing_aspect_source_and_metadata_mapping(self):
        before = hashlib.sha256(self.original.read_bytes()).hexdigest()
        with tempfile.TemporaryDirectory(dir=self.root) as temp:
            out = Path(temp) / 'proxies'
            record = make_proxies([self.original], out)
            self.assertEqual(before, hashlib.sha256(self.original.read_bytes()).hexdigest())
            self.assertEqual(record, json.loads((out / 'manifest.json').read_text()))
            clip = record['clips'][0]
            self.assertEqual(clip['original_sha256'], before)
            self.assertEqual(clip['original_file'], self.original.name)
            self.assertEqual(clip['source_offset_seconds'], 0)
            self.assertEqual(clip['speed'], 1)
            original, proxy = clip['original_video'], clip['proxy_video']
            self.assertGreaterEqual(float(original['start_time']), 5)
            self.assertAlmostEqual(float(proxy['start_time']), 0, places=3)
            self.assertLessEqual(abs(proxy['duration_seconds'] - original['duration_seconds']), clip['timing_tolerance_seconds'])
            self.assertEqual((proxy['width'], proxy['height']), (640, 180))
            self.assertEqual(proxy['sample_aspect_ratio'], '1:1')
            self.assertEqual(proxy['avg_frame_rate'], '15/1')
            self.assertFalse(proxy['has_audio'])
            self.assertLessEqual(clip['proxy_bytes'], 30_000_000)
            self.assertNotIn('location', (out / 'manifest.json').read_text())

    def test_existing_output_and_duplicate_sources_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'already exists'):
            make_proxies([self.original], self.root)
        with self.assertRaisesRegex(ValueError, 'distinct'):
            make_proxies([self.original, self.original], self.root / 'duplicate')

    def test_encoder_failure_does_not_publish_partial_directory(self):
        with tempfile.TemporaryDirectory(dir=self.root) as temp:
            output = Path(temp) / 'failed'
            with self.assertRaises(OSError):
                make_proxies([self.original], output, ffmpeg='no-such-vrwebview-encoder')
            self.assertFalse(output.exists())
            self.assertEqual(list(Path(temp).iterdir()), [])

    def test_size_limit_is_enforced_on_actual_bytes(self):
        with tempfile.TemporaryDirectory(dir=self.root) as temp:
            output = Path(temp) / 'small'
            record = make_proxies([self.original], output, max_bytes=30_000)
            self.assertLessEqual(record['clips'][0]['proxy_bytes'], 30_000)


if __name__ == '__main__':
    unittest.main()
