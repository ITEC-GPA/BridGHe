# Rilascio ufficiale GPC — 1° novembre 2026 (sera)

Dalla sera del 1° novembre BridGHe e ReBear usano un set ufficiale in cui le verifiche di sezione allocano circa il 60% di memoria in meno, con gli stessi risultati bit per bit. Il set è prodotto con `Checker/build/Release-Set.ps1`, senza `-Candidate`, da checkout puliti dei commit pubblicati; le versioni sono controllate rispetto al [set della mattina](GPC-RILASCIO-2026-11-01.md). Il [manifest distribuito](../lib/GPC/release-set.json) contiene `candidate=false` e `onOriginMaster=true` per tutti i repository.

| Repository | Commit |
|---|---|
| Utilities | `e57b440b` |
| Geometry | `6734eee1` |
| Model | `fe353243` |
| Checker | `50979c27` |

## Binari e versioni

| Assembly | 1° novembre (mattina) | 1° novembre (sera) |
|---|---|---|
| GPCUtilities | 3.0.0.0 | 3.0.0.0 |
| GPCGeometry | 2.3.0.0 | 2.3.0.0 |
| DelaunayMesh | 2.0.0.14 | 2.0.0.14 |
| GPCModel | 5.32.0.0 | 5.33.0.0 |
| GPCModelData (solo ReBear) | 0.3.0.17 | 0.3.0.19 |
| GPCChecker.Concrete | 0.32.0.5 | 0.34.0.0 |
| GPCChecker.Steel | 2.127.0.5 | 2.127.0.7 |
| GPCChecker.CompositeBridge | 1.22.0.1 | 1.23.0.0 |

## Novità

Super Element Check dell'esempio 50 verifica 32.832 sezioni. Con il garbage collector di Rhino (workstation) circa il 70% del tempo erano pause del GC: ogni sezione allocava 6,8 MB. Il banco GPC (`--gpc-bench … --alloc-types`) e una misura per regioni hanno mostrato dove andava la memoria. Le correzioni non cambiano nessun numero:

- **Model 5.33** (`84751a87`):
  - **Pareti sottili.** I punti di Gauss delle pareti sottili prendevano det J dell'elemento di linea da una matrice 2×2 e dalla LU di MathNet, per ciascuno dei 32 punti, in un ciclo parallelo. Ora usano direttamente j11, che è lo stesso determinante bit per bit, in sequenza: da 120 a 8 KB per costruzione del risolutore.
  - **Chiave della sovrapposizione.** La chiave della sovrapposizione acciaio–calcestruzzo era un array di dodici valori «boxed», ricostruito tre volte per ogni proprietà omogeneizzata e per ogni piatto; ora è una struct confrontata con lo stesso `Equals`.
  - **Trasformazioni dei punti.** `PositionToGlobal` e `PositionToLocal` eseguono le stesse operazioni di `Point2d` sulle coordinate: prima clonavano il punto e convertivano la traslazione in un nuovo `Vector3d` a ogni punto.
  - **Clipper.** Se i riquadri d'ingombro di acciaio e calcestruzzo sono separati da più di 1e-9 relativo, la sovrapposizione è la regione vuota che Clipper restituirebbe, senza calcolarla. È il caso dei piatti sotto la soletta. I riquadri che si toccano passano ancora da Clipper.
- **CompositeBridge 1.23** (`d45b81e7`):
  - Il baricentro lordo di una fase dipende solo dai dati senza le fasi e dal tipo e dall'omogeneizzazione della fase. Ora si calcola una volta per gruppo del batch, invece che per ogni combinazione e fase, ciascuna con geometria, sezione nativa e proprietà omogeneizzate.
  - La sezione nativa di ogni stato efficace non costruisce più l'H d'acciaio che veniva subito rimossa.
- **Concrete 0.34** (`d45b81e7`):
  - La risultante lineare delle barre è calcolata in sequenza: prima c'era un ciclo parallelo per ogni risultante delle iterazioni di Newton, dentro sezioni già calcolate in parallelo.
  - L'integrazione lineare delle pareti crea le tre funzioni una volta per chiamata.
  - L'impronta di costruzione del risolutore scrive gli stessi byte in uno stream riusato dal thread.

Il set porta anche il lavoro pubblicato da altre sessioni sullo stesso master: la duttilità di curvatura di Concrete 0.33 e la preparazione parallela di GPC.Converter 2.11 con GPCModel 5.32.0.1. Le altre DLL sono soltanto ricompilate.

## Controlli

| Controllo | Esito |
|---|---|
| Suite Model | Core 62 e UnitTest 1.602 superate, zero fallimenti; 12 prove non eseguite (fixture native facoltative) |
| Suite Checker | Tutte superate con zero fallimenti, fra cui Concrete net472 959 (contratto K0), CompositeBridge 420, Steel.Members 6.990, ModelChecker 655, Design net8.0 192 e net472 191 |
| Compatibilità API | Nessuna rimozione inattesa |
| Banco GPC, cattura del 50 | 66.960 righe (Super Element e Detail) identiche bit per bit alla cattura |
| Banco GPC, cattura del 99 | 2.736 righe identiche bit per bit |
| 99 REV004 nativo | 2.736 righe GPC identiche alla baseline (impronta `8E9756B7`); 40,6 s (53 s con il set della mattina) |
| 50 REV003 nativo | 66.960 righe identiche alla baseline (impronta `C1CF95A0`); 191,6 s (254 s) |
| Controlli principali BridGHe | 269 superati, regressioni comprese |
| ReBear | `Verify.ps1` 40/40, `Examples.ps1` 20/20; esempi con gli stessi numeri, cambia solo la versione del motore |
| Coerenza fra plugin | Le cinque DLL comuni (Utilities, Geometry, DelaunayMesh, GPCModel, Concrete) hanno lo stesso SHA-256 |

I run nativi sono stati eseguiti con il set candidato degli stessi commit, che ha gli stessi SHA-256 del set ufficiale perché le build sono deterministiche.

## Tempi

Run nativo del 50, misurato dal profiler di Grasshopper. Il carico medio della macchina era del 55% al mattino e del 29% alla sera:

| Componente | 1° novembre (mattina) | 1° novembre (sera) |
|---|---|---|
| Super Element Check «Verifiche di tutte le travi» (32.832 sezioni) | 104,2 s | 61,3 s |
| Bridge Detail Check «Sezioni e pioli — tutte le travi» | 20,0 s | 18,0 s |

Per escludere l'effetto del carico, il banco GPC ricalcola le 32.832 sezioni di Super Element Check (`--stream --parallel --batch`) alternando i due set. Le statistiche del GC riguardano l'intero processo, compresa la lettura della cattura:

| Esecuzione | Set della mattina | Set della sera |
|---|---|---|
| Prima coppia | 102,3 s (pause GC 77,9 s) | 63,3 s (48,4 s) |
| Seconda coppia | 91,4 s (67,0 s) | 63,3 s (47,8 s) |
| Raccolte (gen0/gen1/gen2) | circa 13.860/4.040/50 | circa 6.215/2.130/33 |
| Memoria allocata | 238 GB | 105 GB |

Le righe ricalcolate sono identiche in tutte le esecuzioni. Con il GC server (`DOTNET_gcServer=1`) lo stesso banco richiede 9,8 s: le pause del GC workstation restano circa il 70% del tempo, perché a ogni raccolta i 24 thread hanno in volo i loro oggetti vivi (vedi [Prove GPC nel dominio](PROVE-GPC-DOMINIO.md#calcolo-parallelo-e-garbage-collector)).
