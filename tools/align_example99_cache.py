"""Publish only prepared solver caches and two notes into the user's REV004 graph.
Run --prepare99-rev004 first. No geometry, input, connection or component ID is rewritten.
"""
from pathlib import Path
import xml.etree.ElementTree as ET,html,json,hashlib
root=Path(__file__).resolve().parents[1]
target=root/'example/99-Ponte completo GPC/REV004/flusso-acciaio-misto_REV004.ghx'
prepared=root/'artifacts/align99/prepared.ghx'
old=target.read_bytes().decode('utf-8');new=prepared.read_bytes().decode('utf-8')
def objects(text):
    result={}
    for obj in ET.fromstring(text).findall('.//chunk[@name="Object"]'):
        items={i.get('name'):i for i in obj.findall('./chunks/chunk[@name="Container"]/items/item')}
        if 'InstanceGuid' in items:result[items['InstanceGuid'].text]=items
    return result
original=objects(old);updated=objects(new)
ids=['2c52577a-da63-4e52-a6c9-57c0f9a8a0b7','8187f478-4f1f-4b1d-91ef-51b19851099e','0f593523-bfa4-4c07-b68d-fda28f451c7b','2596c64c-16e2-4176-ae57-929880f64ce2']
text=old
for guid in ids:
    at=text.index('>'+guid+'<');start=text.rfind('<chunk name="Object"',0,at);end=text.find('<chunk name="Object"',at)
    if end<0:end=len(text)
    chunk=text[start:end]
    a=original[guid]['Data'].find('stream');b=updated[guid]['Data'].find('stream')
    old_stream=f'<stream length="{a.get("length")}">{a.text}</stream>'
    new_stream=f'<stream length="{b.get("length")}">{b.text}</stream>'
    assert chunk.count(old_stream)==1
    chunk=chunk.replace(old_stream,new_stream)
    old_ids=original[guid]['ElementGuids'].text;new_ids=updated[guid]['ElementGuids'].text
    assert chunk.count('>'+old_ids+'<')==1
    chunk=chunk.replace('>'+old_ids+'<','>'+new_ids+'<')
    text=text[:start]+chunk+text[end:]
notes={
 '2c784b1f-beda-4adf-ac58-6fb7f89284d8':'Ritiro coerente: εcs = -0.0003171439341670765 (circa -317 µε), dagli input correnti del componente.\nLo stesso valore alimenta primario, pre-strain/gradiente FEM ed Excel Actions!I4.\nCache delle quattro analisi invalidate: geometria e ritiro erano precedenti. Ricalcolare Straus.',
 'd727be4e-0fda-450f-b9ed-993ad7e22552':'1 Ricalcola tutte le quattro analisi Straus. 2 Run Excel in lettura. 3 Run Build e Super Element Check. 4 Generate Report.\nRitiro allineato a -317 µε; i risultati precedenti non valgono per la configurazione attuale.\nVerifiche nel piano N-Mx-Vy: T, M2 e V1 trascurati, conservati nei risultati FEM.'}
for guid,note in notes.items():
    item=original[guid]['UserText'];previous=html.escape(item.text,quote=False)
    if text.count(previous)==0:previous=previous.replace("\n","\r\n")
    assert text.count(previous)==1
    text=text.replace(previous,html.escape(note,quote=False),1)
# Restore allowed fields in parsed copies to prove all remaining XML, including wiring, is identical.
a=ET.fromstring(old);b=ET.fromstring(text)
for doc in [a,b]:
 for obj in doc.findall('.//chunk[@name="Object"]'):
    items={i.get('name'):i for i in obj.findall('./chunks/chunk[@name="Container"]/items/item')}
    guid=items.get('InstanceGuid')
    if guid is None:continue
    for key in (['Data','ElementGuids'] if guid.text in ids else ['UserText'] if guid.text in notes else []):
        node=items[key];node.clear();node.text='allowed change'
assert ET.tostring(a)==ET.tostring(b),'Unexpected graph change'
backup=root/'artifacts/align99/flusso-before.ghx'
assert backup.exists() and backup.read_bytes()==target.read_bytes(),'Original changed since preparation; stop and inspect'
target.write_bytes(text.encode('utf-8'))
record={'date':'2026-10-25','epsilonCs':-0.0003171439341670765,'excelReference':'Actions!I4','combinationCoefficientsUnchanged':True,'geometryAndInputsUnchanged':True,'invalidatedAnalysisCaches':ids,'solverInvoked':False,'solverResultsVerified':False,'status':'pending-native','reason':'Current phase models have 73/112/112/112 beams; stored analyses had 37/76/76/76. Reanalyse every phase before Build, checks and report.','sourceBeforeSha256':hashlib.sha256(backup.read_bytes()).hexdigest()}
(target.parent/'alignment-validation.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
print('REV004 PUBLISH PASS: four caches and two explanatory notes replaced; all geometry, inputs, wires and IDs preserved.')
