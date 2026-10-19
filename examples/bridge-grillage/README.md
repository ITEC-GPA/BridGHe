> Aggiornamento fasi: il GHX corrente usa Bridge Action, Bridge Shrinkage Action e Bridge Phase, con colonne delle combinazioni basate sugli ID azione. Vedere [FASI-E-AZIONI](../../docs/FASI-E-AZIONI.md). Le cache native restano negli artefatti dei test; per ottenere i risultati nel GHX attivare le analisi. Il report phase-workflow-validation.json documenta il replay delle analisi storiche senza nuove esecuzioni Straus. La descrizione numerica storica sotto resta il riferimento del benchmark; i vecchi prefissi e ingressi di matching sono sostituiti dal nuovo workflow.

# Ponte misto a graticcio — esempio completo del flusso disponibile

Aprire **11-ponte-graticcio.ghx** in Grasshopper / Rhino 8 con il pacchetto BridGHe aggiornato. Il file costruisce gli elementi tramite componenti, esegue Straus7 R3, legge i risultati, unisce i contributi delle fasi e calcola le tensioni con BridgeChecks/gPC. Non è il progetto esecutivo di un ponte: la combinazione simmetrica è verificata numericamente; il traffico eccentrico richiede anche verifiche oggi non implementate dall'adattatore.

## Dati e modello fisico

| Voce | Valore del caso guida |
|---|---:|
| Luce semplicemente appoggiata | 30 m |
| Larghezza totale impalcato | 10 m |
| Travi principali | 4, interasse 2,50 m |
| Aggetto rispetto alle travi esterne | 1,25 m per lato |
| H saldata principale | h 1600, ali 600 × 40, anima 16 mm |
| Soletta | 250 mm; larghezza tributaria per trave 2500 mm |
| Diaframmi/traversi in acciaio | h 800, ali 300 × 20, anima 12 mm, ogni 5 m, inclusi gli appoggi |
| Acciaio | Es = 210000 MPa, ν = 0,30; materiale dimostrativo S355 |
| Calcestruzzo | C30; Ec = 32836,568 MPa restituito dalla libreria gPC; ν = 0,20 |
| Coefficiente viscosità efficace scelto | φ = 2 per G2, φ = 0 per traffico |
| Armature / scorrimento connessione | escluse dalle rigidezze / connessione perfetta assunta |
| Mesh base mista | 52 nodi, 48 beam longitudinali + 39 trasversali |
| Unità di lavoro | N, mm, MPa; deformazioni adimensionali |

Sono ipotesi dell'esempio, non dimensionamenti normativi. La larghezza tributaria di 2,50 m è assunta anche come larghezza efficace: non viene calcolato lo shear lag. Non viene dimensionata l'armatura della soletta.

Assi globali: X lungo la luce, Y trasversale, Z verso l'alto. Forze verticali verso il basso e Uz negativi. Le tensioni sono positive a trazione. I CSV mantengono tutte le componenti nelle convenzioni originali BridGHe/Straus; il passaggio a BridgeChecks usa `BridgeForceConvention`.

## Idealizzazione a graticcio e rigidezze

I modelli lineari sono graticci piani: DOF attivi Uz, Rx, Ry; Ux, Uy e Rz sono soppressi. Gli otto nodi sugli appoggi vincolano Uz, lasciando libere le rotazioni. Questa idealizzazione riguarda i carichi verticali, non frenatura, vento, sisma o dilatazioni longitudinali.

Tre modelli indipendenti conservano geometria e identificativi delle travi principali:

1. **Acciaio / G1:** la soletta fresca pesa ma non irrigidisce. I soli collegamenti trasversali sono i sette diaframmi in acciaio; 69 beam complessivi.
2. **Misto lungo termine / G2:** soletta collaborante con Ec,eff = Ec/(1+φ).
3. **Misto istantaneo / Q:** soletta collaborante con Ec.

Per le longitudinali, gPC calcola A ed I della sezione omogeneizzata all'acciaio. Per il controllo indipendente: `n = Es/Ec,eff`, `Aeq = As + Ac/n`, `yg = (As·ys + Ac·yc/n)/Aeq`, `Ieq = Is + As·(ys-yg)² + Ic/n + Ac·(yc-yg)²/n`. Origine della sezione all'interfaccia acciaio-calcestruzzo, ys = −h/2 e yc = t/2.

Per la torsione si assegna `GJlong = Gs·Js + Gc,eff·b·t³/6`. Le strisce trasversali hanno larghezza `s = Δx`, dimezzata agli estremi, e `EItrans = Ec,eff·s·t³/12`, `GJtrans = Gc,eff·s·t³/6`. Nelle sole stazioni dei diaframmi si sommano EI e GJ dell'acciaio. Il diaframma trasversale non viene assunto composto con la soletta: è un contributo elastico in parallelo.

Il fattore 1/2 sulla torsione della striscia rispetto al rettangolo isolato evita di assegnare due volte l'intera torsione della soletta nelle due direzioni. Riferimento tecnico verificato: [Autodesk Structural Bridge Design, Continuous Boundaries](https://help.autodesk.com/cloudhelp/ENU/ASBD-InProdEU/files/design_sections/tech_info_s/torsion_and_shear/ASBD_InProdEU_design_sections_tech_info_s_torsion_and_shear_Continuous_Boundaries_html.html). È una regola di modellazione del graticcio, non una verifica normativa.

Gli aggetti sono inclusi nel peso e nella sezione tributaria longitudinale, ma non hanno aste trasversali esterne dedicate: la flessione locale degli sbalzi non è rappresentata. Il modello non contiene shell sovrapposte al graticcio. Si trascura la deformabilità a taglio (Euler–Bernoulli), dichiaratamente anche nel benchmark analitico.

## Azioni e cinque contributi di fase

| Caso | Azione | Rigidezza |
|---|---|---|
| G1_GETTO | peso acciaio, soletta fresca e diaframmi | solo acciaio |
| G2_PERMANENTI | 2,50 kN/m² su tutta la larghezza | misto, φ = 2 |
| Q_UDL | 2,50 kN/m² simmetrici | misto, φ = 0 |
| Q_ECC / Q_MIRROR | veicolo di 300 kN, 2 assi da 150 kN, interasse 3 m, sulla metà destra/sinistra | misto, φ = 0 |
| RITIRO_INF | εc = −100 µε, contributo primario | sezione, φ = 2 |
| DEF_IST | ulteriore εc = −50 µε, contributo primario | sezione, φ = 0 |

I pesi specifici assunti sono 78,5 kN/m³ per acciaio e 25 kN/m³ per calcestruzzo. Il peso totale dei diaframmi è ripartito uniformemente tra le quattro travi lungo la luce: G1 = 22,02746 N/mm per trave. La densità FEM non genera altri pesi automatici. G2 e Q_UDL valgono 6,25 N/mm per trave. I carichi distribuiti sono concentrati ai nodi per aree tributarie, con metà peso ai nodi di estremità. Per Q_ECC/Q_MIRROR la distribuzione ai due nodi adiacenti conserva risultante e momento, ma è una discretizzazione del carico concentrato.

Il percorso mobile nativo applica quattro ruote da 75 kN, su Y = 1,25 e 3,75 m, asse della corsia Y = 2,50 m; la carreggiata del veicolo dimostrativo è 2,50 m. Il template e i valori sono **didattici, non LM1/NTC/Eurocodice**. Ventuno divisioni del percorso producono **23 posizioni non vuote**, includendo ingresso e uscita: nelle posizioni con un solo asse sul ponte la risultante è 150 kN. Non si applicano coefficienti dinamici o concomitanze normative.

La combinazione BridgeChecks `SLS_SIMMETRICO` somma con fattori 1 G1, G2, Q_UDL e i due incrementi di deformazione imposta. Il ritiro è applicato al solo calcestruzzo come autotensione primaria di sezione: non è un'eigenstrain del modello FEM e non comprende effetti secondari globali. Il secondo incremento è un dato distinto, non la ripetizione del primo ritiro. La viscosità adottata è una schematizzazione elastica per rapporti modulari, non un'integrazione completa della storia reologica.

È presente anche **un benchmark separato di cinque fasi native di getto**: livelli totali 20%, 40%, 60%, 80%, 100% di G1, senza indurimento della soletta. Nei modelli NLA i vincoli nel piano sono rilasciati: Ux è bloccato solo al primo appoggio, Uy e Uz sugli appoggi, rotazioni libere. Si evita così l'irrigidimento membranale artificiale che i vincoli del graticcio piano introdurrebbero in grandi spostamenti. Questi risultati sono totali e **non vanno sommati** come contributi incrementali nel merge BridgeChecks.

## Uso del file Grasshopper

1. Installare il nuovo pacchetto BridGHe e avviare Rhino 8 con licenza Straus API disponibile. Aprire `11-ponte-graticcio.ghx`; i Run delle analisi sono inizialmente false.
2. Esaminare i tre blocchi orizzontali: punti → Node → Restraints → Node Load; curve + proprietà → Beam → Super Element → Element Id → Build Model.
3. Impostare i percorsi `.st7` e avviare le tre `Straus R3 Analysis`. I percorsi iniziali puntano alla cartella temporanea del sistema, che esiste già. Nodo e beam risultati alimentano direttamente i pannelli di spostamenti, momenti e torsione.
4. Avviare `Moving Static Analysis` per il ramo del veicolo. Le posizioni mobili restano distinte; non si mescolano massimi di componenti provenienti da posizioni incompatibili.
5. Sulla destra, `Super Element Filter` seleziona G1…G4; i componenti `Composite Beam Strain` aggiungono i due incrementi di deformazione. I modelli di soli longitudinali hanno topologia identica e possono entrare in `Build BridGHe Model` nell'ordine acciaio, lungo termine, istantaneo.
6. Premere **Run** su `Build BridGHe Model`, poi su `Super Element Check`. Si elaborano 48 elementi × 5 stazioni = **240 sezioni**. `Composite Beam` assegna la sezione mista; `Composite Section Linear Tension Results` espone le tensioni.
7. Per modifiche coordinate a geometria, carichi e rigidezze usare `parameters.json` e rigenerare. Nel canvas i parametri geometrici e i dati sono modificabili, ma non sono collegati da slider universali: cambiare solo la soletta in un ramo non aggiorna automaticamente tutti gli altri. Il generatore è la fonte parametrica coerente.

La matrice delle combinazioni contiene solo la combinazione simmetrica. Q_ECC e Q_MIRROR rimangono nei risultati uniti, ma non sono dichiarati verificati. Aggiungerli a una combinazione del checker provoca correttamente un errore per la torsione non supportata. Le tre opzioni di instabilità locale sono false per il confronto elastico a sezione lorda: non è una verifica di classe o di sezione efficace.

## Risultati e controlli riproducibili

I numeri effettivamente eseguiti sono salvati in `validation.json`. Controlli: equilibrio Fz/Mx/My di ciascun caso, freccia simmetrica analitica, specularità del veicolo, reciprocità Maxwell–Betti, equilibrio in tutte le posizioni mobili, cinque fasi NLA, mesh 12/24/48, sensibilità di GJ ×0,5 e ×2, tensioni indipendenti in cinque fasi e round-trip/esecuzione del `.ghx`.

Il riferimento della freccia somma i contributi esatti dei carichi nodali di una trave appoggiata. Per un carico P alla distanza `a ≤ L/2` dal sostegno più vicino: `uz(L/2) = −P·a·(3L²−4a²)/(48EI)`. Non si confronta una discretizzazione nodale con la formula di q continuo come se fossero identiche. Le verifiche lineari usano tolleranze relative 10⁻⁷ e assolute 10⁻⁶ mm; le cinque fasi NLA ammettono 10⁻⁵ relativi per le rotazioni finite; il raffinamento deve cambiare la freccia del veicolo di meno del 2% tra due mesh consecutive.

Valori del caso guida: Uz di mezzeria per G1 **−32,4603 mm**, G2 **−5,8589 mm**, Q_UDL **−4,4616 mm**. La loro somma elastica è **−42,7808 mm**, esclusi ritiro e deformazione imposta. Sotto Q_ECC, G4 dà **−5,7919 / −5,8367 / −5,8419 mm** con 12/24/48 divisioni. Il massimo |T| sulle longitudinali sotto Q_ECC è **7,1647 kNm**.

Per la combinazione simmetrica, in mezzeria dopo cinque contributi: σ superiore/inferiore acciaio **−86,263 / +90,260 MPa**; σ superiore/inferiore calcestruzzo **−0,680 / +0,217 MPa**. La piccola trazione al lembo inferiore è conservata: il modello scelto è elastico non fessurato. Il confronto indipendente rende esplicita l'idealizzazione a pareti sottili usata dall'attuale motore gPC per le ali nelle fasi miste: `Is,misto = Is,pieno − 2·b·tf³/12`; la fase solo acciaio usa Is pieno.

**Da completare per un progetto reale:** torsione e interazione con taglio/flessione; instabilità locale e globale durante montaggio/getto; fessurazione e armature della soletta, sbalzi; connettori e scorrimento; fatica; appoggi; deformazioni imposte e reologia globali; azioni orizzontali, termiche, accidentali e combinazioni normative. `TauB` resta NaN perché manca un equivalente verificato nell'API gPC adottata.

## Riproduzione e file

La ricetta ammette 12 o 24 divisioni di base e verifica automaticamente anche i raffinamenti ×2 e ×4. I vincoli agli appoggi sono bilaterali: l'eventuale sollevamento non viene trattato con contatto unilaterale.

Da PowerShell nella radice del repository:

```powershell
.\tools\build.ps1 -BridgeGrillage -Package
```

Oppure `dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-grillage --gh` dopo aver compilato il plugin. Il test usa Rhino in processo nascosto e il solutore nativo; un errore o una misura fuori tolleranza restituisce codice diverso da zero.

Per aggiornare anche le tavole dopo una modifica dei dati: eseguire `python tools/render_bridge_example.py` in un ambiente con `reportlab`; lo script rifiuta report numerici con test falliti. Le tavole distribuite sono già generate e controllate visivamente.

- `11-ponte-graticcio.ghx`: definizione riapribile, dati incorporati, componenti originali.
- `parameters.json`: dati della fonte parametrica.
- `validation.json`: misure, valori attesi, tolleranze, esiti e sensibilità.
- `nodes.csv`, `forces.csv`: risultati completi, compresa la torsione; separatore virgola, decimale punto.
- `grasshopper-stresses.json`: tensioni lette attraverso i componenti BridgeChecks.
- `ponte-graticcio.pdf`: tavole sintetiche generate dai risultati.
- `artifacts/bridge-grillage/`: modelli `.st7`, `.lsa`/`.nla`, archivi `.bgmodel` e log delle varie analisi.
- `artifacts/BridGHe-ponte-graticcio-native.zip`: raccolta dei modelli e risultati nativi, prodotta da `-BridgeGrillage`.

Il generatore è `tests/BridGHe.Smoke/BridgeGrillageExample.cs`; la costruzione e prova del canvas è `GrasshopperBridgeExample.cs`. Non richiede nuovi GUID o nuove porte dei componenti esistenti.
