# Trasposizione e refactoring — 16 ottobre 2026

## Correzioni emerse dal ponte a graticcio — 17 ottobre 2026

| Commit | Metodo | Correzione |
|---|---|---|
| `0c96b97` | `BridgeCheckAdapter.Phase` | Rifiuto dei dati non finiti; tolleranze dimensionali di 10⁻⁶ N e 10⁻³ Nmm sulle componenti fuori piano, per non rifiutare il rumore numerico di un graticcio simmetrico. Le azioni maggiori restano bloccanti e sono riportate nell'errore. |
| `09ce512` | `CalculateHomogenizedFactorPhi` nei tre componenti MeCheck2 che lo usano | Nuovo helper `BridgeCreepCoefficient.FromModularRatio`: il rapporto istantaneo Es/Ec non produce più φ = −1,11·10⁻¹⁶. Solo valori in [−10⁻¹², 0) vengono ricondotti a zero; rapporti fisicamente non validi sono rifiutati. |
| `eb79d8a` | `BuildMeCheckModel.SolveInstance` | Le combinazioni riutilizzano i casi canonici del modello con la fase assegnata, invece di creare copie contenenti solo il nome. Il flusso completo ora giunge alle tensioni. |

Ogni fix ha un commit distinto. Nessuna modifica a porte, GUID, DLL gPC o chiamate Straus in questa attività. L'esempio 11 è generato da codice di test, non aggiunge componenti al plugin. Le ipotesi e i limiti numerici sono nella [guida](../examples/bridge-grillage/README.md).

## Passaggi eseguiti

1. Creata una soluzione autonoma `BridGHe.sln`, con progetti Common e Grasshopper, target Windows x64 / .NET 8. La base FeMM è stata letta senza modificarla.
2. Copiati i componenti pertinenti al flusso modello–Straus–risultati–verifica. Mantenuti tutti i 16 componenti MeCheck2, inclusi i componenti di stampa. Riassegnati i GUID e l'identità del plugin.
3. Sostituiti i riferimenti delle librerie con le DLL gPC disponibili nei repository fratelli. I riferimenti sono espliciti, senza includere indiscriminatamente tutte le DLL presenti in una cartella.
4. Aggiornato il binding P/Invoke all'header della versione Straus R3 installata. Il file generato contiene solo i membri utilizzati; origine e SHA-256 sono in `migration/straus-api-source.json`.
5. Introdotti 23 componenti per sezioni Straus e 10 componenti per carichi mobili. Il catalogo e le dimensioni sono descritti in USO.
6. Separati gestione API, sezioni, generazione BXS, esportazione carichi mobili, avvio analisi e lettura risultati. Le operazioni native passano da un unico lock per processo e verificano i codici di ritorno.
7. Adattato MeCheck2 al motore gPC corrente. Corretti scambio delle fasi istantanea/differita, accumulo delle deformazioni imposte, lettura dei flag di instabilità e interpolazione della soletta nelle sezioni variabili.
8. Eliminati componenti e classi specifici dei solutori esclusi e del form finding. Rimossi riferimenti a database esterni, vecchie finestre e integrazioni con il precedente plugin Rhino. Potate le risorse grafiche non referenziate.
9. Sostituita la serializzazione BinaryFormatter con un archivio condiviso, versionato, a riferimenti preservati. Corretti salvataggio delle proprietà composte, sezioni annidate, attributi, risultati e metadati delle sezioni native.
10. Eseguiti compilazione Release, audit dei contratti, prove numeriche e istanziazione nel runtime Rhino 8. Preparati script di build, pacchetto locale e manifest delle dipendenze.
11. Creato il logo BridGHe e un sistema di icone SVG per tutti i 127 componenti e 9 parametri. Incorporati PNG da 24 px, registrate le due categorie con icone da 16 px e sostituite 94 vecchie icone. Porte e GUID mantenuti; dettagli in [ICONE](ICONE.md).

## Metodi e comportamenti cambiati

| Area / chiamata precedente | Implementazione corrente | Effetto e limite |
|---|---|---|
| Riferimenti ai vecchi assembly di geometria, modello e verifiche | `GPCGeometry`, `GPCModel`, `GPCUtilities`, `GPCChecker.*` | Namespace e assembly adattati; nessun riferimento alla precedente società nel progetto. |
| `SectionCheckerModelCode2010.GetLinearAnalysisResultByStages` | `BridgeCheckAdapter` → `HBridgeSection.Calculate(HBridgeInput)` | Conservati gli output MeCheck2 tramite `BridgeStressResult`. Il motore corrente ha semantica Eurocode 4 / SLE rara per il calcolo tensionale; non equivale automaticamente a ogni opzione del vecchio checker. |
| `SectionCheckerModelCode2010.GetSelfTension` | Fase gPC `BridgePhaseKind.Shrinkage` | Deformazione imposta convertita da adimensionale a microstrain; effetti primari della soletta. |
| `StressAnalysisResult.MeCheckResults.Point1…Point8` | `BridgeStressResult` | Tensioni ricostruite nei punti geometrici storici. Taglio dell'anima con somma dei contributi di fase `VQ/(It)`; tensione ideale `sqrt(sigma² + 3 tau²)`. Punto di armatura assente → `NaN`. |
| Output `TauB` | Nessuna corrispondenza assunta | `NaN` e warning: occorre verificare la definizione fisica originaria prima di associarlo ai nuovi risultati di connessione. |
| `ThinWalls.SubjectToLocalInstability` | `BridgeAnalysisOptions.WebBuckling`, `TopFlangeBuckling`, `BottomFlangeBuckling` | Flag separati. `Effective Epsilon=true` non ha un equivalente verificato e provoca errore esplicito. |
| Vecchie chiamate temporali del calcestruzzo | `ConcreteTimeEffects(...).AtAge(t).CreepCoefficient` | `t=0` conserva il significato di tempo infinito. `n0=Es/Ecm`, `n(t)=n0[1+phi(t)/1.05]`, perché phi corrente è riferito a `Ec=1.05 Ecm`. |
| Vecchio ritiro dipendente dal tempo | Nessun metodo equivalente utilizzato | Output `epsilonCS=NaN`. Il metodo legacy a tempo infinito non è usato: nel sorgente gPC consultato sono presenti problemi nella determinazione di `kh`/interpolazione. Non sono state modificate le librerie esterne. |
| `ReinforcedConcreteSection.CreateBridgeSection(..., verticalOffset, ...)` | `CompositeSectionFactory.Create` | Il parametro corrente è eccentricità **orizzontale**. Offset verticale diverso da zero rifiutato, senza reinterpretarlo. |
| `GetHomogeneizedMechanicalProperties` con flag di inclusione | `CompositePropertyAdapter.Calculate` → API gPC con `phi` | Selezione di soletta, barre e acciaio sul modello temporaneo; ricostruzione dei momenti d'area quando il calcestruzzo è escluso. |
| Operatore di scala delle forze non più disponibile | `BridgeCheckAdapter.Scale` | Scalate tutte le sei componenti, incluso fattore zero. |
| Conversioni dei momenti duplicate nei componenti MeCheck2 | `BridgeForceConvention.FromStraus` | `BM1/BM2` sono momenti nei piani; gPC usa momenti attorno agli assi. Un'unica conversione, con test di segno. |
| Aggregazione delle fasi dentro `SuperElementCheckComponent` | `BridgePhaseAssembler.Assemble` | Lookup per caso/stazione, fasi esplicite, deformazioni sommate. Risultati mancanti o ambigui producono errore, non una forza nulla. |
| Generazione BXS mediante processo R2 esterno | `StrausBxsGenerator` → `St7GenerateBXS` R3 | Mesh gPC/Delaunay e modello API temporaneo; nessun eseguibile R2. Richiede prova nativa. |
| Gestione nativa distribuita tra componenti | `StrausRuntime`, `StrausAnalysis`, `StrausResultsReader` | Inizializzazione, DLL, ID dei modelli, chiusura file e lettura risultati centralizzati. Analisi su copia del modello. |
| Serializzazione locale dei singoli Goo / BinaryFormatter | `ModelArchive` / formato `BGHA1` | Conserva oggetti condivisi e dati annidati. Gli archivi binari legacy non sono importati automaticamente. |
| Selettori dei cataloghi esterni | Warning che invita a collegare i componenti di costruzione | Nessun database legacy distribuito. Le porte dei parametri sono mantenute. |
| Bake tramite il precedente plugin Rhino | Geometria Rhino nativa | Linee, punti e mesh; il bake non conserva automaticamente il modello strutturale nei dati utente Rhino. |

Le nuove API native per sezioni comprendono `St7SetBeamSectionGeometry`, `St7SetBeamSectionGeometryBGL`, `St7CalculateBeamSectionProperties`, `St7GetBeamSectionPropertyData`, `St7AssignBXS`, `St7AssignLibraryBeamSection` e `St7AssignLibraryBeamSectionBGL`. Per i carichi mobili sono usate le famiglie `St7*LoadPathTemplate*`, `St7SetLoadPath` e `St7ConvertLoadPathsToLoadCases`, con firme estratte dall'header installato.

## Pulizia e ottimizzazione

Esclusi SAP/ETABS, componenti R2, cable net/form finding (incluse force density e condizioni di lunghezza/forza imposte al vecchio solutore), preferenze di progetto associate ai vecchi solutori, Story, link non esportati, insertion point SAP, trasferimenti di carico SAP e proprietà non prismatiche specifiche SAP. L'elenco dei file è in `migration/excluded-components.json` e `migration/cleanup-removed.json`.

Restano alcuni testi storici nelle porte, per esempio le due uscite plate un tempo specifiche SAP. In R3 tali valori sono `NaN`; non indicano una dipendenza dal solutore escluso.

Le ottimizzazioni eseguite riguardano la cache delle immagini, la deduplicazione dei template mobili, l'assemblaggio delle fasi tramite lookup e la rimozione di codice/dipendenze inutilizzati. Non è stato eseguito un benchmark di prestazioni con un ponte completo: non viene dichiarata una percentuale di accelerazione. L'esportatore R3 conserva ancora parte della struttura della base; un'ulteriore riscrittura richiede prima i benchmark nativi descritti in VERIFICHE.

## Identità e contratti

GUID plugin: `db9c536d-770b-4586-8c8c-1c7a4df232b4`. Ogni componente e parametro ha un GUID nuovo, isolato dai GUID della base. Dopo questa consegna i GUID devono restare stabili.

L'audit confronta i token dei metodi `RegisterInputParams` / `RegisterOutputParams`, inclusi testo, ordine, accesso, default e opzionalità. Ignora commenti e spazi; normalizza solo le sostituzioni necessarie di namespace, enum del solutore e accesso alla tolleranza. La baseline è stata ricavata dalla base FeMM, non dal codice nuovo.

## Revisione correttiva

Correzioni successive alla revisione del codice, con un commit per difetto. Porte e GUID dei componenti/plugin non modificati. I nuovi controlli riguardano il comportamento del software; la validazione strutturale e nativa resta distinta.

| Commit | Correzione |
|---|---|
| `9f02c7a` | R7: inizializzazione delle geometrie della trave composta durante deserializzazione. |
| `be9eea4` | R7: sostituzione dei solidi di anteprima senza accumulo. |
| `407152b` | R1: aggregazione di forze e momenti nodali per caso prima delle chiamate Straus. |
| `55a3b46` | R2: mappa unica per nome dei casi, senza mutare gli ID delle istanze. |
| `9086c08` | R3: conservazione di carichi, attributi, gruppi e identità dei nodi nel merge. |
| `17747f7` | R4: rimappatura dei vertici delle mesh e conservazione delle facce valide. |
| `8caa181` | Validità delle proprietà beam polimorfica; legge cutoff richiesta solo per cutoff bar. |
| `8542bd6` | R8: validità FEM delle travi native indipendente dalla presenza di un solido di anteprima. |
| `4d63918` | R5: mappa GUID completa, persistita e ripristinabile più volte senza collisioni. |
| `ecfd303` | R6: copie indipendenti di modelli, elementi e Goo, con riferimenti interni conservati. |
| `726b580` | R9: inclusione dei casi presenti soltanto nelle combinazioni di fase. |
| `399dcb3` | R9: selezione dei risultati coerente con solutore e fase, senza rinumerazione. |
| `9bf34c5` | Merge delle piastre basato sui nodi FEM, senza usare i vertici della geometria estrusa. |
| `192f558` | Errori bloccanti separati dagli avvisi; eliminato il filtro basato sulle parole Failed/Unable. |
| `5a7f071` | Ripristino degli aggiornamenti della geometria all'aggiunta/rimozione di attributi dopo archivio. |
| `9adc51e` | Aggiornamento delle piastre dopo sostituzione, modifica o svuotamento della collezione dei nodi. |
| `bec76f1` | Travi incomplete ispezionabili senza eccezioni e senza anteprime obsolete. |
| `62d9094` | Cache delle bounding box invalidata alla modifica della geometria, anche per travi native. |
| `63abc53` | Descrizioni di validità corrette per Goo piastra e trave composta. |

Ottimizzazioni separate: `1bbeba3` indicizza i risultati MeCheck2 una volta per trave e rimuove le scansioni ridondanti; `3a34c87` riutilizza un indice spaziale per la connettività Straus. `950f4fb` rende opzionale il test delle categorie che carica i plugin esterni del profilo. Misure e copertura sono in [VERIFICHE](VERIFICHE.md).

### Metodi modificati durante le correzioni

- `ModelHelper.GetLoadCases` considera anche le fasi; `GetLoadCaseNumbers` centralizza la numerazione locale.
- `NodalLoadAccumulator.Sum` sostituisce l'accumulo implicito tramite letture/scritture native ripetute.
- `GH_Model.FromModel`, `GetGuid`, `SynchronizeGuids`, `Write`, `Read` e `Duplicate` gestiscono identità e persistenza; i costruttori di copia dei modelli e `DuplicateGeometry` isolano i dati mutabili.
- `StrausResultSelection.Catalog/Select` è condiviso da UI e analisi; `StrausAnalysis.Run` usa i risultati selezionati anche nelle fasi.
- `BridgePhaseAssembler.CreateIndex(...).Assemble(...)` è usato da MeCheck2. La precedente firma statica `Assemble` resta disponibile.
- `StrausNodeIndex.Find` sostituisce le scansioni complete dei nodi nell'esportatore.
- `CreateShapes`, `PlateModel.Points` e `GeometryRevision` governano rigenerazione e invalidazione delle anteprime.

Non sono state modificate le DLL gPC, le firme P/Invoke R3 o le porte Grasshopper durante questa revisione.

## Collaudo nativo del 17 ottobre 2026

Con la licenza API disponibile sono stati eseguiti dieci benchmark e cinque contributi di fase composta, oltre a salvare, riaprire e risolvere le dieci definizioni Grasshopper. Formule, unità, tolleranze e risultati: [esempi](../examples/README.md).

| Commit | Metodo corretto | Difetto e comportamento verificato |
|---|---|---|
| `69934c0` | `StrausSection.Assign` | `St7CalculateBeamSectionProperties` ammette solo le forme standard. Per BGL si leggono le proprietà calcolate da `St7SetBeamSectionGeometryBGL`; tutte le 21 famiglie passano. |
| `969f61b` | `StrausR3Helper.CreateModelR3` | Dopo `St7AddStage` viene chiamato `St7EnableNLAStage`: senza abilitazione il solutore restituiva 1018, incrementi non definiti. |
| `d6a9229` | `StrausResultsReader.Read` | `St7GetElementResultState` distingue gli elementi inattivi; non si richiedono le loro forze e non si generano zeri fittizi. Gli altri errori API restano bloccanti. |
| `b86494b` | `StrausAnalysis.Run` | Controllo del numero di incrementi, di `St7GetResultCaseConvergence` e di `St7GetResultCaseStage` per tutta la storia NLA, anche per incrementi non selezionati in output. |
| `6fe4d7d` | `BridgeForceConvention.FromStraus` | I valori definiti negli assi positivi della sezione vengono trasformati nel riferimento di destinazione gPC. Il precedente codice assegnava direttamente il riferimento `(-x,-y)` e invertiva i segni in MeCheck2. Correzione verificata con flessione pura nativa e tensioni di lembo. |

Le dichiarazioni aggiunte al binding sono rigenerate dall'header R3 installato, non adattate manualmente. Nessuna modifica a porte, GUID o sorgenti gPC. I benchmark di sezione composta esplicitano l'approssimazione a linea media delle flange del motore attuale. Le vecchie verifiche MeCheck2 vanno ricalcolate per effetto della correzione del segno, mentre le uscite beam continuano a riportare BM1/BM2 nella convenzione Straus.
