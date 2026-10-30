# Collaudo di un set GPC sugli esempi 50 e 99

Procedura per confrontare due set di librerie GPC (per esempio il set ufficiale e un candidato) sugli esempi più significativi: **50 REV003** (cinque analisi Straus, 31 posizioni mobili, 32.832 risultati di sezione) e **99 REV004** (quattro analisi, due travi, dettagli e grafici). Il confronto riguarda esiti, tempi e, riga per riga, tutti i risultati di sezione GPC.

## Esecuzione

```powershell
# 1. Set corrente in lib/GPC; per un candidato aggiungere -p:GpcAllowCandidate=true
dotnet build tests/BridGHe.Smoke/BridGHe.Smoke.csproj -c Release
# 2. Esecuzioni temporizzate (licenza Straus per i run nativi)
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tools/prove-gpc-dominio.ps1 -Phase baseline -Runs '50-check','50-regression','99-replay','99-native','50-native'"
# 3. Sostituire insieme DLL, PDB e release-set.json in lib/GPC, ricompilare e ripetere con -Phase candidato
# 4. Confronto
powershell -NoProfile -ExecutionPolicy Bypass -File ./tools/compare-gpc-fingerprints.ps1 -Baseline baseline -Candidate candidato
```

Con Windows PowerShell 5.1 un elenco di `-Runs` richiede `-Command`: con `-File` arriva un solo valore.

Con un set che contiene la cache dei domini di rottura (`FailureDomainCache` in GPCChecker.Concrete), il riepilogo dell'impronta ne riporta i contatori. Per escluderla nello stesso processo impostare `$env:BRIDGHE_GPC_DOMAIN_CACHE_CAPACITY = '0'` prima di lanciare lo script; i processi figli ereditano la variabile.

| Run | Comando dell'harness | Note |
|---|---|---|
| `50-check` | `--example50-rev003 --check` | Controllo offline del GHX |
| `50-regression` | `--regression bridge-moving-workflow` | Combinazioni mobili, senza solver |
| `50-native` | `--example50-rev003 --native` | Cinque analisi Straus, Build, verifiche e dettagli |
| `99-replay` | `--example99 replay004` | Riapertura dei risultati salvati, senza Straus |
| `99-native` | `--modular-bridge-native "99-Ponte completo GPC/REV004/flusso-acciaio-misto_REV004.ghx"` | Quattro analisi sul GHX distribuito, che non viene modificato |
| `99-rev004` | `--example99 rev004` | Rigenera la REV004 dal REV001; la cartella viene poi ripristinata |

Lo script esegue `dotnet <BridGHe.Smoke.dll>` dalla radice del progetto e scrive in `artifacts/prove-gpc-dominio/<fase>/<run>`:

- `output.log` con i secondi trascorsi su ogni riga, `stderr.log` e `run.json` (exit code, durata, tempo CPU del processo, carico della macchina, righe PASS/FAIL, valori delle validazioni);
- `fingerprint/`: impronta numerica dei risultati GPC;
- `outputs/` e `hashes.json`: cartelle generate dall'harness, spostate qui, e SHA-256 di ogni file prodotto;
- `gpc-set.json`, `summary.json` e `hashes.sha256` a livello di fase.

I file versionati che l'harness riscrive (`REV003-native-validation.json`, cartella REV004) vengono copiati negli output e poi ripristinati byte per byte. Nessun file viene eliminato.

## Impronta numerica

Con `BRIDGHE_RESULT_DUMP=<cartella>` i runner nativi chiamano `ResultFingerprint` (`tests/BridGHe.Smoke/ResultFingerprint.cs`) dopo le verifiche. Senza la variabile il comportamento dei test non cambia.

Per ogni `BridgeStressResult` di **Super Element Check** e **Bridge Detail Check** l'impronta conserva provenienza, punti 1–8, τ, lo stadio GPC completo (contributi, punti, taglio, pioli, torsione e relative verifiche) e lo SHA-256 dell'ingresso GPC (`Input`, geometria, materiali). Ogni riga ha il proprio SHA-256; il file `*.rows.jsonl.gz` contiene il dettaglio completo. Il riepilogo `*.summary.json` riporta esiti delle verifiche locali, utilizzi massimi, tempi del profiler GH per componente, marcatori `TIMING` e assembly caricati con percorso e SHA-256.

Il confronto separa quindi le differenze di ingresso (FEM e combinazioni) da quelle prodotte da GPC. I GUID sono esclusi dall'impronta: i materiali GPC ricevono `Guid.NewGuid()` alla costruzione e cambierebbero l'hash dell'ingresso a ogni esecuzione.

Due esecuzioni identiche producono la stessa impronta riga per riga e gli stessi `validation.json`/`progress.json`. Cambiano sempre i file Straus `.st7`, `.LSA` e `.LSL`, che registrano ora, tempo CPU e spazio libero della soluzione: il confronto li segnala a parte. Anche l'Excel rigenerato da `99-rev004` cambia hash pur con le stesse voci: differiscono solo le date del contenitore zip.

I tempi dipendono molto dal carico della macchina: con il set ufficiale, due esecuzioni di `50-native` hanno richiesto 2.338 s e 3.047 s. Per confrontare i tempi conviene ripetere ogni fase almeno due volte.

## Banco delle sezioni GPC

Il banco ripete i calcoli di sezione GPC (`HBridgeSection.Calculate`) di un run nativo senza Straus e senza Grasshopper. Serve per cronometrare un set e confrontarlo bit per bit in pochi minuti.

1. Cattura: con `BRIDGHE_GPC_CAPTURE=<cartella>` i runner nativi scrivono `<etichetta>.inputs.jsonl.gz` subito dopo l'impronta. Per ogni risultato di sezione di **Super Element Check** e **Bridge Detail Check** la riga contiene la chiave dell'impronta, lo stadio, la provenienza, le ipotesi, l'ingresso GPC e gli hash dell'ingresso e della riga. Lo script `prove-gpc-dominio.ps1` passa la variabile ai processi figli.
2. Ripetizione:

```powershell
dotnet obj/bin/BridGHe.Smoke/Release/net8.0-windows/BridGHe.Smoke.dll --gpc-bench <cattura> --rhino [--every N | --station-every N] [--offset K] [--component testo] [--batch] [--repeat R] [--out righe.tsv] [--dump cartella]
dotnet obj/bin/BridGHe.Smoke/Release/net8.0-windows/BridGHe.Smoke.dll --gpc-bench <cattura> --rhino --stream [--parallel] [--batch]
```

La prima forma ricalcola una riga ogni N (tutte per default), oppure con `--station-every N` tutte le righe di una stazione ogni N, R volte. Con `--batch` le righe consecutive della stessa stazione (componente, trave e stazione) sono calcolate in un'unica chiamata `HBridgeSection.Calculate(inputs)`, come fanno i componenti. Stampa:

- tempo reale e CPU;
- millisecondi per sezione (mediana, p90, massimo);
- memoria allocata e raccolte del GC;
- il confronto dell'hash di ogni riga con quello catturato.

`--stream` fa una sola passata su tutta la cattura, a blocchi e con memoria limitata; `--parallel` distribuisce ogni blocco sui processori. L'uscita vale 0 solo se tutte le righe sono identiche. `--dump` scrive il contenuto ricalcolato delle prime differenze, da confrontare con il file `*.rows.jsonl.gz` dell'impronta.

I materiali Model vengono ricostruiti dai loro costruttori, un solo oggetto per descrittore come nei run nativi, dove le sezioni di una trave condividono i materiali: il batch raggruppa infatti le combinazioni che hanno gli stessi oggetti materiale. L'hash dell'ingresso, uguale a quello dell'impronta, dimostra che la ricostruzione è fedele.

`--rhino` avvia Rhino come i runner nativi ed è necessario per il confronto bit per bit. Senza Rhino circa il 40 % delle righe del 99 differisce all'ultima cifra (1e-15 relativo), perché il processo con Rhino avviato usa un ambiente numerico diverso. Le opzioni del JIT (`DOTNET_TieredCompilation`, `TieredPGO`, `EnableAVX2`, `EnableHWIntrinsic`) non c'entrano.

Il banco usa le DLL GPC della propria cartella. Per provare un set senza ricompilare BridGHe basta copiare nella cartella di uscita le sette DLL con i PDB.

## Limiti noti al 29 ottobre 2026

Indipendenti dal set GPC, osservati sul master `509fddf` con il set ufficiale del 27 ottobre:

- `50-check` fallisce se il progetto non si trova in `Repository\BridGHe`. Il GHX REV003 salva la tabella Excel con il percorso assoluto (`ExcelPath`), mentre l'ingresso `File` è relativo. In un'altra cartella il percorso risolto differisce, quindi la cache non viene usata e `Validate` non preme Run. `50-native` preme Run e non è influenzato.
- `99-replay` fallisce con `no FEM results for 'G1_GETTO'`. Le cache FEM della REV004 sono state invalidate il 25 ottobre (`alignment-validation.json`, stato `pending-native`). Per la REV004 usare `99-native`.
- `99-rev004` fallisce dopo le quattro analisi con `Incorrect phase activation of transverse slab.` Il generatore parte dal REV001 e controlla ancora la geometria precedente (37/76 aste), mentre la REV004 corrente ha 73/112 aste. La cartella viene comunque ripristinata.
- `Win32_Processor.LoadPercentage` restituisce 0 su questa macchina. Lo script usa il contatore `PercentProcessorTime` di `_Total`.
