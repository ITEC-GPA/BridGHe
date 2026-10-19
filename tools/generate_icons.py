"""Generate original, editable SVG icons on the Grasshopper 24 px grid.

No legacy bitmap is edited. Render with render_icons.cjs (sharp); generated PNGs
are checked in so building the plugin never needs Python, Node or an SVG engine.
"""
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'assets/icons'
OUT.mkdir(parents=True, exist_ok=True)
INK = '#203b50'
PALETTE = {
    'model': ('#137f86', '#e8f5f4'),
    'section': ('#316ca8', '#edf3fa'),
    'material': ('#526778', '#eff3f5'),
    'load': ('#bd5b19', '#fff2e5'),
    'moving': ('#ba6120', '#fff2e5'),
    'solver': ('#6751ad', '#f1edfa'),
    'check': ('#27734b', '#eaf6ee'),
    'result': ('#7e55a2', '#f5eff8'),
    'tool': ('#536977', '#eff3f5'),
}

def path(d, color=INK, width=1.6, fill='none', **attrs):
    rest = ' '.join(f'{k.replace("_", "-")}="{v}"' for k, v in attrs.items())
    return f'<path d="{d}" fill="{fill}" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round" {rest}/>'

def rect(x, y, w, h, fill, radius=0, stroke='none', sw=1):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{radius}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'

def circle(x, y, r, fill, stroke='none', sw=1.5):
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"/>'

def arrow(x, y, u, v, color):
    # Explicit arrowheads stay legible at 24 px (no marker scaling).
    import math
    a = math.atan2(v-y, u-x)
    points = [(u-3*math.cos(a+d), v-3*math.sin(a+d)) for d in (-0.65, 0.65)]
    return path(f'M{x} {y} L{u} {v} M{points[0][0]:.2f} {points[0][1]:.2f} L{u} {v} L{points[1][0]:.2f} {points[1][1]:.2f}', color)

def profile(kind, accent, rounded=False):
    contours = {
        'I': 'M5 4H19V7H14V17H19V20H5V17H10V7H5Z',
        'T': 'M4 4H20V8H14V20H10V8H4Z',
        'Angle': 'M5 4H9V16H20V20H5Z',
        'Channel': 'M5 4H19V8H9V16H19V20H5Z',
        'LipChannel': 'M5 4H19V10H16V7H8V17H16V14H19V20H5Z',
        'TopHat': 'M3 17H6V5H18V17H21V20H15V8H9V20H3Z',
        'Z': 'M4 4H14V16H20V20H10V8H4Z',
        'TrapezoidSolid': 'M8 4H16L21 20H3Z',
        'TrapezoidHollow': 'M8 4H16L21 20H3Z M10 8L6.8 17H17.2L14 8Z',
        'TriangleSolid': 'M12 3L21 20H3Z',
        'TriangleHollow': 'M12 3L21 20H3Z M12 10L7.5 17H16.5Z',
        'Cruciform': 'M10 4H14V10H20V14H14V20H10V14H4V10H10Z',
        'Bulb': 'M6 4H10V15C15 10 20 12 20 16C20 19 16 21 12 20H6Z',
        'Brep': 'M5 4H16V9H20V16H14V20H4V12H8V8H5Z M10 11H14V15H10Z',
    }
    if kind == 'CircularSolid': return circle(12, 12, 8, accent)
    if kind == 'CircularHollow': return circle(12, 12, 7, 'none', accent, 3)
    if kind == 'RectangleSolid': return rect(4, 5, 16, 14, accent, 1)
    if kind == 'RectangleHollow': return rect(5, 5, 14, 14, 'none', 3 if rounded else 0.5, accent, 3)
    if kind == 'Generic':
        return path('M5 5H18L20 13L14 20H4Z M9 9V15H14V9Z', accent, 0, accent, fill_rule='evenodd')
    d = contours[kind]
    return path(d, accent, 1 if rounded else 0, accent, fill_rule='evenodd')

def glyph(name, a):
    if name == 'studs':
        return rect(3,17,18,4,INK) + path('M7 17V7M17 17V7M4 6H10M14 6H20',a,2.5)
    if name == 'stiffener':
        return path('M4 4H20M4 20H20M12 4V20',INK,2) + path('M7 6V18M17 6V18M7 12H17',a,2)
    if name.startswith('section:'): return profile(name.split(':')[1], a)
    if name.startswith('bgl:'): return profile(name.split(':')[1], a, True)
    if name == 'beam':
        return path('M5 17L19 6', a, 3) + circle(5, 17, 2.2, 'white', INK) + circle(19, 6, 2.2, 'white', INK)
    if name == 'node':
        return path('M12 4V20M4 12H20', a) + circle(12, 12, 3.5, 'white', INK, 2)
    if name == 'plate':
        return path('M4 8L15 4L20 16L9 20Z', a, 1.6, 'white') + path('M4 8L20 16M9 20L15 4', a, 1)
    if name == 'mesh':
        return glyph('plate', a) + path('M6.5 14L17.5 10M9.5 6L14.5 18', a, 1)
    if name == 'model':
        return path('M4 17L8 5L20 7L17 20ZM4 17L20 7M8 5L17 20', a, 1.5) + ''.join(circle(x,y,1.8,'white',INK,1.2) for x,y in [(4,17),(8,5),(20,7),(17,20)])
    if name == 'bridge':
        return path('M3 14H21M5 14V19M19 14V19M6 14Q12 2 18 14M9 8V14M15 8V14', a, 2)
    if name == 'composite':
        return rect(3,4,18,4,a,0.7) + path('M6 10H18M12 10V19M6 19H18',INK,2.8)
    if name == 'slab':
        return rect(3,6,18,11,'white',1,a,1.8) + ''.join(circle(x,9,1.2,a) for x in [6,10,14,18]) + path('M5 15H19',a,1)
    if name in ('steel','aluminium','concrete','material'):
        out = path('M4 8L12 4L20 8V17L12 21L4 17ZM4 8L12 12L20 8M12 12V21', a, 1.5, 'white')
        if name=='steel': out+=path('M6 11L10 13M8 12V16M6 15L10 17',INK,1.3)
        if name=='aluminium': out+=path('M15 5L15 1M13 3H17',a,1.4)
        if name=='concrete': out+=''.join(circle(x,y,0.85,a) for x,y in [(6,11),(9,14),(6,16),(16,12),(18,16)])
        if name=='material': out+=path('M6 13L10 15',INK,1.5)
        return out
    if name == 'table':
        return rect(4,4,16,16,'white',1,a,1.4) + rect(4,4,16,4,a,0.5) + path('M4 12H20M4 16H20M10 8V20',a,1)
    if name == 'axes':
        return arrow(6,18,20,18,a)+arrow(6,18,6,4,a)+arrow(6,18,15,8,INK)
    if name == 'group':
        return path('M6 8L18 8L12 18Z',a,1.2)+''.join(rect(x-3,y-3,6,6,'white',1,a,1.6) for x,y in [(6,8),(18,8),(12,18)])
    if name == 'case':
        return path('M5 3H14L19 8V21H5ZM14 3V8H19',a,1.6,'white') + path('M8 12H16M8 16H13',a,1.6)
    if name == 'stages':
        return rect(4,15,4,5,a,0.5)+rect(10,10,4,10,a,0.5)+rect(16,4,4,16,a,0.5)
    if name == 'ruler':
        return path('M4 16L16 4L21 9L9 21ZM8 12L11 15M12 8L15 11M16 4L19 7',a,1.5,'white')
    if name == 'release':
        return path('M3 12H9M15 12H21',INK,3) + circle(12,12,3,'white',a,1.8)
    if name == 'taper': return path('M4 5L20 10V15L4 20Z',a,1.7,'white')
    if name == 'support':
        return path('M12 5L5 17H19ZM3 20H21M5 20L3 22M10 20L8 22M15 20L13 22M20 20L18 22',a,1.5,'white') + circle(12,5,2,'white',INK,1.4)
    if name == 'spring':
        return path('M12 3V6L7 8L17 11L7 14L17 17L12 19V21M7 21H17',a,1.7)
    if name == 'patch':
        return glyph('plate',a)+path('M9 10L13 8L16 14L12 16Z',a,0,a)
    if name == 'stations':
        return path('M3 12H21',INK,2.5)+''.join(path(f'M{x} 7V17',a,1.4) for x in [5,10,15,20])
    if name == 'id': return path('M9 4L7 20M17 4L15 20M4 9H21M3 15H20',a,2)
    if name in ('point','distributed','gradient','preload'):
        out=path('M3 18H21',INK,2.5)
        if name=='point': out+=arrow(12,3,12,15,a)
        if name=='distributed': out+=path('M4 4H20',a,1.2)+''.join(arrow(x,4,x,15,a) for x in [5,12,19])
        if name=='gradient': out+=path('M4 11L20 4',a,1.2)+''.join(arrow(x,y,x,15,a) for x,y in [(5,11),(12,8),(19,5)])
        if name=='preload': out+=arrow(3,10,9,10,a)+arrow(21,10,15,10,a)+path('M10 6V14M14 6V14',INK,1.4)
        return out
    if name == 'moment': return path('M4 17H20',INK,2.5)+path('M7 12A6 6 0 1 1 16 12M16 8V12H20',a,1.9)
    if name == 'pressure': return path('M3 16L15 12L21 16L9 20Z',INK,1.5,'white')+arrow(6,3,6,12,a)+arrow(16,2,16,10,a)
    if name == 'temperature':
        return path('M10 15V5A2 2 0 0 1 14 5V15A4 4 0 1 1 10 15Z',a,1.5,'white')+path('M12 8V17',a,2)+circle(12,18,1.8,a)+path('M17 6H20M17 10H19',a,1.3)
    if name == 'vehicle':
        return path('M3 7H13V15H3ZM13 10H17L21 14V17H3V15M17 10V14H21',a,1.5,'white')+circle(7,17,2,INK)+circle(17,17,2,INK)
    if name == 'route': return path('M4 19C17 20 4 6 17 5H20M17 2L20 5L17 8',a,2)+circle(4,19,2,'white',INK,1.3)
    if name == 'centrifugal': return path('M4 20A15 15 0 0 1 20 4',INK,2)+arrow(12,12,21,15,a)+circle(12,12,2,'white',a,1.5)
    if name == 'template': return rect(3,4,18,16,'white',1,a,1.4)+path('M4 9H20M4 14H20',a,1)+rect(6,6,5,2,a,0.5)+rect(12,11,5,2,a,0.5)+rect(8,16,6,2,a,0.5)
    if name == 'library': return rect(3,5,4,15,a,0.5)+rect(9,5,4,15,a,0.5)+path('M16 5L20 4L23 19L19 20Z',a,0,a)+path('M4 9H6M10 9H12M18 8L20 8', 'white',1.2)
    if name == 'solver': return glyph('model',a)
    if name == 'chart': return path('M4 4V20H21',INK,1.4)+path('M6 16L10 10L14 13L20 5',a,2)+circle(10,10,1.4,a)
    if name == 'shear': return path('M4 12H20M4 6H12V18H20',a,1.8)+path('M4 12V6M20 12V18',a,1)
    if name == 'stress': return path('M4 4V20M20 12H4M6 5L18 19V12H6Z',a,1.5,'white')
    if name == 'clock': return circle(12,12,8,'white',a,1.7)+path('M12 6V12L16 14',INK,1.8)
    if name == 'strain': return path('M4 8H20M4 16H20M8 6V18M16 6V18',INK,1.3)+arrow(9,12,3,12,a)+arrow(15,12,21,12,a)
    if name == 'printer': return rect(3,9,18,10,a,2)+rect(7,3,10,7,'white',0.5,a,1.4)+rect(7,14,10,7,'white',0.5,a,1.4)+path('M9 17H15',a,1.2)+circle(18,12,0.8,'white')
    if name == 'filter': return path('M3 4H21L14 12V19L10 21V12Z',a,1.6,'white')
    if name == 'split': return path('M4 5H20V19H4ZM4 19L20 5',a,1.5,'white')+path('M12 3V21',INK,1.5,stroke_dasharray='2 2')
    if name == 'merge': return arrow(3,5,12,12,a)+arrow(3,19,12,12,a)+arrow(12,12,21,12,a)
    if name == 'decompose': return arrow(3,12,11,12,a)+arrow(11,12,20,4,a)+arrow(11,12,20,20,a)
    if name == 'mass': return path('M7 8H17L21 20H3ZM10 8V5A2 2 0 0 1 14 5V8',a,1.6,'white')
    if name == 'bars': return rect(4,4,16,16,'white',1,a,1.5)+''.join(circle(x,y,1.7,a) for x,y in [(7,7),(17,7),(7,17),(17,17)])
    raise KeyError(name)

def badge(kind, accent):
    if not kind: return ''
    marks = {
        'add': path('M19 16V22M16 19H22','white',1.5),
        'check': path('M16 19L18 21L22 17','white',1.6),
        'arrow': arrow(16,19,22,19,'white'),
        'play': path('M17 16L22 19L17 22Z','white',0,'white'),
        'data': path('M16.5 17H21.5M16.5 19H21.5M16.5 21H20','white',1.2),
        'filter': path('M16 16.5H22L20 19V21L18 22V19Z','white',0,'white'),
        'measure': path('M16 21L21 16M17 20L18 21M19 18L20 19','white',1.3),
        'clock': circle(19,19,2.8,'none','white',1)+path('M19 17V19L21 20','white',1),
        'parameter': circle(19,19,2,'white'),
        'native': path('M19 15.5L22.5 19L19 22.5L15.5 19Z','white',0,'white'),
        'radii': path('M16.5 21V19A2.5 2.5 0 0 1 19 16.5H21','white',1.7),
        'edit': path('M16 21L17 18L20.5 15L23 17.5L19 21Z','white',0,'white'),
        'stress': path('M16.5 16V22M17 16L22 21H17','white',1.1),
        'force': arrow(19,15.5,19,22,'white'),
        'heat': path('M17 22C14 19 20 19 18 16M21 22C18 19 23 19 21 16','white',1.2),
        'stages': path('M16 21V19H18V17H20V15H22','white',1.3),
        'library': path('M16 16V22M19 16V22M22 16V22','white',1.4),
        'link': path('M16 20L18 18M20 20L22 18M18 20L20 18','white',1.7),
        'shape': path('M16 16H22V22H16Z','white',1),
        'support': path('M19 16L16 21H22ZM16 22H22','white',1),
        'axes': arrow(17,21,22,21,'white')+arrow(17,21,17,16,'white'),
    }
    return circle(19,19,4.65,'white')+circle(19,19,3.95,accent)+marks[kind]

def icon_svg(symbol, family, mark, title, parameter=False):
    accent, pale = PALETTE[family]
    frame = rect(0.75,0.75,22.5,22.5,pale,5,accent,0.6)
    drawing = glyph(symbol,accent)
    if mark: drawing=f'<g transform="translate(0.25 0.25) scale(.86)">{drawing}</g>'
    if parameter: frame=rect(0.75,0.75,22.5,22.5,'#ffffff',5,accent,1)
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><title>{html.escape(title)}</title>{frame}{drawing}{badge(mark,accent)}</svg>\n'

# Each row is an explicit semantic assignment, never a generic unknown icon.
ROWS = '''
AlignBeamAxesComponent|beam|axes|model
AlignPlateAxesComponent|plate|axes|model
BeamFilterComponent|beam|filter|tool
BeamOverrideComponent|beam|edit|tool
MovingStaticAnalysisComponent|vehicle|play|solver
StrausR3AnalysisComponent|solver|play|solver
BeamOutputStationsComponent|stations||model
BeamReleasesComponent|release||model
BeamStringGroupComponent|beam|link|model
BeamSupportComponent|beam|support|model
BeamTaperComponent|taper||model
ElementIdComponent|id||model
LoadPatchTypeComponent|patch||load
NodeRestraintsComponent|support||model
NodeStiffnessComponent|spring||model
PlateSupportComponent|plate|support|model
SuperElementComponent|bridge|link|model
ForceComponent|axes|force|load
GroupComponent|group||model
LoadCaseComponent|case||load
LoadCombinationComponent|case|add|load
StagedConstructionComponent|stages||model
BeamGeometricPropertyComponent|beam|measure|section
BeamPropertyComponent|beam|data|section
PlatePropertyComponent|plate|data|section
StrausR3ExportComponent|case|arrow|solver
BeamDistributedLoadComponent|distributed||load
BeamDistributedMomentComponent|moment|stages|load
BeamGradientLoadComponent|gradient||load
BeamPointLoadComponent|point||load
BeamPointMomentComponent|moment||load
BeamPreLoadComponent|preload||load
MovingPointComponent|point|arrow|moving
MovingDistributedComponent|distributed|arrow|moving
MovingHeatComponent|temperature|arrow|moving
MovingVehicleComponent|vehicle||moving
MovingCentrifugalComponent|centrifugal||moving
MovingTemplateComponent|template||moving
MovingLibraryTemplateComponent|template|library|moving
MovingPathComponent|route||moving
AttachMovingPathsComponent|model|link|moving
NodeDisplacementLoadComponent|node|arrow|load
NodeLoadComponent|node|force|load
NodeMomentComponent|moment|parameter|load
NodeTemperatureComponent|temperature|parameter|load
PlateNSMassComponent|mass|shape|load
PlatePressureComponent|pressure||load
PlatePreStressComponent|plate|stress|load
MaterialAluminiumComponent|aluminium||material
MaterialConcreteComponent|concrete||material
MaterialCustomComponent|material|edit|material
MaterialSteelComponent|steel||material
MaterialTableComponent|table||material
BeamPropertyCompositeComponent|composite|data|check
BridgeGeometryComponent|composite|native|check
BridgeRebarRowComponent|bars|add|check
BridgePhaseComponent|stages|native|check
BridgeAnalysisOptionsComponent|table|edit|check
BridgeTransverseReinforcementComponent|slab|force|check
BridgeFatigueComponent|studs|clock|check
BridgeBoxOptionsComponent|section:RectangleHollow|check|check
BridgeSectionCheckComponent|composite|check|check
BridgeShearResultsComponent|shear|data|result
BridgeStudResultsComponent|studs|data|result
BridgeVerificationResultsComponent|table|check|result
BridgeShearPlotComponent|shear|measure|result
BridgeStudPlotComponent|studs|measure|result
BridgeStudsComponent|studs||check
BridgeIntermediateStiffenerComponent|stiffener||check
BridgeSupportStiffenerComponent|stiffener|force|check
BridgeDetailCheckComponent|studs|check|check
BeamSectionConcreteSlabComponent|slab||check
BeamSectionHPropertyComponent|section:I|data|check
BuildBridgeModel|model|check|check
CompositeBeamComponent|bridge|shape|check
CompositeBeamLinearTensionAnalysisComponent_OBSOLETE|composite|play|check
CompositeBeamSelfTensionComponent|composite|heat|check
CompositeBeamSlabStrainComponent|strain||check
CompositeBeamTensionResultComponent|stress|data|check
CompositeSectionComponent_OBSOLETE|composite|add|check
CompositeSectionGeometricPropertyComponent|composite|measure|check
CreepAndShrinkageConcreteComponent|slab|clock|check
BridgeForcePlotComponent|printer|force|check
BridgeResultPlotComponent|printer|stress|check
SuperElementCheckComponent|bridge|check|check
SuperElementFilterComponent|bridge|filter|check
BeamComponent|beam||model
BuildModelComponent|model|add|model
DecomposeModelComponent|decompose||model
MergeModelComponent|model|link|model
NodeComponent|node||model
PlateComponent|plate||model
MergeGeometryComponent|merge||tool
BeamParam|beam|parameter|model
BeamSectionParam|section:I|parameter|section
CoordinateSystemParam|axes|parameter|model
GroupParam|group|parameter|model
LoadCombinationsParam|case|parameter|load
MaterialParam|material|parameter|material
NodeParam|node|parameter|model
PlateParam|plate|parameter|model
BeamResultsComponent|beam|data|result
NodeResultsComponent|node|data|result
PlateResultsComponent|plate|data|result
BeamSectionAngleComponent|section:Angle||section
BeamSectionBrepComponent|section:Brep||section
BeamSectionCComponent|section:Channel||section
BeamSectionGenericComponent|section:Generic|data|section
BeamSectionHollowCircleComponent|section:CircularHollow||section
BeamSectionHollowRectComponent|section:RectangleHollow||section
BeamSectionIComponent|section:I||section
BeamSectionSolidCircleComponent|section:CircularSolid||section
BeamSectionSolidRectComponent|section:RectangleSolid||section
BeamSectionTComponent|section:T||section
SectionGeometricComponent|section:Brep|shape|section
SectionGeometricPropertyComponent|section:Generic|measure|section
StrausBxsSectionComponent|section:Brep|native|section
StrausLibrarySectionComponent|library|native|section
ElementFilterComponent|model|filter|tool
NodeFilterComponent|node|filter|tool
PlateFilterComponent|plate|filter|tool
RemeshComponent|mesh|edit|tool
SurfaceSplitterComponent|split||tool
'''
mapping = {r[0]:r[1:] for line in ROWS.strip().splitlines() if (r:=line.split('|'))}
for shape in ['CircularSolid','CircularHollow','RectangleSolid','RectangleHollow','LipChannel','TopHat','I','T','Angle','Z','TrapezoidSolid','TrapezoidHollow','TriangleSolid','TriangleHollow','Cruciform']:
    mapping[f'Straus{shape}SectionComponent']=[f'section:{shape}','native','section']
for shape in ['Rectangle','I','Channel','T','Angle','Bulb']:
    mapping[f'StrausBgl{shape}SectionComponent']=[f'bgl:{"RectangleHollow" if shape=="Rectangle" else shape}','radii','section']

items=json.loads((ROOT/'migration/current-components.json').read_text(encoding='utf-8'))
assert set(mapping)=={i['type'].split('.')[-1] for i in items}, 'Icon map must cover exactly the runtime inventory.'
manifest=[]
for item in items:
    key=item['type'].split('.')[-1]
    symbol,mark,family=mapping[key]
    svg=icon_svg(symbol,family,mark,item['name'],item['kind']=='parameter')
    (OUT/f'{key}.svg').write_text(svg,encoding='utf-8')
    manifest.append(dict(key=key,type=item['type'],guid=item['guid'],name=item['name'],family=family,glyph=symbol,badge=mark))

# A bridge arch, deck and FEM joints form the standalone plugin mark.
logo = rect(0.5,0.5,23,23,'#17364b',5.5) + path('M3.5 14.5H20.5M5 14.5V19M19 14.5V19','white',2) + path('M5 14Q12 1.5 19 14','#51d4c4',2.2) + path('M9 8.5V14M15 8.5V14','#51d4c4',1.2) + circle(5,14.5,1.6,'white')+circle(19,14.5,1.6,'white')
plugin=f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24"><title>BridGHe</title>{logo}</svg>\n'
(OUT/'BridGHe.svg').write_text(plugin,encoding='utf-8')
(OUT/'BridGHeChecks.svg').write_text(icon_svg('bridge','check','check','BridGHe Checks'),encoding='utf-8')
(OUT/'manifest.json').write_text(json.dumps({'grid':24,'palette':PALETTE,'icons':manifest},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Generated {len(manifest)} component/parameter SVGs plus plugin and Checks category.')
