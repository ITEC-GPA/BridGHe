# 50 · Ponte parametrico: quattro modelli di calcolo

Aprire **[50-ponte-quattro-modelli.ghx](50-ponte-quattro-modelli.ghx)** con la Release aggiornata di BridGHe per Rhino 8. Questo esempio deriva da una copia del **49**, che resta invariato. Conserva tutti gli input geometrici a sinistra, le sezioni per concio e le uscite con nome completo.

La configurazione iniziale ha tre campate da 30 m, tre travi, 24 conci, diaframmi pieni, controventi, fasce trasversali di soletta alla quota reale, link rigidi e appoggi. Ogni modello contiene la stessa geometria di 324 beam e 266 nodi. Le sezioni e i valori di carico sono dimostrativi, non dimensionati né combinati secondo una norma.

## I quattro modelli

| Modello | Proprietà delle travi principali | Altra carpenteria | Fasce trasversali della soletta |
|---|---|---|---|
| **M01_ACCIAIO** | Sezione a I in solo acciaio, distinta per concio | Acciaio | Cls fittizio senza peso |
| **M02_NINF** | Sezione mista omogeneizzata con n∞ = 18 | Acciaio invariato | Stesso cls senza peso |
| **M03_N0** | Sezione mista omogeneizzata con n0 = 7 | Acciaio invariato | Stesso cls senza peso |
| **M04_NSH** | Sezione mista omogeneizzata con n da ritiro = 12 | Acciaio invariato | Stesso cls senza peso |

**n∞ indica il comportamento a lungo termine, con coefficiente finito**; non è un infinito numerico. I tre coefficienti sono slider indipendenti. Le sezioni miste usano le sezioni di ogni concio, il calcestruzzo e le due file di armatura B450C del 49. La larghezza efficace iniziale è 3000 mm per trave; è un input dimostrativo, distinto dalla larghezza geometrica dell'impalcato e da verificare per il ponte effettivo.

Le proprietà omogeneizzate sono sezioni generiche Straus riferite all'acciaio, calcolate dal componente esistente **Composite Beam Property**. La torsione conserva la convenzione già implementata dal componente. **Bridge Segment Override** abbina le proprietà tramite `G1-C1`, `G1-C2`, ecc.: tutte le aste FEM appartenenti allo stesso concio ricevono la sua proprietà. Il numero delle suddivisioni FEM non determina l'abbinamento.

Il materiale fittizio delle fasce ha **E = 30000 MPa**, **ν = 0,20**, **G = E/[2(1+ν)]** e **densità zero**. È comune ai quattro modelli per isolare il confronto delle proprietà longitudinali. L'eventuale variazione della rigidezza trasversale con viscosità, fessurazione o fase costruttiva non è applicata automaticamente.

## Carichi di prova

Ogni modello ha due casi separati, con slider propri:

| Caso | Valore iniziale | Applicazione |
|---|---|---|
| `Mxx_…_SOLETTA` | p = −1 kN/m² | Verticale globale Z sulle fasce trasversali |
| `Mxx_…_TRAVI` | q = −1 N/mm = −1 kN/m | Verticale globale Z sulle sole travi principali |

Per la soletta il carico lineare di ogni asta è **qᵢ = p × bᵢ / 1000**, con p in kN/m² e larghezza tributaria bᵢ in mm: qᵢ risulta in N/mm. Le fasce alle estremità hanno larghezza ridotta; sono usate le larghezze effettive del graticcio, anche quando la discretizzazione non è uniforme. Il segno negativo indica carico verso il basso.

Nel ponte rettilineo iniziale i risultanti sono **−900 kN sulla soletta** e **−270 kN sulle travi**, in ogni modello. I due casi restano indipendenti; modificare un ramo non modifica gli altri. Non sono carichi normativi da mantenere senza valutazione.

### Peso proprio e caso `Dead`

`Build Model` aggiunge il caso riservato **Dead**, oltre ai due casi di prova. La soletta non contribuisce al peso perché ha densità zero. Per le travi omogeneizzate, **Preserve Mass = True** mantiene la massa lineare della trave originale:

**ρ_om = ρ_acciaio × A_acciaio / A_om**.

La correzione riguarda esclusivamente la densità della copia di proprietà: non cambia E, area omogeneizzata o inerzie. Evita che l'area trasformata del cls venga contata come volume di acciaio nel peso proprio. Le proprietà di partenza restano inalterate. `Dead` è disponibile nelle analisi ma **non è incluso nelle quattro combinazioni di confronto**; l'eventuale peso della soletta dovrà essere applicato esplicitamente nella fase pertinente.

## Flusso dei componenti

1. **Geometria e conci:** modificare campate, tracciato, travi e diaframmi come nel 49. Dopo modifiche ai giunti, aggiornare il pannello **Tabella conci**. Le sezioni base vengono ancora definite con slider e override per riga.
2. **Sezioni miste:** il componente genera una proprietà per ogni concio e per ciascun coefficiente n. Gli ID mantengono la corrispondenza lungo tutto il flusso.
3. **Quattro override:** ogni ramo riceve la carpenteria di base e sostituisce le proprietà delle sole travi principali. Gli output separano travi principali, altra carpenteria e ID FEM.
4. **Carichi:** ciascun ramo carica le proprie travi principali e una copia delle fasce di soletta. Traversi e controventi non ricevono il carico lineare di prova delle travi principali.
5. **Quattro Build Model:** assemblano travi caricate, altra carpenteria, fasce caricate, appoggi e collegamenti eccentrici.
6. **Quattro Straus R3 Analysis:** premere Run su ciascuno, oppure portare a True **CALCOLA I QUATTRO MODELLI STRAUS**, attendere il completamento e riportare il toggle a False. I percorsi `.st7` sono espliciti nei pannelli a sinistra e inizialmente puntano alla sottocartella `calcoli` dell'esempio. Per trasferire il file su un altro PC, aggiornare questi percorsi.
7. **Azioni e fasi:** ogni caso di prova diventa un'azione. Le fasi 10/20/30/40 identificano i quattro rami. Le azioni selezionano le sole travi principali; gli altri elementi restano nei modelli FEM con i loro risultati.
8. **Build BridGHe Model:** dopo le analisi, premere Run sul componente finale. Riceve tutti e quattro i modelli calcolati, le quattro fasi e la tabella delle combinazioni. Prima delle analisi gli avvisi di attesa risultati sono normali.

Il selettore **Modello FEM da visualizzare** controlla preview, decomposizione e audit. Le uscite dei quattro modelli caricati e dei quattro modelli calcolati sono nominate separatamente.

## Ramo n da ritiro

**M04_NSH è per ora un confronto di rigidezza con carichi meccanici**, non un calcolo del ritiro reale. Usa il coefficiente n da ritiro, ma non applica deformazioni imposte, precarichi o gradienti termici.

Le due **Bridge Shrinkage Action** hanno incremento di deformazione **ε = 0** e associano i risultati meccanici alla risposta n da ritiro del verificatore. Non modificare soltanto quel valore per simulare il ritiro: per un ritiro reale occorre costruire anche il corrispondente caso FEM mediante **Composite Shrinkage Actions**, usare la stessa deformazione primaria e associare entrambi alla medesima azione. Questo passaggio resta da impostare quando verranno scelti i carichi definitivi.

## Combinazioni di confronto

Il pannello **COMBINAZIONI TEST** contiene una riga di intestazione con gli otto ID azione, seguita da quattro righe. Un componente **Text Split** trasforma le righe separate da `;` nell'albero richiesto da Build BridGHe Model.

- `TEST_M01_ACCIAIO`: soletta + travi del solo ramo acciaio.
- `TEST_M02_NINF`: soletta + travi del solo ramo n∞.
- `TEST_M03_N0`: soletta + travi del solo ramo n0.
- `TEST_M04_NSH`: soletta + travi del solo ramo n da ritiro.

I coefficienti valgono 1 per i due casi del ramo scelto e 0 per gli altri. Non vengono sommati quattro volte gli stessi carichi. Sono confronti tecnici, non combinazioni normative né una storia costruttiva già definita: i valori e le azioni da conservare saranno scelti successivamente.

## Ipotesi geometriche e verifiche

Gli assi FEM, gli offset e i link restano quelli del graticcio di base. Il cambio di proprietà non sposta automaticamente gli assi al baricentro della sezione mista; valgono la convenzione e le limitazioni del flusso di override già usato nel 99. Prima di usare il modello per un progetto, verificare la coerenza del riferimento delle sollecitazioni, delle eccentricità e della calibrazione del graticcio.

Le fasce cls sono attive anche in M01, come richiesto per il confronto. Questo ramo non rappresenta automaticamente una fase di getto fresco, nella quale la rigidezza della soletta deve essere valutata diversamente. Non viene aggiunta una shell che duplicherebbe il contributo longitudinale già contenuto nelle sezioni omogeneizzate. Questa definizione si ferma all'assemblaggio delle risposte; non esegue le verifiche finali del ponte.

## Rigenerazione e controlli riproducibili

```powershell
& '.\example\50-ponte-quattro-modelli\genera.ps1'
# Se il file esiste: -Force conserva prima una copia in artifacts.
# Per eseguire anche le quattro analisi con licenza Straus:
& '.\example\50-ponte-quattro-modelli\genera.ps1' -Force -Native
```

Il generatore è [ParametricBridgePhasesExample.cs](../../tests/BridGHe.Smoke/ParametricBridgePhasesExample.cs). La generazione controlla geometria e ID comuni, tutte le 24 assegnazioni per concio, proprietà in acciaio/generiche, densità nulla delle fasce, massa invariata della carpenteria, carichi sulle famiglie corrette, risultanti analitici e salvataggio/riapertura. Verifica inoltre che il file 49 conservi il proprio hash.

Un test separato verifica anche le connessioni fino al Build BridGHe Model usando sollecitazioni sintetiche riconoscibili, in una copia temporanea in memoria. Controlla che gli otto casi restino associati alle quattro fasi corrette. Questi dati vengono scartati e non sono mai salvati nella definizione consegnata: verificano l'instradamento dei risultati, non il comportamento strutturale.

[validation.json](validation.json) documenta i controlli del modello. Se eseguita con `-Native`, [native-validation.json](native-validation.json) riporta l'equilibrio delle reazioni dei casi effettivamente risolti da Straus e l'assemblaggio finale delle quattro fasi, oppure lo stato `not-completed` con il motivo dell'interruzione. L'esistenza della definizione GH non implica, da sola, che l'analisi nativa sia stata eseguita.
