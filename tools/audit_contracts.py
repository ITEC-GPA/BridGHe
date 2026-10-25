"""Audit the current BridGHe source contracts and component/plugin GUIDs."""
from pathlib import Path
import argparse, hashlib, json, re, sys

root = Path(__file__).resolve().parents[1]
tokens = re.compile(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/|\w+|[^\s]')

def contracts(source):
    ts = [t for t in tokens.findall(source) if not t.startswith(('//', '/*'))]
    result = []
    for i, value in enumerate(ts):
        if value not in ('RegisterInputParams', 'RegisterOutputParams'): continue
        if i == 0 or ts[i-1] != 'void': continue
        signature_end = ts.index(')', i)
        start = signature_end + 1
        if ts[start:start+2] == ['=', '>']:
            # Expression-bodied registration ends at its own semicolon, not at
            # the braces of the next method (which could be SolveInstance).
            end = start + 2; depth = 0
            while ts[end] != ';' or depth:
                depth += (ts[end] in ('(', '[', '{')) - (ts[end] in (')', ']', '}'))
                end += 1
            end += 1
        else:
            if ts[start] != '{': raise ValueError('Unknown registration method body')
            end = start + 1; depth = 1
            while depth:
                depth += (ts[end] == '{') - (ts[end] == '}'); end += 1
        result.append([value, ts[start:end]])
    return result

guid_pattern = re.compile(r'(?:ComponentGuid|public override Guid Id)\s*(?:=>|\{[\s\S]*?return)\s*new Guid\("([a-fA-F0-9-]+)"\)')
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--snapshot', action='store_true', help='Record intentionally reviewed interface changes.')
    parser.add_argument('--baseline-only', action='store_true', help='Compatibility alias: the audit always uses the repository baseline.')
    args = parser.parse_args()
    actual = {}; guids = []
    for path in sorted((root/'src/BridGHe.Grasshopper').rglob('*.cs')):
        source = path.read_text(encoding='utf-8-sig')
        ports = contracts(source)
        ids = guid_pattern.findall(source); guids.extend(ids)
        if ports or ids:
            actual[path.relative_to(root).as_posix()] = {'ports': hashlib.sha256(json.dumps(ports, ensure_ascii=False).encode()).hexdigest(), 'guids': ids}
    path = root/'migration/port-contract-baseline.json'
    if args.snapshot:
        path.write_text(json.dumps({'schema':2, 'contracts':actual}, indent=2), encoding='utf-8')
    baseline = json.loads(path.read_text(encoding='utf-8'))
    errors = []
    if baseline.get('schema') != 2: errors.append('Interface baseline schema requires an intentional snapshot.')
    else:
        expected = baseline['contracts']
        for name in sorted(set(actual)|set(expected)):
            if actual.get(name) != expected.get(name): errors.append('Interface/GUID changed: '+name)
    if len(guids) != len(set(guids)): errors.append('Duplicate component/plugin GUIDs')
    report = {'contractFiles':len(actual), 'currentGuids':len(guids), 'errors':errors}
    (root/'artifacts').mkdir(exist_ok=True)
    (root/'artifacts/contract-audit.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2)); sys.exit(bool(errors))

if __name__ == '__main__':
    main()
