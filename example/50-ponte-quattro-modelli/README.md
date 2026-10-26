# 50 · Ponte parametrico: quattro modelli, fasi, ritiro e verifiche

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

## REV003 — carichi mobili sul modello n0

Aprire **[50-ponte-quattro-modelli_REV003.ghx](50-ponte-quattro-modelli_REV003.ghx)**. Aggiunge un ramo mobile sullo stesso modello n0, le azioni delle posizioni nella fase 30 e combinazioni alternative collegate al verificatore e al report. Il REV002 è conservato. Input, procedura e ipotesi sono nella **[guida REV003](README_REV003.md)**.

## REV002 — stati locali da momento sotto peso proprio

Aprire **[50-ponte-quattro-modelli_REV002.ghx](50-ponte-quattro-modelli_REV002.ghx)** con la Release aggiornata. Deriva dal REV001 effettivamente salvato dall'utente, inclusi tracciato, controlli, sezioni e prove aggiunte al canvas. Il REV001 è conservato. **[Sezioni_conci_REV002.xlsx](Sezioni_conci_REV002.xlsx)** è una copia del suo Excel, con una riga per concio fisico: non occorre aggiungere righe per distinguere rigidezza fessurata e non fessurata.

Il nuovo blocco orizzontale, fra M01 e M02, legge le sole longitudinali del modello acciaio calcolato e classifica **tutte le stazioni del caso Dead**, con My/BM2. Le due nuove assegnazioni usano lo stesso stato per M02 n∞ e M03 n0; M04 ritiro resta non fessurato per scelta esplicita. Nodi, aste e collegamenti restano comuni ai quattro modelli.

| Risultati di Dead sull'asta | Proprietà meccanica in M02/M03 |
|---|---|
| Positivi, con eventuali zeri | Sezione composta non fessurata |
| Negativi, con eventuali zeri | Acciaio + armature, calcestruzzo escluso |
| Entrambi i segni | Politica esplicita: nel file **2, tutta l'asta fessurata** |
| Tutti entro ±1000 N mm | Nel file **0, non fessurata** |
| Nessun risultato | Attesa, senza assegnazione sostitutiva |

La scelta per le aste miste è un'approssimazione di mesh, **non un limite di sicurezza**. L'uscita `ASTE A SEGNO VARIABILE` e la tabella mostrano i casi interessati. Per ridurre l'approssimazione, diminuire `Maximum Element Length` nella geometria comune e ricalcolare i quattro modelli. Non vengono creati giunti costruttivi negli zeri del momento. La mappa da Dead non costituisce una valutazione normativa della fessurazione e non rappresenta automaticamente ogni combinazione di esercizio.

Procedura:

1. Aprire l'Excel REV002, modificare le sezioni se necessario, salvare; usare **Run** su **Bridge Read Section Excel**.
2. Lanciare **Run** su M01. Il caso `Dead` è incluso nella selezione dei risultati per costruire la mappa; non viene aggiunto automaticamente alle combinazioni già impostate.
3. Controllare classi, segno, tolleranza e politica per le aste miste. Dopo modifiche a geometria, sezioni, appoggi o carichi, ricalcolare M01.
4. Lanciare **Run** su M02 e M03, poi M04. M04 conserva solo il caso di deformazione da ritiro: stesso εcs per effetti FEM secondari e primari locali, sezione non fessurata.
5. **Run** su `Build BridGHe Model`, quindi su `Super Element Check`. Tutte le aste longitudinali confluiscono nelle verifiche di sezione e nei rami pioli/irrigiditori già presenti.
6. Controllare i grafici e generare il report con **Generate**. Le ipotesi di stato locale e di verifica N–Mx–Vy viaggiano con i risultati. Il documento di destinazione è `Report_50_REV002.docx`; i modelli Straus vanno in `calcoli/REV002`.

Il file distribuito non contiene risultati FEM ed è salvato con **Keep Running=False**. La validazione offline percorre geometria, proprietà, azioni, merge, verifiche, dettagli, grafici e ingressi del report usando risultati controllati in un documento temporaneo, poi li scarta. **Il 26 ottobre sono state eseguite le quattro analisi native del REV002 salvato: 498 aste e 488 nodi per modello, equilibrio delle reazioni, merge di cinque combinazioni, 4.560 risultati di sezione, tre rami dettagli e sei grafici.** Resta da svolgere una valutazione di sensibilità della soluzione del ponte alla mesh. Nessun report di calcolo sintetico viene consegnato come risultato del ponte.

Ripetere i controlli con `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --example50-rev002-check`. Lo script `--example50-rev002` genera una nuova copia dal REV001 e rifiuta di sovrascrivere un REV002 già esistente.

## REV001 e versione base

La revisione personale **[50-ponte-quattro-modelli_REV001.ghx](50-ponte-quattro-modelli_REV001.ghx)** include ora **Bridge Support Stations** e **Bridge Read Section Excel**, collegati al flusso esistente. La **[guida agli input del REV001](INPUT-REV001.md)** spiega progressive degli appoggi e creazione/lettura del foglio **[Sezioni_conci_REV001.xlsx](Sezioni_conci_REV001.xlsx)**. L'integrazione conserva geometria, sezioni e altri dati della revisione salvata dall'utente. I valori dimostrativi descritti sotto si riferiscono alla versione base.

Aprire **[50-ponte-quattro-modelli.ghx](50-ponte-quattro-modelli.ghx)** con la Release aggiornata di BridGHe per Rhino 8. L'esempio deriva da una copia del **49**, che resta invariato. Il flusso si legge **da sinistra a destra**; ogni componente ha i propri input espliciti a sinistra e gli output nominati a destra.

## Organizzazione del canvas

Le undici zone numerate seguono questa sequenza: **geometria → materiali e sezioni → graticcio comune → modello 0 acciaio → modello 1 n∞ → modello 2 n0 → modello 3 ritiro → assemblaggio → verifiche → grafici → report**.

La numerazione visiva 0–3 corrisponde ai modelli esistenti M01–M04: nomi dei casi, identificativi delle fasi e combinazioni restano gli stessi. In ciascun ramo di calcolo, leggere proprietà, carichi/deformazioni, Build Model e analisi, quindi azioni e fase. I componenti paralleli sono impilati nello stesso blocco; i modelli successivi proseguono verso destra.

I controlli condivisi si trovano presso il primo componente che li utilizza. I collegamenti fra zone sono impostati su **Hidden** per evitare fasci di fili attraverso l'intera definizione: **selezionare il componente destinatario per vederli**. I collegamenti locali restano visibili. I sei grafici sono organizzati in due gruppi affiancati di tre righe, ciascuna con selezione della trave, generazione e anteprima.

Il riordino modifica soltanto posizioni, dimensioni e annotazioni del canvas e visualizzazione dei fili. Valori, GUID dei componenti e dei parametri, sorgenti ordinate, cache e impostazioni di calcolo sono conservati.

Configurazione iniziale: tre campate da 30 m, tre travi, 24 conci, diaframmi, controventi, fasce trasversali di soletta alla quota reale, link rigidi e 12 appoggi. Ciascun modello contiene 324 beam (108 longitudinali) e 266 nodi. Carichi e dettagli sono dimostrativi: questo file non attesta un progetto verificato.

## Modelli, azioni e fasi

| Fase | Modello | Proprietà longitudinali | Azioni |
|---|---|---|---|
| 10 | M01_ACCIAIO | I in solo acciaio, per concio | M01_ACCIAIO_SOLETTA, M01_ACCIAIO_TRAVI |
| 20 | M02_NINF | Misto con n∞ = 18 | M02_NINF_SOLETTA, M02_NINF_TRAVI |
| 30 | M03_N0 | Misto con n0 = 7 | M03_N0_SOLETTA, M03_N0_TRAVI |
| 40 | M04_NSH | Misto con n ritiro = 12 | **M04_NSH_RITIRO**, unica azione |

n∞ è un coefficiente finito a lungo termine. I tre rapporti modulari restano slider indipendenti, con valori dimostrativi; non sono calcolati automaticamente dalle condizioni ambientali. La larghezza efficace iniziale è 3000 mm per trave; la soletta ha due file di armatura B450C.

**Bridge Segment Override** abbina le proprietà FEM tramite G1-C1, G1-C2, ecc. **Bridge Segment Composite** abbina agli stessi ID le sezioni miste per il ritiro e il verificatore: un concio può contenere più aste FEM. Liste riordinate sono ammesse; ID mancanti o duplicati sono errori.

La carpenteria non longitudinale mantiene le proprietà d'acciaio. Le fasce trasversali hanno cls fittizio con E = 30000 MPa, ν = 0,20, G = E/[2(1+ν)] e **densità zero**. Sono attive anche in M01: questa scelta di confronto non rappresenta automaticamente un getto fresco privo di rigidezza. Viscosità e fessurazione trasversali non sono applicate automaticamente. Le sezioni omogeneizzate sono generiche Straus riferite all'acciaio; assi FEM ed eccentricità seguono il 49.

## Carichi meccanici e ritiro

Soltanto M01, M02 e M03 hanno due carichi meccanici di prova:

- Soletta: p = −1 kN/m² in Z globale. Ogni fascia riceve qᵢ = p × bᵢ / 1000 N/mm, con bᵢ larghezza tributaria effettiva in mm. Risultante iniziale −900 kN.
- Travi principali: q = −1 N/mm in Z globale. Risultante iniziale −270 kN.

**M04 non contiene carichi meccanici di prova.** Creep And Shrinkage Concrete calcola εcs tra 7 e 365 giorni, RH 70%, cemento N, fck 32 MPa. Ac = larghezza efficace × spessore; u = 2 × larghezza efficace assume due facce esposte ed esclude i bordi. Cambiando classe del cls, aggiornare anche fck nel calcolo del ritiro.

Lo **stesso εcs**, negativo per contrazione, segue due percorsi:

1. Sezione mista di ogni asta → Composite Shrinkage Actions → Beam Pre Load in modalità **Strain** + Beam Gradient Load, asse 2 → M04 → Straus. Prestrain e gradiente rappresentano deformazione assiale e curvatura equivalenti della stessa azione; il gradiente non è un ulteriore carico termico.
2. Bridge Shrinkage Action: stesso εcs, modello M04 calcolato, caso M04_NSH_RITIRO, **Primary Only = False**, Response = n ritiro. Il verificatore associa autotensione primaria ed effetti di vincolo FEM. Non aggiungere un secondo ritiro primario.

Le azioni riferiscono l'intero modello: **Beam IDs è vuoto**, per conservare le reazioni globali. Le travi da verificare vengono selezionate dopo il Build.

I valori di questa sezione descrivono la versione base: i REV001/REV002 conservano gli input modificati dall’utente. Il sistema di esportazione prevede ancora il caso riservato **Dead**. Preserve Mass mantiene la massa fisica dell'acciaio: ρ_om = ρ_acciaio A_acciaio / A_om, senza modificare E o inerzie. **Dead non entra nelle azioni/combinazioni dell'esempio**; l’eventuale lettura diagnostica di Dead in M04 non lo inserisce nell’azione ritiro. La fase ritiro riceve soltanto M04_NSH_RITIRO.

## Build e verifiche

I quattro modelli calcolati e le quattro Bridge Phase entrano in **Build BridGHe Model**, modalità Cumulative. La tabella ha sette colonne di azione e cinque combinazioni:

- Quattro TEST_Mxx isolano le singole risposte.
- DEMO_CUMULATIVA assegna 1 a tutte le sette azioni: i sei casi meccanici rappresentano **tre incrementi distinti**, pur avendo uguali intensità iniziali. Non devono essere tre copie dello stesso carico fisico. Il ritiro viene contato una volta sola, con lo stesso coefficiente per primario e secondario.

Non sono combinazioni normative. Bridge Analysis Options imposta esplicitamente Ultimate per esercitare le verifiche locali: **non applica fattori ai carichi**. Sostituire azioni, combinazioni e coefficienti con quelli di progetto.

Il modello unito viene scomposto; le sezioni miste sono associate per concio alle aste **con risultati**. Composite beam e Super Element Check ricevono tutte le 108 aste longitudinali. A valle:

- Sezioni e pioli: tutte le stazioni, combinazioni e travi.
- Irrigiditori intermedi: ai giunti dei conci, selezionati con Bridge Detail Stations.
- Irrigiditori agli appoggi: posizioni reali e reazioni combinate dalle stesse fasi. La reazione firmata è esposta; il sollevamento non diventa compressione.
- Piatti, saldature e pioli sono input dimostrativi. La lunghezza di pannello assunta è **12000 mm**: è un input esplicito, non una ricostruzione automatica delle distanze locali. Verificarla rispetto a giunti e appoggi.
- Bridge Check Summary e Bridge Result Labels raccolgono tutte le verifiche.
- Sei grafici con anteprima GH: momento, tensioni, tau, pioli Ed/Rd, WR intermedi e WR appoggi. Bridge Girder Results seleziona una trave e reindicizza i risultati: non concatena tre travi in una falsa ascissa di 270 m. Per gli irrigiditori si può scegliere la singola voce o l'inviluppo WR.

**Ipotesi del checker H: verifica nel piano N–Mx–Vy.** Torsione T, momento sull’asse debole M2 e taglio trasversale V1 sono trascurati nelle verifiche di sezione, pioli e irrigiditori. Restano integri nei risultati FEM e nei grafici delle sollecitazioni. Gli effetti esclusi e le loro interazioni non sono verificati. L’ipotesi è visibile sui componenti, accompagna ogni risultato e compare nel riepilogo del report. Non è attestata una verifica completa sotto qualsiasi azione. Stabilità globale, fatica, traversi, ancoraggi e flessione locale della soletta richiedono verifiche dedicate.

## Sequenza operativa e report

1. Controllare geometria, sezioni, materiali, rapporti modulari, ritiro, carichi, combinazioni e dettagli.
2. Con licenza disponibile, attivare **CALCOLA I QUATTRO MODELLI STRAUS**, attendere, controllare i log e riportare False.
3. Premere **Run** su ASSEMBLA QUATTRO MODELLI E FASI.
4. Premere **Run** su Verifiche di tutte le travi. Controllare l’ipotesi N–Mx–Vy e le sollecitazioni T, M2 e V1 escluse dalle verifiche.
5. Controllare riepilogo, dettagli ed eventuali WR mancanti; selezionare la trave per i grafici, che si aggiornano automaticamente.
6. Nel Bridge Report scegliere i capitoli, verificare i percorsi e premere **Generate**.

Il report usa **[Template_BridGHe.docx](Template_BridGHe.docx)**, copiato dal template esistente. Riceve i risultati di **tutte** le travi e dei dettagli; le figure riguardano la trave selezionata. Overwrite è inizialmente False. Dopo modifiche ripetere analisi, Build e verifiche: aggiornare i grafici non ricalcola la struttura.

## Validazione e rigenerazione

La prova offline verifica topologia, massa, carichi, sezioni per concio, unico εcs, combinazioni, collegamenti e riapertura. Una copia temporanea con dati controllati verifica 4 fasi, 7 azioni, 5 combinazioni, **1080 risultati di sezione**, tre rami di dettaglio e sei grafici, compreso il cambio G1/G2/G3. **I risultati sintetici vengono scartati e non sono nel GHX consegnato**; non attestano una soluzione FEM.

Analisi Straus e report numerico reale restano da eseguire. native-validation.json dichiara solverResultsVerified: false. Non è prodotto un report Word con risultati simulati.

Per rigenerare da PowerShell: `.\example\50-ponte-quattro-modelli\genera.ps1 -Force`. Il comando crea un backup, riparte dal 49 e non usa Straus. Aggiungere `-Native` soltanto quando autorizzato e con licenza disponibile: esegue analisi, equilibrio, merge e verifiche. Il report resta a generazione manuale.

La rigenerazione usa i valori del generatore: per conservare personalizzazioni nel 50, usare il GHX e i pulsanti. Backup in artifacts/four-models-backups; file Straus in calcoli; Release in bin/Release.

La rigenerazione applica automaticamente il layout orizzontale. Per ridisporre **il file 50 esistente conservandone i dati**, dalla root eseguire:

```powershell
dotnet build tests/BridGHe.Smoke -c Release -p:BuildProjectReferences=false
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --example50-horizontal
```

Questo comando non risolve il grafo né avvia Straus. Prima di sostituire il file controlla l'identità del contenuto funzionale XML, la riapertura e l'assenza di sovrapposizioni. Crea un backup in `artifacts/example50-horizontal` e aggiorna [layout-validation.json](layout-validation.json). Le validazioni numeriche precedenti restano separate da questa verifica del canvas.
