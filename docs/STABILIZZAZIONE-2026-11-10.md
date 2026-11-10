# Stabilizzazione BridGHe — 10 novembre 2026

## Perimetro e riferimento

Interventi sul checkout `master`, base `95ab7c3`, inizialmente pulito. Nessuna integrazione F6 e nessuna modifica ai repository Model e Checker. La prima fase ha mantenuto il set del 2 novembre (Model `15587299`, Checker `742f45f3`). Su successiva richiesta esplicita dell'utente è stato adottato il set ufficiale ReBear dell'8 novembre, poi mantenuto fisso: vedere la sezione finale per il collaudo corrente. I formati degli archivi e i contratti dei componenti restano invariati.

Ambiente osservato: Windows x64, SDK .NET 9.0.320, target .NET 8, Rhino installato 8.36.26296.17001; riferimenti NuGet Rhino/Grasshopper 8.27.26019.16021. Straus API presente in `C:\Program Files\Straus7 R31\Bin64\St7API.dll`. La presenza della DLL non dimostra la disponibilità della licenza.

Log locali in `artifacts/stabilization-20261110/`; `reference.json` registra ambiente, commit e hash dei file GPC. I log sono artefatti ignorati da Git, non evidenze distribuite con il sorgente.

## Correzioni confermate

### Recupero di risultati esterni temporaneamente indisponibili

Prima: `SavedAnalysisResults.Load` eliminava percorso, hash e stato di attesa dopo un errore di lettura. I componenti Straus e Moving sostituivano il modello segnaposto; Moving eliminava anche l'hash dell'input. Un successivo salvataggio perdeva quindi il riferimento, anche per una temporanea assenza del file.

Dopo: il riferimento resta disponibile per salvataggi e nuovi tentativi. Nessun risultato mancante o alterato viene usato. È possibile ripristinare il file o indicare un nuovo `Output Path` e ricalcolare senza avviare Straus. Reset, cambi di input che invalidano l'analisi e nuove analisi conservano la loro semantica precedente.

La regressione `analysis-results-file` copre entrambi i componenti, migrazione dei risultati incorporati, risalvataggio prima della prima soluzione, hash non valido, file mancante, recupero con percorso trasferito e fallback ai risultati incorporati quando la scrittura esterna fallisce.

### Pacchetti completi per i GHX con risultati esterni

Prima: il packaging escludeva `calcoli` integralmente e ometteva i `.bghr` referenziati dai GHX. Inoltre i file nativi dell'esempio 99 sotto `Straus File` sfuggivano al filtro per cartella.

Dopo: `tools/package_results.py` legge i riferimenti dei GHX effettivamente distribuiti, valida confini dei percorsi e SHA-256 e copia soltanto i risultati necessari. Riferimenti mancanti, alterati o incompatibili impediscono la creazione del pacchetto. I file nativi Straus sono esclusi anche se si trovano fuori da `calcoli`.

Il packaging non riscrive i GHX: dopo un trasferimento occorre aggiornare gli input assoluti `Output Path`. Sono distribuiti i cinque `.bghr` referenziati dall'esempio 50 REV003; non si tratta di un pacchetto completo per rieseguire tutti i calcoli nativi storici.

## Campo delle verifiche e sistema di build

L'ipotesi N–Mx–Vy è già presente in `BridgeCheckAdapter.PlanarAssumption`, nelle avvertenze di Super Element Check e nelle ipotesi dei risultati. I report dichiarano gli effetti esclusi; `BridgeReportTests` ne controlla la presenza anche con selezioni differenti dei capitoli. Non è stato cambiato il calcolo resistente né ampliata la sua copertura.

Il progetto di smoke test resta fuori dalla soluzione principale: è una scelta organizzativa compatibile con il runner esistente, non un difetto dimostrato. I test vanno compilati esplicitamente o tramite `tools/build.ps1 -Smoke`. Nessuna modifica superflua a soluzione, target di pubblicazione o architettura.

## Valutazione iniziale Model, Checker e F6 — storico

Valutazione limitata a provenienza e superficie delle modifiche, senza compilare o adottare candidati. Alla lettura del 10 novembre, i checkout fratelli riportano Model `ff318f87` e Checker `9e513417`, con commit di rilascio candidati F7d dell'8 novembre. Questi commit non identificano il set usato da BridGHe.

Il ramo locale `claude/f6-bridghe`, punta `bbf84de` del 9 novembre, diverge da master di 159 commit propri e 3 propri di master. Il confronto con l'antenato comune interessa 426 file e introduce anche adattatori GPC, archivi, file risultati e componenti di analisi: non è un aggiornamento intercambiabile di DLL. I conteggi sono un'istantanea locale, non una misura della qualità delle modifiche.

Decisione iniziale: conservare il set del 2 novembre. Questa decisione è stata successivamente superata dall'autorizzazione ad adottare il set ufficiale ReBear dell'8 novembre. Resta esclusa l'integrazione F6 e resta vietato aggiornare le baseline per nascondere differenze. Il set precedente è conservato negli artefatti locali per consentire un ritorno esplicito.

Gli hash delle DLL comuni in `ConcreteSectionChecker/lib/GPC` coincidono con quelli di BridGHe. I percorsi dichiarati da Arachne per Geometry e Utilities puntano oggi a DLL coincidenti. Una copia transitiva di GPCUtilities nell'output di Geometry è invece diversa: ciò non dimostra che Arachne la carichi, ma conferma che non bisogna dedurre la compatibilità dal solo nome/versione. Nessuna DLL esterna è stata sostituita. Il caricamento reale simultaneo dei tre plugin resta distinto dai test del guard di BridGHe.

## Riordino

I quattro log storici della root (`build-initial.log`, `build-current.log`, `access_54647.log`, `error_54647.log`) sono spostati, senza cancellazione, in `artifacts/stabilization-20261110/legacy-root-logs/`. Non sono stati ripuliti i risultati Straus né le cartelle di backup. Gli esempi possono dipendere dai `.bghr`: non eliminare indiscriminatamente `calcoli`. La documentazione separa ora gli esiti storici dalle verifiche di questa sessione.

## Verifiche precedenti sul set del 2 novembre — storico

| Controllo eseguito | Esito e limite |
| --- | --- |
| Build Release plugin e smoke runner | 0 avvisi, 0 errori. |
| Audit contratti | 152 file di contratto, 229 GUID, nessun errore. |
| Integrità GPC | 7 DLL conformi al manifest; tutti i file registrati nello snapshot iniziale sono invariati. |
| Core della suite `-Smoke` | `PASS TOTAL 269`. |
| Ripetizione core + Straus dopo ripristino licenza | `PASS TOTAL 378`, codice di uscita 0: incluse 21 famiglie native e mensola analitica. |
| Prima esecuzione generale | 15 gruppi isolati superati; esecuzione interrotta intenzionalmente per ricompilare gli ultimi test e concentrare la verifica finale sui percorsi interessati. Non è un successo della suite completa di 87 gruppi. |
| Regressioni mirate della prima correzione | Superati `analysis-results-file`, `bridge-numerical-baseline`, `bridge-report`, `bridge-planar-noise`, `composite-archive`, `archive-before-cleanup`, `bridge-history-workflow`. I due gruppi di caricamento, inizialmente incompleti, sono stati risolti nella successiva indagine descritta sotto. |
| Baseline numerica | 238 valori su 238 esattamente uguali; fixture con tolleranza relativa/assoluta 1e-12. |
| Riapertura GHX | 55 documenti, 8.821 oggetti, 8.698 collegamenti preservati; contratti correnti. |
| Replay portabile | 23 documenti, 586 oggetti, 412 collegamenti; nessun solver invocato. |
| Test Python | 8 superati: 5 sul packaging dei risultati, 3 sul parser dei contratti. |
| Sistema di build | 21 controlli di pulizia e 9 di pubblicazione/MSBuild superati. |

La prima esecuzione delle regressioni generali e mirate totalizza 21 gruppi distinti superati; `analysis-results-file` compare in entrambe. I controlli descritti non costituiscono una validazione strutturale esaustiva.

### Arresti della prima esecuzione (storico)

- **Straus nativo:** il tentativo iniziale `-Native` termina senza `PASS TOTAL`, pur restituendo codice zero. Il runner rifiuta correttamente il falso successo. La prima ripetizione diagnostica `--sections-only` registra `NATIVE: initializing Straus7 API` ma non il completamento di `StrausRuntime.EnsureInitialized`. In quella prima esecuzione nessuna sezione nativa o mensola era stata validata. Successivamente l'utente ha segnalato la licenza mancante e ne ha ripristinato la disponibilità: il nuovo collaudo è descritto sotto.
- **`gpc-release-set` e `plugin-build-check`:** le asserzioni gestite su manifest, DLL caricate, versioni, hash e provenienza passano. Nel secondo test passano anche gli avvisi sui componenti e il controllo della build pubblicata. Entrambi si interrompono dopo l'avvio del processo figlio Rhino, senza marcatore finale e con codice zero. Nei tentativi compaiono anche messaggi fatali WebSocketSharp sulla lettura di frame. Non è dimostrata una causa comune con l'interruzione Straus.
- La prova di esecuzione sequenziale degli host non ha risolto il problema ed è stata rimossa. Restano messaggi diagnostici prima/dopo l'avvio del figlio e per ogni asserzione completata; i figli sono avviati senza finestra console. Non si dichiara risolto il problema di esecuzione dei guard. Per la riproduzione usare `tools/test_regressions.ps1 -Groups gpc-release-set,plugin-build-check` e verificare i marcatori finali, non solo il codice di uscita.

### Ripetizione con licenza Straus disponibile

Eseguito `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --native --core-only`: **378 controlli superati**, marcatore `PASS TOTAL 378` presente e codice di uscita 0. L'API completa l'inizializzazione. Passano le 21 famiglie di sezioni native, i controlli geometrici associati e la mensola con confronti di spostamento `FL³/(3EI)` e momento `FL`; è verificata anche la conservazione dei risultati del modello di input. Questo esito supera il precedente blocco Straus, senza modifiche al codice di calcolo.

Rieseguiti anche `gpc-release-set` e `plugin-build-check`: **entrambi ancora incompleti**, codice 0 ma nessun marcatore finale, dopo `GPC GUARD CHILD START` e `PLUGIN CHECK CHILD START: --plugin-check`. Il ripristino della licenza Straus non ha quindi risolto questi due arresti; la loro causa resta da diagnosticare separatamente.

Evidenze locali in `artifacts/stabilization-20261110/licensed-retry/`: `native.log`, `native-result.json`, `guards.log` e `guards/results.json`. Le 7 DLL GPC sono state nuovamente verificate contro il manifest, senza variazioni. Non sono stati ricompilati i binari né rigenerato il pacchetto ZIP in questa ripetizione: il resoconto aggiornato è quello nel repository.

### Risoluzione degli arresti dei processi figli

L'indagine successiva ha localizzato l'arresto **dopo il completamento dei controlli nel figlio**, durante il percorso di distruzione di `RhinoCore` generato dal precedente `using`. Con Rhino 8.36, in questo scenario di host annidati, la distruzione nativa causava anche la terminazione del padre con codice zero, prima che raccogliesse l'esito. Le tracce per PID distinguono ingresso nel guard, caricamento dal worker, completamento e ritorno al padre. Le prove cambiando host di avvio o tipo di attesa non hanno risolto il problema e non sono state mantenute. Non è stata attribuita la causa a Model, Checker o alla licenza Straus.

La prova determinante ha mantenuto identici i controlli nel figlio e sostituito soltanto l'uscita dopo il test: terminando il processo prima di `RhinoCore.Dispose`, il padre ha raccolto l'esito e concluso tutte le asserzioni. Questo identifica il percorso di shutdown responsabile nell'ambiente osservato; non è una modifica o una diagnosi interna del codice nativo McNeel.

La correzione è confinata all'eseguibile di test:

- `SmokeChildProcess` centralizza avvio, timeout, raccolta di stdout/stderr e verifica del protocollo. Il lancio resta `dotnet <runner.dll>`; le librerie non sono sostituite.
- I soli host figli eseguono il test, intercettano gli errori, scrivono e scaricano i flussi, emettono `RHINO CHILD COMPLETE: <modalità> <codice>` e chiamano `Environment.Exit(codice)` prima del teardown nativo. La fine del processo rilascia le sue risorse. Il padre e gli altri percorsi del runner conservano il normale ciclo di vita di Rhino.
- Un errore gestito conserva codice 1 e diagnostica originale. Un codice zero senza marcatore coerente viene rifiutato. Il limite di attesa resta 300 secondi, seguito dall'arresto dell'albero del solo figlio in timeout.
- La nuova regressione `rhino-child-lifecycle` esercita figli reali: errore intenzionale, uscita zero senza completamento e successivo successo. Controlla inoltre marcatori con modalità o codice incongruenti.
- `BRIDGHE_SMOKE_TRACE_DIRECTORY` abilita facoltativamente tracce separate per PID, utili anche se il processo termina senza restituire stdout al padre. Non serve per eseguire o superare i test.

Per riprodurre il collaudo mirato:

```powershell
dotnet build tests/BridGHe.Smoke -c Release
.\tools\test_regressions.ps1 -Groups gpc-release-set,plugin-build-check,rhino-child-lifecycle
```

**Esito finale: 3 gruppi superati, 0 fallimenti**, con codice di uscita zero e marcatore finale per ciascuno: `gpc-release-set`, `plugin-build-check`, `rhino-child-lifecycle`. Evidenze in `artifacts/guard-investigation/`; `final-build.log` registra la build senza avvisi/errori e `final/results.json` gli esiti dei gruppi. La verifica finale è stata eseguita senza la variabile di tracing. Set GPC nuovamente confrontato con manifest e snapshot iniziale, invariato; nessuna modifica ai repository Model e Checker o al codice del plugin per questa correzione.

Nei log possono ancora comparire messaggi WebSocketSharp di chiusura del runtime Rhino; non sostituiscono gli esiti delle asserzioni e dei marcatori. L'errore intenzionale del test di ciclo di vita è atteso e viene verificato come tale. Il precedente pacchetto ZIP non è stato rigenerato: questa correzione interessa il runner e il resoconto nel repository.

Restavano fuori da quel collaudo mirato la suite generale completa e il caricamento reale simultaneo con gli altri plugin. La successiva esecuzione completa è documentata sotto; il caricamento simultaneo dei plugin resta un controllo distinto.

## Adozione del set ReBear e collaudo completo — esito corrente

L'utente ha autorizzato esplicitamente: «Adotta il set ReBear e poi mantienilo fisso». Sono stati copiati insieme DLL, PDB e manifest ufficiale dell'8 novembre, senza compilare Model o Checker. La sorgente completa è `ANTHEA/supporto/artefatti/librerie-20261108-av`: il suo manifest coincide byte per byte con quello di `ConcreteSectionChecker/lib/GPC`, mentre CompositeBridge non è distribuito da ReBear ed è stato preso dallo stesso set ufficiale.

| Libreria | Versione adottata |
| --- | --- |
| GPCUtilities | 3.0.0.0 |
| GPCGeometry | 2.3.0.0 |
| DelaunayMesh | 2.0.0.14 |
| GPCModel | 5.50.0.2 |
| GPCModelData | 0.3.0.30 |
| GPCChecker.Concrete | 0.37.0.1 |
| GPCChecker.Steel | 2.134.0.0 |
| GPCChecker.CompositeBridge | 1.31.0.1 |

Provenienza: Model `ff318f875b992ec5efdbde8e40479b52d38f6637`, Checker `9e51341775377875f4214ccd4c1f4fe33d47c6a6`, `candidate=false`. SHA-256 del manifest: `D004291C865A8AEC6D8ACBB9D844771C0626F23EF4A9754F361F761F530530CE`. I 17 file fissati (otto DLL, otto PDB e manifest) sono registrati in `artifacts/rebear-set-20261110/frozen-set.json`; la copia precedente è in `previous-GPC`. Le sette DLL comuni coincidono anche con `ConcreteSectionChecker/bin`. Utilities, Geometry e DelaunayMesh non cambiano rispetto al set precedente.

La build e il pacchetto includono ora GPCModelData e System.Text.Json 10.0.13, con System.Text.Encodings.Web e System.IO.Pipelines per .NET 8. Il controllo preliminare verifica otto DLL. Nei test `BridgeHistoryWorkflowTests` e `BridgeMomentStateTests` sono stati aggiunti alias espliciti per i due enum BridGHe divenuti omonimi di nuove API CompositeBridge; non sono cambiati algoritmi, attesi o tolleranze.

### Correzione del replay nativo dell'esempio 19

Il controllo aggiuntivo inizialmente si arrestava senza marcatore finale. Un host diagnostico ha mostrato l'asserzione originale, nascosta durante il teardown Rhino: il test assumeva un solo caso primario e leggeva sempre l'indice 1. I file REV003 presenti, modificati il 5 novembre, contengono invece `1: Dead` e il caso incrementale richiesto all'indice 2.

Il test ora interroga `St7GetResultCaseName` e seleziona l'unico caso con nome corrispondente, sia nella lettura BridGHe sia nel ricampionamento indipendente. Prove dedicate coprono un caso singolo, un caso preceduto da Dead, nomi mancanti e duplicati; gli ultimi due sono rifiutati. L'errore viene stampato prima della distruzione dell'host. Nessun file di calcolo, fixture numerica o codice del solver è stato modificato. Il collaudo è stato ripetuto nel runner ordinario dopo una prima verifica in output isolato.

### Risultati sul set fissato

| Controllo | Esito |
| --- | --- |
| Build Release e audit | 0 avvisi, 0 errori; 152 contratti e 229 GUID coerenti. |
| `tools/build.ps1 -Native` | Completato, codice 0 e marcatore finale presente. |
| Core e Straus nativo | 378 controlli, comprese 21 famiglie di sezioni e mensola analitica. |
| Regressioni isolate complete | **88 gruppi, 0 fallimenti**, inclusi `gpc-release-set`, `plugin-build-check` e `rhino-child-lifecycle`. |
| Baseline numerica | 238 valori entro la tolleranza originaria `1e-12`; 163 identici, 75 diversi entro tolleranza. |
| GHX distribuiti | 55 documenti, 8.821 oggetti e 8.698 collegamenti preservati. |
| Replay portabile | 23 documenti, 586 oggetti, 412 collegamenti; nessun solver invocato. |
| Esempi nativi 01–10 | 10 esempi, 0 fallimenti. |
| Fasi di sezione | 30 confronti superati. |
| Vincoli delle travi | 29 controlli nativi superati. |
| Carichi mobili | 5 scenari e 5.377 controlli superati. |
| Appoggi e collegamenti | Rilettura API Straus di sistemi locali, vincoli, molle e rigid link; nessuna nuova soluzione per questi due test. |
| Ponte a graticcio 11 | 191 controlli, 0 fallimenti. |
| Ponti 12–15 | 208 controlli, 0 fallimenti. |
| Ponti con spina 16–18 | 140 controlli, 0 fallimenti. |
| Stazioni esempio 19 | 12.552 confronti API, 399 risultati di sezione, nessun componente GH in errore; hash dei file nativi invariati. |
| Python | 8 test superati. |
| Pulizia e pubblicazione | 21 + 9 controlli superati. |

La baseline versionata non è stata riscritta. Il confronto aggiuntivo dei valori osservati, salvati in una cartella isolata, misura uno scarto assoluto massimo di `1,1368683772161603e-13` e uno scarto massimo normalizzato con `max(1, abs(atteso))` di `9,412414804942348e-15`. Il massimo assoluto riguarda grandezze con unità diverse e non va interpretato come una singola tolleranza ingegneristica. Non si dichiara identità bit per bit con il set precedente.

Per l'esempio 19 gli scarti massimi sono `5,82e-11 N`, `7,15e-7 Nmm` per i momenti flettenti e `2,84e-13 Nmm` per la torsione; le tolleranze originarie sono rispettivamente `1e-5 N`, `0,01 Nmm` e `0,01 Nmm`. È una rilettura dei risultati esistenti e un ricampionamento API, non una nuova soluzione del ponte 19.

### Evidenze e riproduzione

Evidenze locali in `artifacts/rebear-set-20261110/`: `full-suite.log`, `regression-results.json`, `native-results.json`, `workflow-stations-final.log`, `workflow-stations-validation.json`, `baseline-comparison.json`, log Python e del sistema di build. I nove gruppi nativi estesi sono stati eseguiti in `native-workspace`, conservando i parametri distribuiti e scrivendo lì i nuovi calcoli; non sono stati rigenerati i GHX originali.

```powershell
.\tools\build.ps1 -Native
py -m unittest discover -s tools -p test_*.py
.\tools\test-build-cleanup.ps1
.\tools\test-post-build.ps1
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --workflow-stations-native
```

Le modalità native estese del runner sono `--examples all`, `--bridge-native`, `--beam-support`, `--moving-load-tests all --native`, `--bridge-bearings-native`, `--bridge-connections-native`, `--bridge-grillage`, `--bridge-suite all` e `--bridge-spine all`. Per conservarne gli output separati dagli esempi distribuiti, avviare l'eseguibile con una directory di lavoro isolata contenente i relativi `example/**/parameters.json`, come nello script locale `run-native.ps1`.

Il pacchetto viene rigenerato con `tools/build.ps1 -Package` dopo questo collaudo. Comprende il manifest ufficiale, le otto DLL GPC, le dipendenze JSON, la documentazione aggiornata e i cinque `.bghr` referenziati dai GHX; esclude i file nativi Straus. `package-verification.json` registra percorso, SHA-256 e verifica del contenuto ZIP. La distribuzione viene confrontata con `tested-build.json` per garantire che i binari confezionati siano quelli collaudati.

Il successo di queste suite non è una verifica completa di ogni struttura: resta l'ambito N–Mx–Vy dichiarato dagli adattatori e non è stato eseguito il caricamento reale simultaneo di BridGHe, ReBear e Arachne. Gli esiti del manifest GPC a monte non sono conteggiati come test eseguiti in questa sessione.
