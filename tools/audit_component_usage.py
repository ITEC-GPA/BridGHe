"""Report repository evidence of use, not whether a component is used in external documents."""
import json
from pathlib import Path
from xml.etree import ElementTree as ET
from current_example_definitions import current_definitions

root = Path(__file__).resolve().parents[1]
items = json.loads((root / 'migration/current-components.json').read_text(encoding='utf-8'))
catalog = json.loads((root / 'example/catalog.json').read_text(encoding='utf-8'))
examples = []
for path in current_definitions(root / 'example', catalog):
    objects = ET.parse(path).find(".//chunk[@name='DefinitionObjects']/chunks")
    if objects is None:
        raise ValueError(f'No Grasshopper objects in {path}')
    guids = {node.text.lower() for node in objects.findall("chunk/items/item[@name='GUID']")}
    examples.append((path, guids))
tests = [(p, p.read_text(encoding='utf-8-sig')) for p in (root / 'tests').rglob('*.cs')]
rows = []
for item in items:
    if item['kind'] != 'component':
        continue
    key = item['type'].split('.')[-1]
    rows.append({
        'name': item['name'], 'type': item['type'], 'guid': item['guid'],
        'panel': item['subcategory'],
        'examples': [p.relative_to(root).as_posix() for p, guids in examples if item['guid'].lower() in guids],
        'namedInTestSources': [p.relative_to(root).as_posix() for p, text in tests if key in text],
    })
rows.sort(key=lambda r: r['name'])
report = {'scope': 'Current GHX definitions explicitly listed in example/catalog.json and explicit type names in test sources. Unlisted personal files and archives are not inspected. Presence is not execution coverage.',
          'exampleCount': len(examples), 'components': rows}
(root / 'docs/component-usage.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
lines = ['# Uso dei componenti BridGHe', '',
         f"Audit di **{len(rows)} componenti** nei **{len(examples)} GHX correnti** elencati in `example/catalog.json`, comprese le definizioni aggiuntive. Copie archiviate e file personali non catalogati sono esclusi. Assenza dagli esempi non significa componente inutile o non utilizzato nei file dell'utente.", '',
         'I riferimenti per nome nei sorgenti dei test indicano un uso esplicito; non sono una misura di copertura. Il caricamento e le icone di tutti i componenti sono controllati separatamente dagli smoke test.', '',
         '## Componenti assenti dagli esempi GHX', '',
         '| Componente | Gruppo nella tab BridGHe | Riferimenti espliciti nei test |', '|---|---|---|']
for r in rows:
    if not r['examples']:
        lines.append(f"| {r['name']} | {r['panel']} | {'Sì' if r['namedInTestSources'] else 'No'} |")
lines += ['', '## Valutazione funzionale', '',
          '- Il vecchio `CompositeSectionComponent_OBSOLETE`, esposto come secondo `Composite Beam Property`, è stato rimosso: il componente attuale fornisce la proprietà composta e le proprietà FEM di acciaio, n0, n∞ e n ritiro. Nessun esempio versionato usa il GUID rimosso.',
          '- `Composite Section Linear Tension Analysis` è il percorso precedente per azioni assegnate a una sezione. Per risultati FEM il percorso corrente usa `Super Element Check`; per sezioni H, H inclinate e cassoni con fasi esplicite usa `Bridge Section Check`. Il vecchio componente resta disponibile: i tre percorsi non sono sostituiti automaticamente perché una sostituzione potrebbe cambiare i risultati.',
          '- `Section Force` e `Composite Section Self Tension` servono per controlli di sezione manuali. Le proprietà geometriche H e composte servono per interrogare e confrontare le rigidezze. Non sono duplicati dell’export.',
          '- `Concrete Slab Section`, `Composite Beam Property` e `Creep And Shrinkage Concrete` servono a costruire le proprietà: gli esempi precedenti salvano spesso questi dati già costruiti nei parametri. Il nuovo esempio 19 contiene esplicitamente i generatori della soletta e della proprietà mista.',
          '- Geometria, file di armature, fasi, opzioni gPC, fatica, armatura trasversale, cassoni, tau, pioli e grafici sono funzioni complementari del verificatore. L’assenza da un GHX non giustifica la loro eliminazione.', '',
          '## Componenti presenti negli esempi GHX', '', '| Componente | Esempi |', '|---|---|']
for r in rows:
    if r['examples']:
        lines.append(f"| {r['name']} | {len(r['examples'])} |")
lines += ['', 'Dettagli riproducibili, GUID e percorsi: [component-usage.json](component-usage.json). Rigenerazione: `py tools/audit_component_usage.py`.', '']
(root / 'docs/USO-COMPONENTI.md').write_text('\n'.join(lines), encoding='utf-8')
print(f"Component usage: {len(rows)} checked; {sum(not r['examples'] for r in rows)} absent from {len(examples)} GHX examples")
