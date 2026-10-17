"""Package only accepted bridge inputs/results and their current refinement."""
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
DELIVERY = ROOT/'examples/bridge-suite'
NATIVE = ROOT/'artifacts/bridge-suite'
reports = sorted(DELIVERY.glob('*/validation.json'))
if len(reports) != 4:
    raise SystemExit('Four validated examples are required')
entries = {}
for file in reports:
    report=json.loads(file.read_text(encoding='utf-8'))
    if not report['Checks'] or not all(c['Passed'] for c in report['Checks']):
        raise SystemExit(f'Failed or missing checks: {file}')
    recipe=json.loads((file.parent/'parameters.json').read_text(encoding='utf-8'))
    for field in ('Id','Title','Spans','Radius','PanelsPerSpan'):
        if recipe[field]!=report['Definition'][field]:
            raise SystemExit(f'Parameters changed since validation: {file}')
    name=report['Definition']['Id'];refined=2*report['Definition']['PanelsPerSpan']
    for phase in ('G1_GETTO','G2_PERMANENTI','Q_TRAFFICO','RITIRO'):
        for sub in (phase,f'mesh-{refined}-{phase}'):
            folder=NATIVE/name/sub
            for required in ('model.st7','model.LSA','input.bgmodel','results.bgmodel'):
                if not (folder/required).is_file():
                    raise SystemExit(f'Missing native result: {folder/required}')
            for f in folder.iterdir():
                if f.is_file(): entries[f'native/{name}/{sub}/{f.name}']=f
    for f in file.parent.iterdir():
        if f.is_file():entries[f'examples/{name}/{f.name}']=f
for f in DELIVERY.iterdir():
    if f.is_file():entries[f'examples/{f.name}']=f
manifest=[dict(path=name,bytes=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for name,f in sorted(entries.items())]
output=ROOT/'artifacts/BridGHe-quattro-ponti-native.zip'
with ZipFile(output,'w',compression=ZIP_DEFLATED,compresslevel=6) as z:
    for name,f in sorted(entries.items()):z.write(f,name)
    z.writestr('manifest.json',json.dumps(manifest,indent=2))
with ZipFile(output) as z:
    assert z.testzip() is None
    for item in manifest:
        assert hashlib.sha256(z.read(item['path'])).hexdigest()==item['sha256']
print(f'{output} | {len(manifest)} files | {output.stat().st_size} bytes | verified SHA-256')
