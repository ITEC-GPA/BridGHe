"""Copy only hash-verified external results referenced by packaged GHX files."""
import argparse
import hashlib
from pathlib import Path, PureWindowsPath
import shutil
import xml.etree.ElementTree as ET


def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def copy_results(source_examples, packaged_examples):
    source_examples = Path(source_examples).resolve()
    packaged_examples = Path(packaged_examples).resolve()
    dependencies = {}
    for ghx in sorted(packaged_examples.rglob('*.ghx')):
        for items in ET.parse(ghx).iter('items'):
            fields = {item.get('name'): item.text for item in items.findall('item')}
            if 'ResultsFile' not in fields:
                continue
            stored = PureWindowsPath(fields['ResultsFile'])
            # Relocate checkout paths through the example root, never copy arbitrary
            # absolute paths or allow traversal outside the example tree.
            indices = [i for i, part in enumerate(stored.parts) if part.lower() == 'example']
            if not indices:
                raise ValueError(f'{ghx}: results outside example tree: {stored}')
            relative = Path(*stored.parts[indices[-1] + 1:])
            source = (source_examples / relative).resolve()
            target = (packaged_examples / relative).resolve()
            if (source_examples not in source.parents or packaged_examples not in target.parents
                    or source.suffix.lower() != '.bghr'):
                raise ValueError(f'{ghx}: invalid results path: {stored}')
            expected = (fields.get('ResultsHash') or '').lower()
            if len(expected) != 64 or not source.is_file() or digest(source) != expected:
                raise ValueError(f'{ghx}: missing or changed results: {source}')
            if target in dependencies and dependencies[target][1] != expected:
                raise ValueError(f'{ghx}: incompatible results references: {source}')
            dependencies[target] = (source, expected)
    # Validate every dependency before copying any result.
    for target, (source, expected) in dependencies.items():
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        if digest(target) != expected:
            raise ValueError(f'Results changed during packaging: {source}')
    return len(dependencies)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_examples', type=Path)
    parser.add_argument('packaged_examples', type=Path)
    args = parser.parse_args()
    print(f'Packaged external results: {copy_results(args.source_examples, args.packaged_examples)}')
