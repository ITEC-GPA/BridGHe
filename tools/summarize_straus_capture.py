"""Summarize native mutation transcripts, including connectivity and quad warpage.

python tools/summarize_straus_capture.py <trace.jsonl.gz> <summary.json>
The geometric metric is the distance of the fourth vertex from the plane through
the largest-area triangle (stable for slender quads), divided by maximum edge.
This validates captured arguments, not a full native-file roundtrip.
"""
import collections
import gzip
import hashlib
import itertools
import json
import math
from pathlib import Path
import struct
import sys

def vector(a, b): return [x-y for x, y in zip(a, b)]
def cross(a, b): return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
def norm(a): return math.sqrt(sum(x*x for x in a))
def double(bits):
    if len(bits) != 16: raise ValueError('Invalid binary64 payload')
    return struct.unpack('>d', bytes.fromhex(bits))[0]

def summarize(path):
    active, models, failed, counts = {}, [], [], collections.Counter()
    last_sequence = 0
    with gzip.open(path, 'rt', encoding='utf-8') as stream:
        for line in stream:
            row = json.loads(line)
            if row['sequence'] != last_sequence + 1: raise ValueError('Missing/repeated trace record')
            last_sequence += 1
            api, args = row['api'], row['before']
            counts[api] += 1
            if row['code']:
                failed.append(row)
                continue
            if api == 'St7NewFile':
                model = {'ordinal': len(models)+1, 'file': args[1], 'nodes': {}, 'elements': {}, 'units': None,
                         'writes': collections.Counter(), 'saved': False, 'sha': hashlib.sha256()}
                models.append(model); active[args[0]] = model
            elif api == 'St7CloseFile':
                active.pop(args[0], None)
                continue
            model = active.get(args[0]) if args else None
            if model is None: continue
            model['writes'][api] += 1
            # IDs internal to a model and exact payloads are preserved; file names/scratch
            # directories and process-local model handle are excluded from this content hash.
            if api not in ('St7NewFile', 'St7SaveFile'):
                model['sha'].update(json.dumps([api, args[1:], row['refs']], separators=(',', ':')).encode())
            if api == 'St7SetUnits': model['units'] = args[1]
            elif api == 'St7SetNodeXYZ': model['nodes'][args[1]] = [double(b) for b in args[2]['doubleBits']]
            elif api == 'St7SetElementConnection':
                connection = args[4]
                model['elements'][(args[1], args[2])] = {'property': args[3], 'nodes': connection[1:1+connection[0]]}
            elif api == 'St7SaveFile': model['saved'] = True
    reports = []
    for model in models:
        plates, warped = [], []
        for (kind, entity), element in model['elements'].items():
            if any(node not in model['nodes'] for node in element['nodes']):
                raise ValueError(f'Missing node for entity {kind}/{entity}')
            if kind != 2: continue
            plates.append(entity)
            if len(element['nodes']) != 4: continue
            points = [model['nodes'][n] for n in element['nodes']]
            triangles = [(a, cross(vector(points[b], points[a]), vector(points[c], points[a])))
                         for a, b, c in itertools.combinations(range(4), 3)]
            origin, normal = max(triangles, key=lambda pair: norm(pair[1]))
            area2 = norm(normal)
            if not area2: raise ValueError(f'Degenerate plate {entity}')
            deviation = max(abs(sum(a*b for a, b in zip(vector(p, points[origin]), normal)))/area2 for p in points)
            scale = max(norm(vector(points[i], points[(i+1) % 4])) for i in range(4))
            warped.append({'id': entity, 'nodeIds': element['nodes'], 'deviationInModelLengthUnits': deviation,
                           'maxEdgeInModelLengthUnits': scale, 'relativeDeviation': deviation/scale})
        reports.append({'ordinal': model['ordinal'], 'file': model['file'], 'units': model['units'],
                        'nodes': len(model['nodes']), 'beams': sum(k[0] == 1 for k in model['elements']),
                        'plates': len(plates), 'quadMetrics': warped, 'saved': model['saved'],
                        'writesByApi': dict(model['writes']), 'inputTranscriptSha256': model['sha'].hexdigest().upper()})
    return {'trace': str(path.resolve()), 'traceSha256': hashlib.sha256(path.read_bytes()).hexdigest().upper(),
            'calls': last_sequence, 'callsByApi': dict(counts), 'failedCalls': failed, 'models': reports,
            'limitations': 'Input transcript, not persisted-file readback. Zero plates means no quad-planarity evidence. Fingerprint preserves API order and native entity IDs.'}

if __name__ == '__main__':
    report = summarize(Path(sys.argv[1]))
    destination = Path(sys.argv[2])
    with destination.open('x', encoding='utf-8') as stream: json.dump(report, stream, indent=2)
    print(f"{report['calls']} calls; {len(report['failedCalls'])} failures; {len(report['models'])} models; "
          f"{sum(m['plates'] for m in report['models'])} plate writes in final connectivity")
