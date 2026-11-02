"""Generate optional diagnostic wrappers; the shipping P/Invoke source is unchanged.

Run before building Smoke with -p:BridgheTraceApi=true. Declarations and marshalling
come verbatim from the checked-in vendor subset. Fail closed on unfamiliar syntax.
"""
from pathlib import Path
import hashlib
import json
import re

root = Path(__file__).resolve().parents[1]
source_path = root / 'src/BridGHe.Common/Helpers/St7API.cs'
source = source_path.read_text(encoding='utf-8-sig')
pattern = re.compile(r'\[DllImport\("St7API.dll"\)\]\s*public static extern int (\w+)\(([^;]*)\);')
covered = []
excluded = []

def replace(match):
    name, parameters = match.groups()
    if not re.match(r'St7(Set|New|Add|Enable|Disable|Update|Calculate|Assign|Delete|OpenFile|SaveFile|CloseFile)', name):
        excluded.append(name)
        return match.group(0)
    args = []
    names = []
    for parameter in parameters.split(',') if parameters else []:
        p = re.fullmatch(r'\s*(?:(ref) )?(int|byte|double|string|StringBuilder|int\[\]|double\[\]) (\w+)\s*', parameter)
        if not p:
            raise ValueError(f'Unsupported declaration: {name}: {parameter}')
        modifier, _, arg = p.groups()
        args.append(('ref ' if modifier else '') + arg)
        names.append(arg)
    covered.append({'name': name, 'parameters': names})
    values = ', '.join(names)
    ref_values = ', '.join(n for a, n in zip(args, names) if a.startswith('ref '))
    return f'''[DllImport("St7API.dll", EntryPoint = "{name}")]
        private static extern int Native_{name}({parameters});
        public static int {name}({parameters})
        {{
            var before = StrausApiCapture.Snapshot(new object[] {{ {values} }});
            int code = Native_{name}({', '.join(args)});
            StrausApiCapture.Record("{name}", before, code, new object[] {{ {ref_values} }});
            return code;
        }}'''

generated, count = pattern.subn(replace, source)
if count != source.count('public static extern'):
    raise ValueError('Not every native declaration was parsed; review the generator.')
target = root / 'obj/diagnostics'
target.mkdir(parents=True, exist_ok=True)
(target / 'St7API.cs').write_text(generated, encoding='utf-8')
(target / 'coverage.json').write_text(json.dumps({
    'sourceSha256': hashlib.sha256(source_path.read_bytes()).hexdigest(),
    'covered': covered, 'excludedReadOrControlCalls': excluded,
    'semantics': 'Arguments before native mutations, return code, ref scalar outputs. IEEE-754 binary64 in hexadecimal. Includes failed writes; filter code==0 for accepted writes. This is an API transcript, not a persisted-file readback.'
}, indent=2), encoding='utf-8')
print(f'{len(covered)} mutation wrappers; {len(excluded)} read/control calls unchanged')
