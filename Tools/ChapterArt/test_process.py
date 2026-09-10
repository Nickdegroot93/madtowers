"""Offline regression checks; never touches project art or invokes generation."""
import importlib.util
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
from PIL import Image, ImageDraw

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('chapter_process', HERE / 'process.py')
processor = importlib.util.module_from_spec(spec)
spec.loader.exec_module(processor)


class ProcessingChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.project = pathlib.Path(self.temp.name)
        self.root = self.project / 'Tools/ChapterArt'
        self.root.mkdir(parents=True)
        processor.ROOT = self.root
        processor.DEST = self.project / 'Assets/Resources/ChapterArt'
        processor.DEST.mkdir(parents=True)
        self.raw = self.root / 'raw/background-led'
        self.raw.mkdir(parents=True)
        self.asset = processor.DEST / 'ornament_Old.png'
        Image.new('RGBA', (512, 512), 'red').save(self.asset)
        self.record = dict(asset='Assets/Resources/ChapterArt/ornament_Old.png', bytes=self.asset.stat().st_size)
        (self.root / 'manifest.json').write_text(json.dumps([self.record]))
        (processor.DEST / 'CornerLayout.json').write_text(json.dumps(dict(entries=[dict(key='ornament_Old', inset=.25)])))

    def snapshot(self):
        return {str(p.relative_to(self.project)): p.read_bytes()
                for p in self.project.rglob('*') if p.is_file() and self.raw not in p.parents}

    def source(self, name):
        im = Image.new('RGB', (128, 128), (0, 255, 0))
        ImageDraw.Draw(im).rectangle((32, 32, 96, 96), fill=(160, 80, 40))
        im.save(self.raw / (name + '.png'))
        (self.raw / (name + '.json')).write_text(json.dumps(dict(id='offline', params=dict(prompt='fixture'))))

    def test_empty_sources_preserve_outputs(self):
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, 'No raw PNGs'):
            processor.process()
        self.assertEqual(before, self.snapshot())

    def test_partial_sources_preserve_other_metadata(self):
        self.source('ornament_New')
        old_sprite = self.asset.read_bytes()
        processor.process()
        records = json.loads((self.root / 'manifest.json').read_text())
        self.assertIn(self.record, records)
        self.assertEqual(2, len(records))
        entries = json.loads((processor.DEST / 'CornerLayout.json').read_text())['entries']
        self.assertIn(dict(key='ornament_Old', inset=.25), entries)
        self.assertEqual(2, len(entries))
        self.assertEqual(old_sprite, self.asset.read_bytes())
        self.assertTrue((self.root / 'review/contact.png').exists())

    def test_later_invalid_source_does_not_publish_earlier_source(self):
        self.source('A_Valid')
        self.source('Z_Invalid')
        Image.new('RGB', (128, 128), 'blue').save(self.raw / 'Z_Invalid.png')
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, 'chroma green'):
            processor.process()
        self.assertEqual(before, self.snapshot())

    def test_missing_job_does_not_publish_sprite(self):
        self.source('New')
        (self.raw / 'New.json').unlink()
        before = self.snapshot()
        with self.assertRaises(FileNotFoundError):
            processor.process()
        self.assertEqual(before, self.snapshot())

    def test_fresh_checkout_fetch_creates_raw_parents(self):
        shutil.rmtree(self.root / 'raw')
        for name in ('generate.py', 'chapters.json', 'prompts.json'):
            shutil.copyfile(HERE / name, self.root / name)
        result = subprocess.run([sys.executable, str(self.root / 'generate.py'), 'fetch'], capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(self.raw.is_dir())


if __name__ == '__main__':
    unittest.main()
