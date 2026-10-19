"""Measured bridge-suite results only. Run after --bridge-suite --gh passes."""
import csv
import json
import sys
from pathlib import Path
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib import colors
from reportlab.lib.pagesizes import landscape, A4
from reportlab.lib.utils import simpleSplit
from reportlab.graphics.shapes import Drawing, String
from reportlab.graphics.charts.lineplots import LinePlot
from reportlab.graphics import renderPDF

ROOT = Path(__file__).resolve().parents[1] / 'example'
spine = '--spine' in sys.argv
folders = sorted(p.parent for p in ROOT.glob('1[678]-*/validation.json' if spine else '1[2345]-*/validation.json'))
assert len(folders) == (3 if spine else 4)
reports = [json.loads((p/'validation.json').read_text(encoding='utf-8')) for p in folders]
assert all(r['Checks'] and all(c['Passed'] for c in r['Checks']) for r in reports)
assert all(any(c['Name'].startswith('GH ') for c in r['Checks']) for r in reports)
W, H = landscape(A4)
output = ROOT/'00-documentazione'/('ponti-spina-centrale.pdf' if spine else 'quattro-ponti.pdf')
c = Canvas(str(output), pagesize=(W,H))
c.setTitle('BridGHe | '+('Spina centrale a tre travi' if spine else 'Quattro ponti misti')+': tre fasi + ritiro')
NAVY=colors.HexColor('#173548'); GREY=colors.HexColor('#62727c'); TEAL=colors.HexColor('#148b8a')
PALETTE=[colors.HexColor(x) for x in ('#3079aa','#148b8a','#b85f24','#804997')]

def label(x,y,text,size=10,color=NAVY,bold=False):
    c.setFillColor(color); c.setFont('Helvetica-Bold' if bold else 'Helvetica',size); c.drawString(x,y,str(text))

def para(x,y,text,width=750,size=10):
    for line in simpleSplit(text,'Helvetica',size,width):
        label(x,y,line,size,GREY); y-=size*1.45
    return y

def header(page,title,sub):
    c.setFillColor(colors.HexColor('#f5f8fa'));c.rect(0,0,W,H,fill=1,stroke=0)
    label(40,H-35,'BridGHe / '+('SPINA CENTRALE A TRE TRAVI' if spine else 'QUATTRO PONTI')+' / 3 FASI + RITIRO',10,TEAL,True)
    label(40,H-67,title,24,NAVY,True);label(40,H-90,sub,10,GREY)
    c.setStrokeColor(colors.HexColor('#ced9df'));c.line(40,37,W-40,37)
    label(40,21,'Rhino 8 + Grasshopper + Straus7 R3 + gPC | Risultati di calcolo, non verifica normativa completa',8,GREY)
    label(W-65,21,f'{page} / {2*len(reports)}',8,GREY)

def table(x,top,widths,head,rows):
    h=25;c.setFillColor(NAVY);c.rect(x,top-h,sum(widths),h,fill=1,stroke=0)
    xx=x
    for w,t in zip(widths,head):label(xx+7,top-17,t,9,colors.white,True);xx+=w
    for i,row in enumerate(rows):
        y=top-(i+2)*h;c.setFillColor(colors.white if i%2==0 else colors.HexColor('#e9f0f4'));c.rect(x,y,sum(widths),h,fill=1,stroke=0)
        xx=x
        for w,t in zip(widths,row):label(xx+7,y+8,t,9);xx+=w

def chart(x,y,width,height,series,title,ylabel,length):
    drawing=Drawing(width,height); lp=LinePlot();lp.x=50;lp.y=35;lp.width=width-65;lp.height=height-70
    lp.data=series;lp.joinedLines=1
    for i in range(len(series)):lp.lines[i].strokeColor=PALETTE[i%4];lp.lines[i].strokeWidth=1.3
    lp.xValueAxis.valueMin=0;lp.xValueAxis.valueMax=length/1000;lp.xValueAxis.valueStep=length/5000
    lp.xValueAxis.labelTextFormat='%.0f';lp.yValueAxis.labelTextFormat='%.2f'
    lp.xValueAxis.labels.fontSize=8;lp.yValueAxis.labels.fontSize=8
    drawing.add(lp);drawing.add(String(50,height-12,title,fontName='Helvetica-Bold',fontSize=10,fillColor=NAVY))
    drawing.add(String(0,height-34,ylabel,fontSize=8));drawing.add(String(width-65,10,'s [m]',fontSize=8))
    renderPDF.draw(drawing,c,x,y)

for idx,(folder,r) in enumerate(zip(folders,reports)):
    d=r['Definition'];geom=json.loads((folder/'geometry.json').read_text(encoding='utf-8'))
    nodes=list(csv.DictReader((folder/'nodes.csv').open(encoding='utf-8')))
    subtitle = 'Spina H2000 + due laterali H1600 | Soletta 250 mm | Impalcato 12 m' if spine else 'Quattro H1600 + soletta 250 mm | Impalcato 10 m'
    header(2*idx+1,d['Title'],f"Esempio {d['Id'][:2]} | {subtitle} | {len(r['Checks'])} controlli superati")
    xmin=min(n['X'] for n in geom['Nodes']);xmax=max(n['X'] for n in geom['Nodes']);ymin=min(n['Y'] for n in geom['Nodes']);ymax=max(n['Y'] for n in geom['Nodes'])
    scale=min(740/(xmax-xmin),165/(ymax-ymin));ox=40+(750-(xmax-xmin)*scale)/2;oy=307+(165-(ymax-ymin)*scale)/2
    def point(x,y):return ox+(x-xmin)*scale,oy+(y-ymin)*scale
    for b in geom['Beams']:
        c.setStrokeColor(NAVY if b['Main'] else colors.HexColor('#b3c9cf'));c.setLineWidth(1.4 if b['Main'] else .5);c.line(*point(*b['From']),*point(*b['To']))
    for n in geom['Nodes']:
        if n['Support']:
            x,y=point(n['X'],n['Y']);c.setFillColor(PALETTE[2]);c.circle(x,y,2.2,fill=1,stroke=0)
    label(40,286,'Pianta in scala omogenea. Punti arancio: appoggi verticali; guide in pianta definite nel README.',9,GREY)
    rows=[]
    for f in r['Results']:
        rows.append([f['Case'],f"{f['MinUz']:.3f}",f"{f['MaxUz']:.3f}",f"{f['MinMy']/1e6:.1f} / {f['MaxMy']/1e6:.1f}",f"{f['MaxAbsT']/1e6:.2f}"])
    table(40,265,[165,100,100,230,160],['Contributo','Uz min [mm]','Uz max [mm]','My min / max [kNm]','max |T| [kNm]'],rows)
    para(40,115,'G1: acciaio/getto. G2: misto Ec/3. Q: misto istantaneo, carico uniforme dimostrativo 2,5 kN/m2. Ritiro: '+('-200' if spine else '-100')+' microstrain longitudinale; deformazione imposta nel FEM e autotensione di sezione aggiunta una sola volta.',750,10)
    c.showPage()
    header(2*idx+2,'Deformate, ritiro e dettagli','Risultati incrementali e campo delle verifiche disponibili')
    series=[]
    for phase in range(1,5):
        series.append(sorted((float(n['s_mm'])/1000,float(n['uz_mm'])) for n in nodes if int(n['phase'])==phase and int(n['girder'])==(2 if spine else 1)))
    chart(30,245,470,245,series,('G2 (spina)' if spine else 'G1')+': deformata dei quattro contributi','Uz [mm]',d['Length'])
    for i,name in enumerate(('G1_GETTO','G2_PERMANENTI','Q_TRAFFICO','RITIRO')):label(520,467-i*21,name,10,PALETTE[i],True)
    sh=r['Shrinkage'];label(520,365,'Ritiro equivalente',12,TEAL,True)
    label(520,344,f"epsilon0 = {sh['Epsilon']*1e6:.3f} microstrain",10)
    label(520,324,f"k = {sh['Gradient']:.6g} 1/mm",10)
    label(520,304,f"max errore mesh = {100*max(m['RelativeError'] for m in r['Mesh']):.3f}%",10)
    label(520,284,f"mesh: {d['PanelsPerSpan']} -> {2*d['PanelsPerSpan']} / campata",10)
    if d['Radius']==0:
        details=json.loads((folder/'details.json').read_text(encoding='utf-8'))
        text='BridGHe: tensioni finali confrontate con integrali indipendenti; sezione lorda non fessurata. Pioli e irrigiditori calcolati da gPC nelle sezioni di appoggio e del primo diaframma. I controlli resistenti che richiedono SLU restano senza esito sul caso SLS.'
        ss=r['Sections'][-1]['FinalStress'];label(40,225,'Sezione campione - tensioni finali [MPa]',11,TEAL,True)
        table(40,210,[188,188,188,191],['CLS sopra','CLS sotto','Acciaio sopra','Acciaio sotto'],[[f'{v:.3f}' for v in ss]])
    else:
        text='La torsione delle travi ad H e conservata nel modello e nei CSV. BridGHe rifiuta le azioni fuori dal proprio campo: nessun esito di verifica H completa. Le definizioni dei pioli e degli irrigiditori sono presenti nel GHX; il ramo verifiche si arresta a monte. Il modello torsionale gPC per cassoni non e applicabile alla H.'
        label(40,225,'FEM calcolato; verifica delle H curve da completare',12,PALETTE[2],True)
    para(40,143,text,750,10)
    para(40,83,'Limiti: niente fessurazione, ritiro trasversale, storia reologica, scorrimento dei pioli o inviluppo del traffico. Parametri, controlli con tolleranze, CSV e istruzioni nelle cartelle degli esempi e nel README.',750,9)
    c.showPage()
c.save()
print(output)
