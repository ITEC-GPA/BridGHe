"""Package only accepted bridge inputs/results and their current refinement."""
import hashlib
import json
import sys
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
DELIVERY = ROOT/'example'
spine = '--spine' in sys.argv
reports = sorted(DELIVERY.glob('1[678]-*/validation.json' if spine else '1[2345]-*/validation.json'))
if len(reports) != (3 if spine else 4):
    raise SystemExit('Missing validated examples')
entries = {}
for file in reports:
    report=json.loads(file.read_text(encoding='utf-8'))
    if not report['Checks'] or not all(c['Passed'] for c in report['Checks']):
        raise SystemExit(f'Failed or missing checks: {file}')
    recipe=json.loads((file.parent/'parameters.json').read_text(encoding='utf-8'))
    if spine:
        if recipe['Central']!=report['Section'] or recipe['Lateral']!=report['LateralSection']:
            raise SystemExit(f'Section parameters changed since validation: {file}')
        recipe=recipe['Definition']
    for field in ('Id','Title','Spans','Radius','PanelsPerSpan'):
        if recipe[field]!=report['Definition'][field]:
            raise SystemExit(f'Parameters changed since validation: {file}')
    name=report['Definition']['Id'];refined=2*report['Definition']['PanelsPerSpan']
    for phase in ('G1_GETTO','G2_PERMANENTI','Q_TRAFFICO','RITIRO'):
        for sub in (phase,f'mesh-{refined}-{phase}'):
            folder=DELIVERY/name/'calcoli'/sub
            for required in ('model.st7','model.LSA','input.bgmodel','results.bgmodel'):
                if not (folder/required).is_file():
                    raise SystemExit(f'Missing native result: {folder/required}')
            for f in folder.iterdir():
                if f.is_file(): entries[f'example/{name}/calcoli/{sub}/{f.name}']=f
    for f in file.parent.iterdir():
        if f.is_file():entries[f'example/{name}/{f.name}']=f
for f in (DELIVERY/'00-documentazione').iterdir():
    if f.is_file():entries[f'example/00-documentazione/{f.name}']=f
manifest=[dict(path=name,bytes=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for name,f in sorted(entries.items())]
output=DELIVERY/'00-documentazione'/'archivio-pacchetti'/('BridGHe-spina-centrale-native.zip' if spine else 'BridGHe-quattro-ponti-native.zip')
output.parent.mkdir(parents=True,exist_ok=True)
with ZipFile(output,'w',compression=ZIP_DEFLATED,compresslevel=6) as z:
    for name,f in sorted(entries.items()):z.write(f,name)
    z.writestr('manifest.json',json.dumps(manifest,indent=2))
with ZipFile(output) as z:
    assert z.testzip() is None
    for item in manifest:
        assert hashlib.sha256(z.read(item['path'])).hexdigest()==item['sha256']
print(f'{output} | {len(manifest)} files | {output.stat().st_size} bytes | verified SHA-256')
