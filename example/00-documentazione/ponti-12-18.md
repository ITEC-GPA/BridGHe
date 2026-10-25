## Workflow aggiornato

I GHX 12-18 usano ora Bridge Action, Bridge Shrinkage Action e Bridge Phase. Gli ID G1/G2/Q/RITIRO sostituiscono i prefissi derivati dall'ordine dei modelli. La sola colonna RITIRO governa primario e secondario, distinti nel calcolo. Vedere [la guida](../../docs/FASI-E-AZIONI.md).

Il nuovo assemblaggio è stato verificato usando i risultati nativi storici (report phase-workflow-validation.json in ciascuna cartella); non sono state ripetute le analisi Straus. Le cache restano nelle sottocartelle `calcoli/` per contenere il peso dei GHX: per ottenere risultati nel documento attivare CALCOLA 4 MODELLI e poi i due Run. Le descrizioni dei vecchi ingressi di matching e i PDF storici sotto si riferiscono alla precedente interfaccia; dati e ipotesi numeriche restano invariati.

# Quattro ponti misti: tre fasi + ritiro

## Esempi aggiuntivi 16-18: spina centrale e due travi laterali

Sono disponibili tre nuovi modelli a **tre travi**, con quattro analisi incrementali ciascuno e **212 controlli superati** sul complesso dei tre esempi, inclusa la riapertura e soluzione dei file Grasshopper:

| Esempio | Geometria | Elementi per campata / trave |
|---|---|---|
| [16: spina, campata unica](../16-spina-1campata/16-spina-1campata.ghx) | 30 m rettilinei | 24 |
| [17: spina, tre campate](../17-spina-3campate/17-spina-3campate.ghx) | 20 + 30 + 20 m rettilinei | 48 |
| [18: spina curva](../18-spina-curva-2campate/18-spina-curva-2campate.ghx) | 25 + 25 m, R=120 m | 96 |

Impalcato largo 12 m, assi delle travi a -4, 0 e +4 m. La spina centrale è una H2000 con flange 800x50 mm e anima 20 mm; le laterali sono H1600 con flange 600x40 mm e anima 16 mm. Soletta di 250 mm, larghezza efficace assunta pari alla tributaria di 4000 mm per ogni trave, senza verifica dello shear lag. Materiali, vincoli, diaframmi e pressioni seguono le ipotesi sotto riportate, con ritiro **-200 microstrain**. Il peso proprio distingue le tre sezioni. Il ritiro equivalente è calcolato separatamente per la spina e per le laterali.

La mesh è confrontata con una discretizzazione doppia in tutte e quattro le analisi, con soglia del 2% sul massimo scarto di Uz riferito al picco della soluzione raffinata. Nei rettilinei sono controllate anche la simmetria delle deformate laterali e le tensioni della spina con formule indipendenti. Il ramo di verifica GH seleziona **G2**, la spina centrale; le azioni delle altre due travi restano nei risultati FEM. Per la spina H curva vengono conservate tutte le azioni FEM; il checker usa soltanto N–Mx–Vy, dichiarando l’esclusione di T, M2 e V1. Questo non costituisce una verifica completa delle tre travi o dell'impalcato.

Per rigenerare, modificare i parametri geometrici e delle sezioni nei rispettivi `parameters.json`, poi eseguire dalla root:

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-spine all --gh
```

I CSV, le verifiche con tolleranze, i dettagli di pioli/irrigiditori e i file GHX si trovano nelle singole cartelle. I modelli Straus e i risultati sono generati in `example/<caso>/calcoli/`.

## Esempi 12-15: quattro travi longitudinali

Quattro definizioni Grasshopper modificabili, costruite con componenti BridGHe e risolte con Straus7 R3. I quattro rami rappresentano **incrementi elastici con rigidezze differenti**: non sono quattro copie di risultati totali di un'analisi staged. I benchmark delle fasi native restano negli esempi 06–10 e 11.

| Esempio | Campate, lungo asse [m] | Raggio asse [m] | Elementi per campata / trave |
|---|---|---|---|
| [12: continuo](../12-continuo-2campate/12-continuo-2campate.ghx) | 20 + 20 | Rettilineo | 12 |
| [13: continuo](../13-continuo-3campate/13-continuo-3campate.ghx) | 20 + 30 + 20 | Rettilineo | 12 |
| [14: curvo](../14-curvo-1campata/14-curvo-1campata.ghx) | 30 | 80 | 48 |
| [15: curvo continuo](../15-curvo-2campate/15-curvo-2campate.ghx) | 25 + 25 | 120 | 24 |

## Uso

1. Caricare la versione aggiornata di BridGHe in Rhino 8/.NET 8. Aprire il GHX corrispondente.
2. Controllare i quattro percorsi `.st7`, nella sottocartella `calcoli/` del rispettivo esempio. Attivare **CALCOLA 4 MODELLI**.
3. Esaminare spostamenti, azioni e torsione nei quattro rami. Eseguire **Run** nel menu di `Build BridGHe Model`, poi di `Super Element Check`.
4. Nei rettilinei il ramo gPC produce tensioni elastiche e alimenta i nuovi componenti dei dettagli. Nei curvi il checker H produce la sola risposta N–Mx–Vy: T, M2 e V1 sono trascurati e l’ipotesi accompagna risultati e report. Le sei azioni FEM restano disponibili.
5. Le proprietà e geometrie nei parametri GH sono editabili. Per cambiare in modo coerente campate, raggio e mesh, modificare `parameters.json` e rigenerare dalla radice del repository:

```powershell
.\tools\build.ps1 -BridgeSuite -Package
```

Questa procedura sovrascrive i GHX generati: salvare con un altro nome eventuali modifiche manuali. Sorgenti: `tests/BridGHe.Smoke/BridgeSuiteExample.cs` e `GrasshopperBridgeSuite.cs`. Le dimensioni di sezione e le azioni comuni sono definite in `SectionParameters`.

## Schema e ipotesi

Unità **N, mm, MPa**. Impalcato largo 10 m; quattro travi longitudinali distanti 2,5 m, a ±1,25 e ±3,75 m dall'asse. Trave H alta 1600 mm, flange 600×40 mm, anima 16×1520 mm; soletta 250 mm. Larghezza tributaria 2500 mm per trave, senza riduzione per shear lag. Materiali dimostrativi: acciaio E=210000 MPa, ν=0,30, fy=355 MPa; C30, E restituito dal materiale gPC, ν=0,20. Le resistenze e i dettagli non sostituiscono la scelta progettuale dei materiali per spessore e norma.

Diaframmi H800, flange 300×20 mm, anima 12 mm, a sei intervalli per campata; in fase mista si aggiungono strisce trasversali di soletta. La mesh può essere raffinata senza aggiungere diaframmi. Il graticcio usa Euler–Bernoulli, A/I11/I22/J equivalenti, connessione completa e sezioni lorde non fessurate. Nelle fasi miste I11 segue la convenzione gPC delle flange sottili (si omette `2 b tf³/12` rispetto alla somma esatta dei rettangoli); la fase di solo acciaio usa l'inerzia completa.

Tutte le travi hanno Uz bloccato a spalle e pile. Ux e Uy sono bloccati sul primo appoggio di G1; Uy sul suo ultimo appoggio. Tutte le altre traslazioni in pianta e le rotazioni sono libere. Questi tre vincoli in pianta eliminano i moti rigidi senza impedire artificialmente il ritiro longitudinale. Nei curvi sono vincoli cartesiani, non guide radiali.

La curva è approssimata con corde: `x=(R-y) sin(s/R)`, `Y=R-(R-y) cos(s/R)`. Carichi nodali tributari riferiti alle lunghezze delle corde, con mezzi contributi alle estremità. Gli sbalzi esterni della soletta entrano nelle larghezze tributarie; la loro flessione locale non è modellata.

## I quattro contributi

| Caso | Rigidezza | Azione |
|---|---|---|
| G1_GETTO | Solo acciaio e diaframmi | Peso travi, soletta fresca e diaframmi; γ acciaio 78,5 e cls 25 kN/m³ |
| G2_PERMANENTI | Mista, Ec/(1+φ), φ=2 | Permanenti portati 2,5 kN/m² |
| Q_TRAFFICO | Mista istantanea | Pressione dimostrativa 2,5 kN/m² sull'intero impalcato |
| RITIRO | Mista lungo termine | εcs = −100 µε, ritiro differenziale longitudinale della soletta |

La combinazione dimostrativa somma i quattro incrementi con coefficienti unitari. Il traffico uniforme non è un modello normativo LM1 né un inviluppo delle disposizioni sulle campate. I veicoli mobili e le disposizioni eccentriche sono trattati nell'esempio 11.

## Ritiro primario e secondario

Con z verticale, z=0 all'interfaccia acciaio–soletta, baricentro elastico zG e Ec,eff=Ec/3:

```
EA = Es As + Ec,eff Ac
N* = Ec,eff Ac εcs                         [N]
M* = N* (zc − zG)                        [N mm]
ε0 = N*/EA                              [adimensionale]
k  = M*/EI                              [1/mm]
```

Il modello nativo impone **pre-strain = −ε0** e **gradiente 2 = k/α**, con α convenzionale 10⁻⁵/K. È un dispositivo per imporre deformazioni, non un'azione termica aggiuntiva. Le risultanti native del caso RITIRO sono gli effetti secondari dei vincoli. Le autotensioni primarie sono aggiunte una sola volta in gPC mediante `Composite Beam Slab Strain`:

```
σs = Es [ε0 + k(z−zG)]
σc = Ec,eff [ε0 + k(z−zG) − εcs]
```

L'adattatore storico BridgeChecks dispone di tre contenitori meccanici: il secondario RITIRO entra nel contenitore Ninf insieme a G2. L'autotensione entra nel quarto contributo separato. È equivalente per la somma elastica finale; i quattro CSV FEM mantengono invece separati G1/G2/Q/RITIRO. Non interpretare il secondo stato interno di BridgeChecks come G2 puro.

Il ritiro trasversale delle strisce, la viscosità evolutiva con età dei getti, il rilassamento nel tempo di G1, gli scorrimenti dei pioli e gli offset fra piani medi non sono inclusi. Il graticcio è un modello globale equivalente, non un modello a due strati con connessione deformabile.

## Pioli e irrigiditori

Nuovi componenti nella sottocategoria **Bridge details**:

- `Bridge Studs`: tutti i parametri di `BridgeStudOptions` (diametro, altezza, passi, fila, testa, copriferro, fu, γV e dettagli per azioni ripetute).
- `Bridge Intermediate Stiffener`: geometria anche asimmetrica, pannelli adiacenti, fattore di lunghezza, compressione esterna ed eccentricità, saldature.
- `Bridge Support Stiffener`: piatti, reazione, impronta, posizione, eccentricità, montante terminale e saldature.
- `Bridge Detail Check`: ricalcola l'input del risultato BridgeChecks con `HBridgeSection.Calculate`; espone domanda, resistenza, unità, rapporto, stato e avvisi originali gPC.

I dettagli sono dati di verifica della sezione; non aggiungono massa, rigidezza o elementi piolo al FEM. Le definizioni sono salvate nel GHX. Negli esempi: pioli 2×Ø22, h=180 mm, passo longitudinale 200 mm e trasversale 150 mm; irrigiditori intermedi bilaterali 200×20 mm e d'appoggio 250×25 mm. Sono proposte dimostrative, non dettagli certificati.

**Attenzione alle unità dei componenti gPC: forze degli irrigiditori in kN**, geometria mm e tensioni MPa. La reazione del primo appoggio è la somma SLS dei quattro risultati nativi divisa per 1000. I controlli che richiedono SLU rimangono `Da verificare`/NaN: il componente conserva lo stato limite e non applica coefficienti nascosti. Nei curvi il ramo dettagli usa ora la risposta N–Mx–Vy con esclusione dichiarata di T, M2 e V1. gPC contiene un modello torsionale per cassoni: non è applicabile alla H aperta.

## Validazione e risultati

Ogni cartella contiene GHX, parametri, geometria, `nodes.csv`, `forces.csv` con tutte le sei azioni, `validation.json` con tolleranze ed esiti. Nei rettilinei sono presenti anche le tensioni GH e `details.json`. I file nativi sono generati in `example/<caso>/calcoli/`, archiviati da `-BridgeSuite` in `example/00-documentazione/archivio-pacchetti/BridGHe-quattro-ponti-native.zip`.

Controlli: equilibrio globale delle sei componenti; trave libera e impedita con ritiro; equazioni dei tre momenti e deformate analitiche dei continui; raffinamento 12→24, 24→48 o 48→96 per campata in tutti e quattro i contributi (scarto Uz ≤2% del picco); tensioni finali con integrali indipendenti; GHX salvati/riaperti, risolti e confrontati per spostamenti XYZ e tutte le azioni N/V1/V2/BM1/BM2/T. La resistenza dei pioli è confrontata con un calcolo indipendente. I report storici registrano il precedente rifiuto della torsione. La regressione attuale confronta la proiezione N–Mx–Vy con il calcolo elastico indipendente, conserva tutte le azioni FEM e verifica la dichiarazione dell’ipotesi nei risultati; **non verifica gli effetti fuori piano**.

Restano da sviluppare per un progetto: soletta fessurata nelle zone di momento negativo, armatura e minima, shear lag, instabilità globale, torsione delle H e distorsione, inviluppi SLU/SLE del traffico, ritiro trasversale e storia reologica, fatica della connessione, soletta locale, appoggi e azioni orizzontali. In particolare la soletta non fessurata dei continui non costituisce un modello finale di progetto.

Fonti tecniche del solutore: [pre-load](https://www.strand7.com/strand7r3help/Content/Topics/VISUALAttributes/BeamAttributesPreLoad.htm), [gradiente termico](https://www.strand7.com/strand7r3help/Content/Topics/VISUALAttributes/BeamAttributesTemperatureGradient.htm), [assi principali](https://www.strand7.com/strand7r3help/Content/Topics/Elements/ElementsBeamsPrincipalAxes.htm). Le verifiche di dettaglio provengono dai sorgenti locali `Checker/GPCChecker.CompositeBridge/HSections/HBridgeSection.Details.cs`, `HBridgeSection.Shear.cs` e `Checks/BridgeShearConnection.cs`; non è stata effettuata una nuova validazione normativa integrale di tali librerie.

Il flusso dei pioli include il contributo V dei secondari del ritiro. La connessione di estremità dovuta al trasferimento delle autotensioni e altri flussi aggiuntivi non sono ricavati automaticamente dal graticcio: gli avvisi gPC restano nel report. Il passo proposto non equivale a una verifica della fatica o dell'intera connessione.
