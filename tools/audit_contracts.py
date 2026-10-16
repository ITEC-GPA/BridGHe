"""Audit retained GH port registrations and GUID isolation against the read-only source tree."""
from pathlib import Path
import argparse, hashlib, json, re, sys

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--source',type=Path,default=root.parent/'FeMM 30-5-2024'/'Test1'/'FeMM')
parser.add_argument('--snapshot',action='store_true',help='Freeze the original port contracts; requires the source tree.')
parser.add_argument('--baseline-only',action='store_true',help='Audit without access to the original repository.')
args=parser.parse_args()
token=re.compile(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*[\s\S]*?\*/|\w+|[^\s]')
def tokens(text):
    return [t for t in token.findall(text.replace('FeMM.','BridGHe.').replace('StrausHelper.Solvers','StrausR3Helper.Solvers').replace('RhinoDoc.ActiveDoc.ModelAbsoluteTolerance','Settings.Instance.Tolerance')) if not t.startswith(('//','/*'))]
def method(text,name):
    ts=tokens(text)
    if name not in ts:return None
    i=ts.index(name);a=ts.index('{',i);d=1;b=a+1
    while d:
        d+=(ts[b]=='{')-(ts[b]=='}');b+=1
    return ts[a:b]
mapping=json.loads((root/'migration/component-map.json').read_text(encoding='utf-8-sig'))
baseline_path=root/'migration/port-contract-baseline.json'
baseline=json.loads(baseline_path.read_text(encoding='utf-8')) if baseline_path.exists() else {'contracts':{},'originalGuids':[]}
use_source=args.source.exists() and not args.baseline_only
if args.snapshot and not use_source:parser.error('The original source tree is required to create the baseline.')
def digest(value):
    return hashlib.sha256(json.dumps(value,ensure_ascii=False,separators=(',',':')).encode('utf-8')).hexdigest()
kept=[];removed=[];errors=[];mecheck=0
for item in mapping:
    target=root/item['target']
    if not target.exists():removed.append(item);continue
    old=(args.source/item['source']).read_text(encoding='utf-8-sig') if use_source else None
    new=target.read_text(encoding='utf-8-sig')
    for name in ('RegisterInputParams','RegisterOutputParams'):
        key=item['source']+' / '+name
        expected=digest(method(old,name)) if use_source else baseline['contracts'].get(key)
        if args.snapshot:baseline['contracts'][key]=expected
        if expected!=digest(method(new,name)):errors.append(f"Port contract changed: {item['target']} / {name}")
    if item['oldGuid']==item['newGuid'] or item['newGuid'] not in new:errors.append('GUID migration failed: '+item['target'])
    kept.append(item)
    if '/MeCheck2/' in item['target']:mecheck+=1
if mecheck!=16:errors.append(f'MeCheck2 count {mecheck}, expected 16')
guid_pattern=re.compile(r'(?:ComponentGuid|public override Guid Id)\s*(?:=>|\{[\s\S]*?return)\s*new Guid\("([a-fA-F0-9-]+)"\)')
guids=[]
for p in (root/'src/BridGHe.Grasshopper').rglob('*.cs'):
    if 'obj' not in p.parts and 'bin' not in p.parts:guids.extend(guid_pattern.findall(p.read_text(encoding='utf-8-sig')))
old_guids=set(baseline['originalGuids'])
if use_source:
    for p in (args.source/'FeMM.Grasshopper').rglob('*.cs'):old_guids.update(re.findall(r'new Guid\("([a-fA-F0-9-]+)"\)',p.read_text(encoding='utf-8-sig')))
if args.snapshot:
    baseline['originalGuids']=sorted(old_guids)
    baseline_path.write_text(json.dumps(baseline,indent=2),encoding='utf-8')
if len(guids)!=len(set(guids)):errors.append('Duplicate current component/plugin GUIDs')
if set(guids)&old_guids:errors.append('Current GUID collides with original plugin')
report={'retained':len(kept),'removedAfterInitialCopy':len(removed),'mecheck2':mecheck,'currentGuids':len(guids),'errors':errors}
(root/'artifacts').mkdir(exist_ok=True)
(root/'artifacts/contract-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2));sys.exit(bool(errors))
