"""Package one successful run of examples 01-10, preserving the example layout."""
import json
import re
import sys
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parents[1] / 'example'
run = sys.argv[1] if len(sys.argv) == 2 else ''
if not re.fullmatch(r'\d{8}-\d{6,9}', run):
    raise SystemExit('Expected a native run timestamp')
report = root / '00-documentazione' / 'validazioni' / 'esecuzioni' / run / 'report.json'
data = json.loads(report.read_text(encoding='utf-8-sig'))
if data['Count'] != 10 or data['Failures'] != 0 or not all(e['Passed'] for e in data['Examples']):
    raise SystemExit('Refusing to package an incomplete or failed run')
files = [report]
for case in data['Examples']:
    folder = root / case['Id'] / 'calcoli' / 'native' / run
    for name in ('model.st7', 'input.bgmodel', 'results.bgmodel', 'report.json'):
        if not (folder / name).is_file():
            raise SystemExit(f'Missing native file: {folder / name}')
    files.extend(p for p in folder.rglob('*') if p.is_file())
output = root / '00-documentazione' / 'archivio-pacchetti' / 'BridGHe-native-examples.zip'
output.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(output, 'w', compression=ZIP_DEFLATED) as archive:
    for file in sorted(files):
        archive.write(file, 'example/' + file.relative_to(root).as_posix())
with ZipFile(output) as archive:
    assert archive.testzip() is None
print(output)
