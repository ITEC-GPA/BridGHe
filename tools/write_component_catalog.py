"""Publish the inventory emitted by the Rhino smoke test; never guesses missing runtime metadata."""
from collections import defaultdict
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
items = json.loads((root / 'artifacts/component-runtime.json').read_text(encoding='utf-8'))
groups = defaultdict(list)
for item in items:
    groups[item['type'].rsplit('.', 1)[0]].append(item)
components = sum(item['kind'] == 'component' for item in items)
lines = ['# Inventario dei componenti 0.1.0', '',
         f'{components} componenti e {len(items)-components} parametri istanziati nel runtime Rhino 8. '
         'GUID completi in `migration/current-components.json`. Nomi e descrizioni delle porte mantenute sono quelli della base.', '']

def ports(values):
    return '; '.join(p['name'].replace('|', '/') for p in values) or '—'

for name, values in sorted(groups.items()):
    lines += ['## ' + name.replace('BridGHe.Grasshopper.', ''), '',
              '| Nome | Tipo | Ingressi | Uscite |', '|---|---|---|---|']
    for item in sorted(values, key=lambda item: item['name']):
        lines.append(f"| {item['name'].replace('|', '/')} | {item['kind']} | {ports(item['inputs'])} | {ports(item['outputs'])} |")
    lines.append('')
(root / 'docs/COMPONENTI.md').write_text('\n'.join(lines), encoding='utf-8')
(root / 'migration/current-components.json').write_text(json.dumps(items, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'Catalog: {components} components, {len(items)-components} parameters')
