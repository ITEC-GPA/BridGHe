# Rilascio ufficiale GPC — 1° novembre 2026

Dal 1° novembre BridGHe e ReBear usano il set ufficiale che aggiunge a CompositeBridge il ricalcolo dei dettagli dalle situazioni di un'analisi precedente. Il set è prodotto con `Checker/build/Release-Set.ps1`, senza `-Candidate`, da checkout puliti dei commit pubblicati; le versioni sono controllate rispetto al set precedente. Il [manifest distribuito](../lib/GPC/release-set.json) contiene `candidate=false` e `onOriginMaster=true` per tutti i repository.

| Repository | Commit |
|---|---|
| Utilities | `e57b440b` |
| Geometry | `6734eee1` |
| Model | `a6f664c9` |
| Checker | `a9ba5887` |

## Binari e versioni

| Assembly | 30 ottobre (pomeriggio) | 1° novembre |
|---|---|---|
| GPCUtilities | 3.0.0.0 | 3.0.0.0 |
| GPCGeometry | 2.3.0.0 | 2.3.0.0 |
| DelaunayMesh | 2.0.0.14 | 2.0.0.14 |
| GPCModel | 5.31.0.0 | 5.32.0.0 |
| GPCModelData (solo ReBear) | 0.3.0.16 | 0.3.0.17 |
| GPCChecker.Concrete | 0.32.0.2 | 0.32.0.5 |
| GPCChecker.Steel | 2.127.0.1 | 2.127.0.5 |
| GPCChecker.CompositeBridge | 1.21.0.0 | 1.22.0.1 |

Novità:

- CompositeBridge 1.22: `HBridgeSection.Recalculate(earlier, input)` e la sua forma a batch. Se l'ingresso differisce da quello di un'analisi precedente solo in pioli, irrigiditori, armatura trasversale, fatica, stato limite o quota comune non usata da alcuna fase, le situazioni convergenti dell'analisi precedente vengono riprese. Si ricalcolano soltanto punti di tensione, taglio, pioli, torsione e dettagli. Il risultato coincide bit per bit con quello di `Calculate`; ogni altro ingresso, o un'analisi non calcolata dalla libreria, viene calcolato per intero.
- GPCModel 5.32 e Steel 2.127 portano il lavoro pubblicato il 30 ottobre sera da altre sessioni: i contratti delle azioni non sismiche e il controllo dei tubi circolari secondo EN 1993-1-6 eq. (D.40). Le altre DLL sono soltanto ricompilate.

In BridGHe, Bridge Detail Check passa al ricalcolo l'analisi del risultato di Super Element Check che riceve.

Il set intermedio dello stesso giorno, con Checker `2a4e1f86` e CompositeBridge 1.22.0.0, registrava ogni analisi in una tabella con un handle del garbage collector per voce, e così rallentava anche Super Element Check. 1.22.0.1 conserva le situazioni in un campo interno del risultato. Il set intermedio non è mai stato installato nei plugin.

## Controlli

| Controllo | Esito |
|---|---|
| Suite Model | 1.663 superate, zero fallimenti; 12 prove non eseguite (fixture native facoltative) |
| Suite Checker | 10.029 superate, zero fallimenti, inclusi Concrete/Steel net472 (contratto K0) e Design net8.0/net472; CompositeBridge 420, con 9 nuove prove del ricalcolo |
| Compatibilità API | Nessuna rimozione inattesa |
| 99 REV004 nativo | 2.736 righe GPC identiche bit per bit alla baseline (impronta `8E9756B7`), nessun componente con hash diverso; 53 s |
| 50 REV003 nativo | 66.960 righe identiche bit per bit alla baseline (impronta `C1CF95A0`), nessun componente con hash diverso; 254 s |
| Controlli principali BridGHe | 269 superati, regressioni comprese |
| ReBear | `Verify.ps1` 40/40, `Examples.ps1` 20/20; esempi con gli stessi numeri, cambia solo la versione del motore |
| Coerenza fra plugin | Le cinque DLL comuni (Utilities, Geometry, DelaunayMesh, GPCModel, Concrete) hanno lo stesso SHA-256 |

Tempi nel 50, misurati dal profiler di Grasshopper nel run nativo. La baseline è il run del 30 ottobre sera, con il set del pomeriggio e un carico medio della macchina del 25%; il 1° novembre il carico medio era del 55%.

| Componente | 30 ottobre | 1° novembre |
|---|---|---|
| Bridge Detail Check «Sezioni e pioli — tutte le travi» (32.832 sezioni) | 115,2 s | 20,0 s |
| Super Element Check «Verifiche di tutte le travi» (32.832 sezioni) | 98,7 s | 104,2 s |

Per isolare l'effetto su Super Element Check, il banco GPC ricalcola le sue 32.832 sezioni (`--gpc-bench … --stream --parallel --batch`) alternando i due CompositeBridge, con lo stesso GPCModel 5.32:

| Serie (esecuzioni alternate A, B, A, B) | A: 1.21.0.2, set precedente | B |
|---|---|---|
| B = 1.22.0.0, set intermedio | 106,8 e 106,2 s | 114,9 e 113,0 s |
| B = 1.22.0.1, set corrente | 106,8 e 112,4 s | 112,1 e 106,6 s |

Le righe ricalcolate sono identiche in tutte le esecuzioni. Con 1.22.0.1 Super Element Check non cambia, entro il ±3% di rumore delle misure; la differenza del run nativo viene dal carico della macchina.
