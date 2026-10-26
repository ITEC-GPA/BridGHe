# Collaudo del flusso modulare — 26 ottobre 2026

Esecuzioni su Rhino 8, .NET 8, Windows x64, Straus7 R3 con licenza disponibile. Dati, tolleranze aggregate, hash dei file e gruppi di regressione sono nel [rapporto JSON](validation-modular-2026-10-26.json).

## Risultati verificati

| Prova | Esito |
|---|---|
| Benchmark nativi 01–10 | **10/10**, 121 confronti analitici; comprendono carico, scarico, attivazione, rimozione e selezione delle fasi |
| Carichi mobili | **5/5 scenari**, 5.377 confronti su forze, reazioni, spostamenti e momenti |
| Accumulo delle fasi di sezione | **30 confronti**, cinque contributi acciaio/misto/ritiro |
| Esempio 50 REV002, input salvati dall'utente | **4 analisi native**, ciascuna con 498 aste e 488 nodi; equilibrio verticale, merge e verifiche completati |
| Verificatore del REV002 | 5 combinazioni, **4.560 risultati di sezione**, 3 rami di dettaglio, 6 grafici; copertura degli input del report controllata |
| Esempio 99 REV004 dopo il fix gPC | **4 analisi native**, 6 combinazioni, **1.224 risultati di sezione**, 6 rami di dettaglio e 10 grafici; equilibrio verticale e input del report verificati |
| Regressioni isolate | **83 gruppi** superati nel percorso standard; comprende sezioni, conci, Excel, fessurazione, interpolazione, ritiro, archivi, dettagli, grafici e report |
| Precisione del solver di sezione | 24 integrazioni indipendenti in BridGHe; 11 casi gPC, ciascuno con due impostazioni di classe e due valori di viscosità |
| Baseline dopo il fix gPC | **238/238 valori esattamente uguali**; tolleranza di confronto relativa/assoluta `1e-12` |
| Suite Checker | **9.040 test superati**, nessun fallimento; Concrete e Steel includono il target net472 |
| Set coerente delle dipendenze | Prodotto con `Checker/build/Release-Set.ps1 -Candidate`; hash delle sette DLL usate da BridGHe verificati contro `bin/Release` |
| Smoke test della Release | **376 controlli**, incluse 21 famiglie di sezioni Straus e mensola con soluzione analitica |
| Migrazione GHX | **60 definizioni** aggiornate, 21 confronti geometrici esatti prima/dopo; nessun vecchio componente monolitico rimasto nei file |
| Catalogo corrente | **54 GHX**, 7.772 oggetti e 7.610 collegamenti riaperti con i contratti correnti |
| Esempi portabili | **23 definizioni** risolte e riaperte senza Straus |
| Release | Compilazione con **0 errori e 0 avvisi**, distribuzione in `bin/Release` |

Per il REV002 la tolleranza dell'equilibrio è `max(0,01 N; |risultante| × 10⁻⁷)`. Il test integra separatamente peso proprio, carichi uniformi, forze nodali e coefficienti delle combinazioni. Per il caso di ritiro controlla la risultante verticale nulla; le tensioni da ritiro sono inoltre coperte dal benchmark analitico di sezione. L'equilibrio da solo non dimostra la correttezza di ogni rigidezza o distribuzione locale.

Il benchmark delle cinque fasi di sezione usa tre casi FEM meccanici e due incrementi locali di ritiro. Il log conserva l'avviso sui metadati di fase legacy: non viene presentato come cinque stadi costruttivi nativi. I cinque benchmark costruttivi nativi sono invece i casi 06–10.

## Correzione emersa dalle analisi

Nel REV002 l'analisi M01 restituiva una combinazione e Dead, mentre alcune azioni richiedevano i singoli casi. Le analisi terminavano, ma Build BridGHe Model non poteva ricostruire tutte le azioni.

**Bridge Analysis Cases** valida i nomi sul modello prima dell'analisi e condivide i riferimenti dei casi con le azioni. La lista Analysis Results viene appiattita quando vi si uniscono sorgenti con percorsi GH diversi: altrimenti il componente poteva iterare e produrre più modelli. Non sono stati cambiati carichi, coefficienti o formule di verifica per correggere questa incongruenza.

Il 99 REV004 ha inoltre riprodotto un difetto di precisione nelle librerie gPC: il solver lineare normalizzava il residuo rispetto alla capacità della sezione e poteva accettare come nullo un risultante di `0,00002 N`. L'audit successivo, normalizzato rispetto alle azioni applicate, lo rifiutava. Non era un errore di matching delle fasi né della soluzione Straus.

La correzione aggiunge un tentativo con tolleranza interna `1e-12` solo dopo un audit fallito. La soglia indipendente di accettazione resta `1e-5`; i risultati già accettati non vengono ricalcolati. Concrete passa a **0.24.0.4**, CompositeBridge a **1.19.0.4**. Le firme esistenti sono conservate; i nuovi overload di `SectionSolver.GetLinearStressAnalysisResult` e `CompositeLinearStressSolver.Solve` accettano una precisione esplicita. Commit Checker `f3f0cb9f` (fix) ed `eea6f307` (versioni), sul branch `codex/bridge-small-load-equilibrium`, senza merge su master né push.

Il set è un **candidato di collaudo**, non un rilascio ufficiale delle librerie. Il manifest locale `artifacts/modular-bridge/gpc-candidate-set/release-set.json` registra commit, versioni, SHA-256 ed esiti. Le suite Model aggiungono 1.493 test superati; tre test nativi opzionali di quel repository restano `NotExecuted` perché non sono state preparate le rispettive fixture (`GPC_STRAUS7_BENCHMARK`, `GPC_STRAUS7_ASSIGNMENTS`, `GPC_STRAUS7_PACKED_PAIRS`). Non sono conteggiati come superati. I benchmark nativi di BridGHe elencati nella tabella sono stati invece effettivamente eseguiti.

## Conservazione dei dati

La migrazione è partita dai file realmente salvati, inclusi REV001/REV002 del 50, la REV003 personale del 99 e la copia del 49. La geometria di riferimento è stata confrontata su aste, nodi, appoggi, giunti, conci e progressive. I due Excel del 50 mantengono esattamente gli hash dei file dell'utente.

I GHX sono distribuiti senza inserire i risultati FEM generati dal collaudo. I file Straus e i log di prova sono sotto `artifacts/modular-bridge` e nelle cartelle di calcolo dei benchmark. Il backup iniziale locale è `artifacts/modular-bridge/before-20261026-192614.zip`.

## Limiti del collaudo

- Il tentativo facoltativo di catturare il viewport 3D con `DisplayPipeline.DrawToBitmap` genera `SEHException` nell'host Rhino nascosto. I test standard delle impostazioni di preview, delle geometrie, degli ingombri e delle armature passano. La cattura OpenGL non è dichiarata validata. Le immagini del canvas GH sono state acquisite separatamente.
- Un'esecuzione cumulativa nell'unico host Rhino si interrompeva prima del riepilogo, anche con codice di uscita zero. `tools/test_regressions.ps1` ora isola ogni gruppo e richiede il marcatore finale di completamento; `tools/build.ps1 -Smoke` usa questo percorso.
- Non è stato svolto uno studio di convergenza della mesh del ponte personale. La conservazione dei conci al variare della mesh è verificata, ma è un controllo diverso dalla convergenza della risposta FEM.
- Il report del ponte è stato controllato negli ingressi; la generazione Word è coperta dai test dedicati. Non viene consegnato un report del ponte come relazione strutturale certificata.
- Restano le ipotesi concordate: verifica longitudinale **N–Mx–Vy**, esclusione di torsione/M2/V1, ritiro non fessurato nel REV002. L'esempio non sostituisce la verifica globale del progetto.

## Ripetere le prove

```powershell
dotnet build tests/BridGHe.Smoke -c Release
./tools/test_regressions.ps1 -Configuration Release
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --core-only --native
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --examples
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --moving-load-tests --native
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --bridge-native
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --migrate-bridge-examples
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --modular-bridge-native
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --modular-bridge-native '99-Ponte completo GPC/REV004/flusso-acciaio-misto_REV004.ghx'
./tools/test_regressions.ps1 -Groups bridge-linear-small-loads,bridge-numerical-baseline
```

La migrazione senza `--publish` prepara copie di collaudo senza sostituire gli originali. Il test nativo usa tali copie e non salva risultati nei GHX. Per il flusso operativo vedere [Flusso modulare](FLUSSO-MODULARE.md) e la [guida Word](BridGHe_Guida_operativa.docx), capitoli 32–34.
