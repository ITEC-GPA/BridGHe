# Refactoring BridGHe

## Estensione del modello di ponte — 20 ottobre 2026

- Dodici nuovi componenti con GUID nuovi e icone dedicate; inventario in `current-components.json`.
- `SimpleBridgeSolid/Truss`: uscita **Bridge Geometry** aggiunta in coda; le precedenti uscite 0–13 restano nello stesso ordine.
- `BridgeModelAudit.Inspect`: parametri opzionali per geometria attesa e modelli di fase; due ingressi opzionali aggiunti in coda al componente.
- `BridgeMemberFamily`: aggiunte `LongitudinalBracing=6` e `BracingPost=7`; metadata fisici separati dagli ID numerici FEM.
- `StrausR3Helper.CreateModelR3` / `CreateModelCore`: nodi con `NodeCoordinateAttributeModel` usano UCS dedicati per vincoli e molle; i nodi senza attributo mantengono il riferimento preesistente. `St7SetUCS` è una nuova chiamata, non una sostituzione di proprietà globali.
- `RigidConnectionAttributeModel`: esportazione con la nuova chiamata `St7SetRigidLink`, dopo la creazione dei nodi. Nessun elemento rigido fittizio tramite rigidezza elevata.
- `GH_PlateProperty`: aggiunto il costruttore vuoto richiesto da Grasshopper per leggere i dati persistenti. Formato dell'archivio invariato.
- `BridgeConstructionSequence`: produce snapshot indipendenti. Stage nativi solo per selezioni esatte di gruppi, proprietà/appoggi invariati, assenza di link rigidi e incrementi espliciti.

Le firme Straus provengono da `API Includes/Visual C#/St7API.cs` dell'installazione R3 e sono rigenerate da `tools/update_straus_bindings.py`; origine registrata in `migration/straus-api-source.json`. Le nuove prove native sono predisposte ma il tentativo ha incontrato `St7Init -8` (hardware lock non disponibile). I 238 valori della baseline dei percorsi precedenti restano esattamente invariati.

## Stato precedente — 19 ottobre 2026

Questa revisione usa i nomi BridGHe, il solutore Straus7 R3 e le librerie gPC. I nomi storici sono rimossi dai sorgenti e dalla distribuzione. La storia Git conserva i commit precedenti.

## Risultati e compatibilità

Prima delle ottimizzazioni è stata fissata una baseline di **238 valori su 14 casi**: instabilità attiva/disattiva, azioni di segno diverso, soletta inclusa/esclusa e ritiro. Dopo il refactoring tutti i 238 valori sono **esattamente uguali**; il test applica comunque una tolleranza relativa/assoluta di 1e-12. La baseline non è stata rigenerata dopo le modifiche.

L'eccezione autorizzata è la correzione degli **appoggi elastici distribuiti**. I modelli che usavano l'esportazione errata possono cambiare risultato. [Differenze, formule, unità e 29 controlli nativi](beam-support-fix.json). I modelli senza questo attributo non sono interessati dalla correzione.

Le porte sono state semplificate su richiesta. I GUID già assegnati a BridGHe restano stabili per i componenti mantenuti; i nuovi componenti hanno GUID propri. Gli esempi distribuiti usano i contratti aggiornati. I file GH modificati dall'utente con le porte eliminate vanno controllati; non è prevista una riconnessione automatica delle vecchie posizioni.

## Metodi e chiamate modificati

| Area | Implementazione corrente | Effetto |
|---|---|---|
| `BridgeCheckAdapter` | Costruttore con `BridgeAnalysisOptions` gPC | Eliminati wrapper di opzioni e argomenti normativi non utilizzati. |
| `HBridgeSection.Calculate(HBridgeInput)` | Invocazione diretta nel nuovo `Bridge Section Check` | H, H inclinata, cassone, fasi e dettagli nativi; unità mm, MPa, kN e kNm. |
| `BridgeStressResult` | Valutazione del taglio con proprietà di fase memorizzate nel singolo risultato | Ordine delle somme conservato; 8.000 letture da 1.106,66 ms a 17,23 ms, checksum identico. Non è il tempo dell'intero solutore. |
| Grafici | `BridgePlot` e `BridgePlotComponent` condivisi | SVG vettoriale e PNG, campioni originali, salti e lacune conservati, min/max e file opzionale. |
| `St7SetBeamSupport2` | Una chiamata per direzione -1/+1/-2/+2, caso di libertà 1, dati `[k,gap]` | Corregge indici, dati, lati, direzioni multiple e contatto; verifica NLA per gli appoggi unilaterali. |
| `StrausSection.Calculate` | Proprietà native più contorni ottenuti con `St7SaveBeamSectionMesh` | La mesh serve solo alla preview. Per le sezioni circolari la discretizzazione grafica è portata a 48 divisioni dopo la lettura delle proprietà esatte. |
| Lettura mesh di preview | `St7OpenFile`, `St7GetTotal`, `St7GetNodeXYZ`, `St7GetElementConnection` | Eseguita durante la creazione della sezione, mai nel callback di disegno; contorni conservati nell'archivio. |
| `GH_Model` | Preview composta di nodi, beam, plate e punti dei percorsi mobili | Riutilizza i wrapper e segue le modifiche geometriche. |
| `CompositeBeamComponent` | Sezione finale opzionale; gruppi aggiuntivi applicati senza duplicati | Omettendo la sezione finale si usa quella iniziale. L'input non viene modificato. |
| `GroupModel` | Confronto null-safe | Evita eccezioni nella risalita della gerarchia. |
| `BeamStringGroupAttributeModel.Equals(object)` | Controllo del tipo String Group corretto | Elimina un confronto errato e una possibile ricorsione infinita. |
| Archivio del modello | Versione del contenuto aggiornata; lettura degli archivi precedenti verificata | Rimossi contenitori vuoti non utilizzati; formato BGHA1 conservato. |

I binding P/Invoke sono estratti dall'header R3 installato; origine, hash e numero di dichiarazioni sono in `migration/straus-api-source.json`. Le firme non sono riscritte manualmente.

## Rimozioni

L'elenco dei file eliminati e le motivazioni sono in `migration/removed-unused-types.json`.

- Named sets, rilasci mediante named set e contenitori generici di funzioni senza consumatore nell'export.
- Operazioni legacy sui casi di carico prive di implementazione Straus. Restano le fasi native `StagedConstructionModel` e i metadati di fase del verificatore.
- Verificatori generici scollegati dal flusso degli elementi Straus, tipi Goo associati e parametro versione non necessario.
- Input Effective Epsilon, offset verticale della sezione composta e target Entity Set: non avevano un comportamento implementato e verificato.
- Input dei supporti non esportabili; rimangono rigidezza distribuita, direzione nativa, compressione soltanto e gap.
- Due input di Beam Output Stations senza effetto su Straus, insieme ai relativi dati non utilizzati. Il numero delle stazioni rimane attivo.
- Vecchio generatore di immagini e metodo privato duplicato `BeamModel.SetSurface`, senza chiamanti.

Le classi che sembrano poco referenziate ma servono a export/lettura/persistenza sono conservate: `StrausResultsReader`, `MovingLoadExporter`, `StrausBxsGenerator`, `ModelTypeResolver`. `EnumExtensions` è utilizzata tramite extension methods.

## Prestazioni e limiti

Il maggiore guadagno misurato riguarda la lettura ripetuta delle tensioni di taglio: [dati prima/dopo](shear-performance.json). Il nuovo renderer migliora soprattutto formato, gestione delle discontinuità e riuso; il [benchmark grafico](plot-benchmark-2026-10-18.json) non mostra una grande riduzione delle allocazioni.

I costi rimanenti dipendono da analisi native, clonazione/serializzazione di modelli grandi, tessellazione delle preview e iterazioni gPC delle larghezze efficaci. Le chiamate Straus rimangono serializzate dal lock di processo. Non è stata introdotta una parallelizzazione non verificata dell'API nativa.

Le nuove definizioni gPC usano direttamente i record della libreria. Gli adattatori del flusso FEM, il renderer e le preview restano interni a BridGHe per una futura migrazione in libreria.

## Riordino e flusso delle fasi — 19 ottobre 2026

Un'unica tab BridGHe. Sezioni Straus in 03-Sections, Beam Support in 07-Attributes, carichi mobili e relativa analisi in 09-Loads. Eliminati Section Geometry, Remesh e la proprietà composta obsoleta duplicata. Gli altri componenti di verifica senza impiego negli esempi sono elencati in USO-COMPONENTI.md: non vengono automaticamente considerati inutili.

Interfacce estese, con GUID e porte precedenti conservati:

- Concrete Slab Section: aggiunti area e distanza dall'asse della fila inferiore; area nulla per gli archivi precedenti. Archivio soletta versione 2.
- Composite Beam Property: aggiunti `n ritiro` opzionale e uscita generica omogeneizzata. Archivio proprietà composta versione 4; se il valore manca, viene usato n∞.
- Build BridGHe Model: input aggiunto in coda per i casi FEM calcolati con n ritiro. `NShrinkage` è aggiunto alla fine dell'enum delle fasi, senza rinumerare i valori precedenti.
- Nuovi componenti: Composite Shrinkage Actions, Display Settings, Preview BridGHe Model, BridGHe Panel.

Metodi modificati: `CompositeBeamPropertyModel.CreateHomogenizedProperty` centralizza le proprietà riferite all'acciaio; `BridgeCheckAdapter.GetLinearAnalysisResultByStages` accetta opzionalmente rapporto e sollecitazioni del ritiro; `BridgePhaseAssembler` conserva il contributo FEM di ritiro separato; `BridgeMergeValidation` verifica ID/geometria e unifica le stazioni entro la tolleranza del verificatore. Gli overload precedenti mantengono i default. Le chiamate Straus non hanno una nuova firma: le proprietà omogeneizzate vengono inviate con `St7SetBeamSectionPropertyData`, evitando la chiamata a geometria nulla. Le vecchie sezioni di tipo Composite vengono trattate come generiche nell'export.

Differenze corrette: abbinamento delle beam per ID anziché per indice; stazioni normalizzate convertite in lunghezze; prefissi distinti per casi di ritiro omonimi provenienti da modelli diversi; coefficienti delle combinazioni letti senza confonderli con nomi numerici; errori per fasi ambigue o mancanti. Le stazioni native quasi coincidenti non generano più campioni duplicati. L'esempio 19 verifica la conservazione esatta di ogni sollecitazione originale. Le convenzioni di rigidezza a taglio e torsione precedenti sono documentate nella guida dell'esempio.

Preview: `SectionModel` versione 7 conserva metadati fisici separati dalle proprietà FEM. Sono ricostruiti profilo e soletta anche dopo Beam Override; per flange asimmetriche la soletta appoggia correttamente sulla flangia superiore. I metadati grafici non modificano area o inerzie esportate.
