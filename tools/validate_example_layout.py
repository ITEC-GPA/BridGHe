"""Validate numbered examples and local documentation links without solver access."""
import json
import re
from pathlib import Path
from urllib.parse import unquote
from xml.etree import ElementTree as ET
from current_example_definitions import current_definitions

ROOT = Path(__file__).resolve().parents[1]
EXAMPLES = ROOT / 'example'
EXCLUDED = {'calcoli', 'archivio-pacchetti', 'backup-prima-fasi', 'esecuzioni'}


def main():
    catalog = json.loads((EXAMPLES / 'catalog.json').read_text(encoding='utf-8'))
    errors = []
    definitions = 0
    links = 0
    numbers = [row['number'] for row in catalog]
    if numbers != sorted(set(numbers)) or any(n <= 0 for n in numbers):
        errors.append('Catalog must contain ordered positive example numbers exactly once')
    if any(not row['id'].startswith(f"{row['number']:02d}-") for row in catalog):
        errors.append('Example directory prefixes must match their stable catalog numbers')
    expected = {'00-documentazione'} | {row['id'] for row in catalog}
    actual = {p.name for p in EXAMPLES.iterdir() if p.is_dir()}
    if expected != actual:
        errors.append(f'Unexpected or missing directories: {expected ^ actual}')
    if (ROOT / 'examples').exists():
        errors.append('Obsolete examples directory still exists')
    for row in catalog:
        if not (EXAMPLES / row['readme']).is_file():
            errors.append(f"Missing instructions: {row['id']}")
    files = current_definitions(EXAMPLES, catalog)
    for file in files:
        try:
            text = file.read_text(encoding='utf-8-sig')
            xml = ET.fromstring(text)
            count = xml.find(".//chunk[@name='DefinitionObjects']/items/item[@name='ObjectCount']")
            if count is None or int(count.text) <= 0:
                errors.append(f'No Grasshopper objects in {file}')
            if re.search(r'BridGHe[\\/](?:examples[\\/]|artifacts[\\/](?:bridge-grillage|bridge-suite|bridge-workflow|gh-examples))', text):
                errors.append(f'Stale repository path in {file}')
            definitions += 1
        except (OSError, ET.ParseError) as exc:
            errors.append(f'{file}: {exc}')
    docs = [ROOT / 'README.md', *sorted((ROOT / 'docs').glob('*.md')), *sorted(EXAMPLES.rglob('*.md'))]
    for file in docs:
        if any(part in EXCLUDED for part in file.relative_to(ROOT).parts):
            continue
        text = file.read_text(encoding='utf-8-sig')
        for target in re.findall(r'\]\(([^)]+)\)', text):
            if re.match(r'[a-zA-Z]+:|#', target):
                continue
            path = unquote(target.partition('#')[0])
            if not (file.parent / path).exists():
                errors.append(f'Broken link in {file.relative_to(ROOT)}: {target}')
            links += 1
    expected_definitions = len(files)
    if definitions != expected_definitions:
        errors.append(f'Expected {expected_definitions} current GHX definitions, found {definitions}')
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'EXAMPLE LAYOUT PASS: {len(catalog)} examples, {definitions} GHX, {links} local links')


if __name__ == '__main__':
    main()
