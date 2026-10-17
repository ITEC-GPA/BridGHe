"""Render the bridge example's measured results; requires reportlab.
Run only after --bridge-grillage --gh succeeds. No solver results are invented.
"""
import csv
import json
from pathlib import Path
from reportlab.pdfgen.canvas import Canvas
from reportlab.lib import colors
from reportlab.lib.pagesizes import landscape, A4
from reportlab.lib.utils import simpleSplit
from reportlab.graphics.shapes import Drawing, String
from reportlab.graphics.charts.lineplots import LinePlot
from reportlab.graphics import renderPDF

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "examples/bridge-grillage"
r = json.loads((OUT / "validation.json").read_text(encoding="utf-8"))
if not r["Checks"] or not all(x["Passed"] for x in r["Checks"]):
    raise SystemExit("Refusing to publish a report with failed or missing checks")
p = r["Parameters"]
nodes = list(csv.DictReader((OUT / "nodes.csv").open(encoding="utf-8")))
forces = list(csv.DictReader((OUT / "forces.csv").open(encoding="utf-8")))
W, H = landscape(A4)
c = Canvas(str(OUT / "ponte-graticcio.pdf"), pagesize=(W, H))
c.setTitle("BridGHe | Ponte misto a graticcio")
c.setAuthor("BridGHe")
NAVY = colors.HexColor("#173548")
BLUE = colors.HexColor("#3079aa")
TEAL = colors.HexColor("#148b8a")
AMBER = colors.HexColor("#b85f24")
GREY = colors.HexColor("#62727c")
PALETTE = [BLUE, TEAL, AMBER, colors.HexColor("#804997")]

def label(x, y, text, size=10, color=NAVY, bold=False):
    c.setFillColor(color)
    c.setFont("Helvetica-Bold" if bold else "Helvetica", size)
    c.drawString(x, y, text)

def para(x, y, text, width=740, size=10, color=GREY):
    for line in simpleSplit(text, "Helvetica", size, width):
        label(x, y, line, size, color)
        y -= size * 1.45
    return y

def header(k, title, sub):
    c.setFillColor(colors.HexColor("#f5f8fa")); c.rect(0, 0, W, H, fill=1, stroke=0)
    label(40, H-40, "BridGHe / ESEMPIO 11", 10, TEAL, True)
    label(40, H-72, title, 25, NAVY, True)
    label(40, H-94, sub, 10, GREY)
    c.setStrokeColor(colors.HexColor("#ced9df")); c.line(40, 37, W-40, 37)
    label(40, 21, "Rhino 8 + Grasshopper + Straus7 R3 + gPC | N - mm - MPa | " + r["Date"][:10], 8, GREY)
    label(W-65, 21, str(k)+" / 3", 8, GREY)

def poly(points, fill, stroke=NAVY):
    path=c.beginPath();path.moveTo(*points[0])
    for point in points[1:]: path.lineTo(*point)
    path.close();c.setFillColor(fill);c.setStrokeColor(stroke);c.drawPath(path,stroke=1,fill=1)

def table(x, top, widths, headings, rows, row_height=28):
    c.setFillColor(NAVY);c.rect(x,top-row_height,sum(widths),row_height,fill=1,stroke=0)
    xx=x
    for width, heading in zip(widths,headings):label(xx+9,top-18,heading,9,colors.white,True);xx+=width
    for i,row in enumerate(rows):
        yy=top-(i+2)*row_height
        c.setFillColor(colors.white if i%2==0 else colors.HexColor("#eaf0f3"));c.rect(x,yy,sum(widths),row_height,fill=1,stroke=0)
        xx=x
        for width,value in zip(widths,row):label(xx+9,yy+9,str(value),10);xx+=width

def chart(x,y,width,height,series,title,ylabel):
    d=Drawing(width,height)
    lp=LinePlot();lp.x=52;lp.y=35;lp.width=width-70;lp.height=height-70
    lp.data=series;lp.joinedLines=1
    for i in range(len(series)):lp.lines[i].strokeColor=PALETTE[i%4];lp.lines[i].strokeWidth=1.5
    lp.xValueAxis.valueMin=0;lp.xValueAxis.valueMax=p["Span"]/1000;lp.xValueAxis.valueStep=p["Span"]/6000
    lp.xValueAxis.labelTextFormat="%.0f";lp.yValueAxis.labelTextFormat="%.1f"
    lp.xValueAxis.labels.fontSize=8;lp.yValueAxis.labels.fontSize=8
    lp.xValueAxis.strokeColor=GREY;lp.yValueAxis.strokeColor=GREY
    d.add(lp);d.add(String(52,height-14,title,fontName="Helvetica-Bold",fontSize=11,fillColor=NAVY))
    d.add(String(width-95,10,"X [m]",fontSize=9));d.add(String(2,height-35,ylabel,fontSize=9))
    renderPDF.draw(d,c,x,y)

header(1,"Travata mista a graticcio","Quattro travi principali, diaframmi ogni 5 m e soletta collaborante. Modello didattico di 30 x 10 m.")
L=p["Span"]/1000;B=p["Width"]/1000;t=p["SlabThickness"]/1000;h=p["SteelHeight"]/1000
def iso(x,y,z):return 110+17*x+8*y,320+3.8*x+8*y+23*z
for j in range(4):
    yy=(j-1.5)*B/4
    poly([iso(0,yy,0),iso(L,yy,0),iso(L,yy,-h),iso(0,yy,-h)],colors.HexColor("#4e7895"))
for i in range(7):
    x=L*i/6
    poly([iso(x,-3*B/8,-.2),iso(x,3*B/8,-.2),iso(x,3*B/8,-.8),iso(x,-3*B/8,-.8)],colors.HexColor("#9bb4c3"))
c.saveState();c.setFillAlpha(.45)
poly([iso(0,-B/2,t),iso(L,-B/2,t),iso(L,B/2,t),iso(0,B/2,t)],colors.HexColor("#a9c8c5"),TEAL)
c.restoreState()
label(680,430,"Soletta 250 mm",12,TEAL,True)
para(680,410,"Il peso del getto agisce prima dell'attivazione della rigidezza composta.",120,10)
label(40,233,"Sezione trasversale",13,NAVY,True)
sx=45;sy=178;scale=57
c.setFillColor(colors.HexColor("#b7cecc"));c.rect(sx,sy,B*scale,t*scale,fill=1,stroke=0)
for j in range(4):
    xx=sx+(j+.5)*B/4*scale
    fw=p["FlangeWidth"]/1000*scale;ft=p["FlangeThickness"]/1000*scale;tw=p["WebThickness"]/1000*scale
    c.setFillColor(BLUE)
    c.rect(xx-fw/2,sy-ft,fw,ft,fill=1,stroke=0);c.rect(xx-tw/2,sy-h*scale,tw,h*scale,fill=1,stroke=0);c.rect(xx-fw/2,sy-h*scale,fw,ft,fill=1,stroke=0)
    label(xx-7,sy+t*scale+12,"G"+str(j+1),9,BLUE,True)
label(660,209,"H 1600 / 600 x 40 / 16",11,NAVY,True)
para(660,187,"Interasse 2,50 m; aggetti 1,25 m. La flessione locale degli aggetti non e modellata.",140,10)
label(45,67,"3 rigidezze: acciaio / misto a lungo termine / misto istantaneo",11,TEAL,True)
c.showPage()

header(2,"Risposta globale e controlli","Risultati effettivi Straus7. Le curve del veicolo eccentrico mostrano la ripartizione tra le quattro travi.")
series=[]
for j in range(4):
    yy=(j-1.5)*p["Width"]/4
    series.append(sorted((float(a["x_mm"])/1000,float(a["uz_mm"])) for a in nodes if a["model"]=="2" and a["case"]=="Q_ECC" and float(a["y_mm"])==yy))
chart(30,245,460,230,series,"Q_ECC: freccia [mm]","Uz [mm]")
for j in range(4):label(100+j*80,247,"G"+str(j+1),10,PALETTE[j],True)
table(510,465,[170,100],["Caso simmetrico","Uz mezzeria"],[("G1 acciaio",f'{r["MidspanSteel"]:.4f} mm'),("G2 lungo termine",f'{r["MidspanLong"]:.4f} mm'),("Q_UDL istantaneo",f'{r["MidspanLive"]:.4f} mm')])
para(510,325,"Le tre frecce sono contributi indipendenti. Somma meccanica: %.4f mm; non include ritiro." % sum(r[k] for k in ["MidspanSteel","MidspanLong","MidspanLive"]),265)
table(45,215,[140,110,130],["Mesh lungo X","Uz G4 [mm]","Tipo di carico"],[(str(a["Panels"])+" divisioni",f'{a["Uz"]:.6f}',"Q_ECC") for a in r["Mesh"]])
para(475,210,"Veicolo didattico: 2 assi da 150 kN, interasse 3 m. 21 divisioni del percorso e 23 posizioni caricate, inclusi ingresso e uscita.",310,11)
para(475,142,"Massimo |T| sulle travi principali: %.4f kNm. Torsione conservata, NON verificata da MeCheck2." % (r["Stresses"]["MaxTrafficTorqueNmm"]/1e6),310,11,AMBER)
label(45,64,"Equilibrio, simmetria, reciprocita, raffinamento e sensibilita GJ verificati nel report JSON.",10,TEAL,True)
c.showPage()

header(3,"Cinque contributi, fino alle tensioni","SLS_SIMMETRICO: fattori 1.0, sezione lorda non fessurata, armature escluse. Valori di mezzeria in MPa.")
names=["1. G1 su acciaio","2. + G2, phi = 2","3. + Q_UDL, phi = 0","4. + ritiro -100 microstrain","5. + incremento -50 microstrain"]
rows=[]
for name,s in zip(names,r["Stresses"]["Rows"]):rows.append([name]+[f'{s[k]:+.3f}' for k in ["TopConcrete","BottomConcrete","TopSteel","BottomSteel"]])
table(40,468,[250,120,120,120,120],["Fase accumulata","Cls superiore","Cls inferiore","Acc. superiore","Acc. inferiore"],rows,32)
label(40,240,str(len(r["Checks"]))+" controlli superati",19,TEAL,True)
para(40,215,"Il file GHX viene scritto, riaperto e risolto. Gli spostamenti sono confrontati con i modelli nativi e le tensioni con l'integrazione elastica indipendente della sezione. 240 sezioni attraversano i componenti MeCheck2.",740,11)
para(40,153,"Cinque fasi native di getto (20-40-60-80-100% di G1) sono testate separatamente. I risultati totali di queste fasi NON sono sommati nel merge dei contributi elastici.",740,11)
para(40,104,"Non costituisce verifica completa del ponte: restano torsione, instabilita, connettori, soletta locale e armature, fatica, appoggi, reologia globale e combinazioni normative. TauB non disponibile. Dati, ipotesi, formule e limiti sono descritti nel README dell'esempio.",740,10,AMBER)
c.save()
print(OUT / "ponte-graticcio.pdf")
