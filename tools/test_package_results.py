import hashlib
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
from package_results import copy_results


class PackageResultsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source' / 'example'
        self.package = self.root / 'package' / 'example'
        self.package.mkdir(parents=True)
        self.relative = Path('50-demo/calcoli/M01.bghr')
        self.result = self.source / self.relative
        self.result.parent.mkdir(parents=True)
        self.result.write_bytes(b'fixture results')
        self.expected = hashlib.sha256(self.result.read_bytes()).hexdigest()

    def reference(self, stored=r'C:\old checkout\example\50-demo\calcoli\M01.bghr', expected=None):
        root = ET.Element('archive')
        items = ET.SubElement(root, 'items')
        ET.SubElement(items, 'item', name='ResultsFile').text = stored
        ET.SubElement(items, 'item', name='ResultsHash').text = expected or self.expected
        ET.ElementTree(root).write(self.package / 'demo.ghx')

    def test_relocated_results_only(self):
        self.reference()
        (self.result.parent / 'large.LSA').write_bytes(b'not needed')
        self.assertEqual(copy_results(self.source, self.package), 1)
        self.assertEqual((self.package / self.relative).read_bytes(), self.result.read_bytes())
        self.assertFalse((self.package / self.relative.parent / 'large.LSA').exists())

    def test_missing(self):
        self.reference()
        self.result.unlink()
        with self.assertRaises(ValueError):
            copy_results(self.source, self.package)

    def test_changed(self):
        self.reference()
        self.result.write_bytes(b'changed')
        with self.assertRaises(ValueError):
            copy_results(self.source, self.package)

    def test_escape(self):
        self.reference(r'C:\old\example\..\outside.bghr')
        with self.assertRaises(ValueError):
            copy_results(self.source, self.package)

    def test_no_dependencies(self):
        (self.package / 'demo.ghx').write_text('<archive/>')
        self.assertEqual(copy_results(self.source, self.package), 0)


if __name__ == '__main__':
    unittest.main()
