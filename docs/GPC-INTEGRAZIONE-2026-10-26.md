# Integrazione dei repository e del set GPC — 26 ottobre 2026

## Revisioni integrate

Model/master è `2acc60ef`: i suoi rami risultano già inclusi, senza modifiche necessarie. Checker/master è `4a1b0090`, con merge `f25f6c69`: include i rami delle verifiche di collegamenti, il fix dell'equilibrio per piccoli carichi del ponte e `lib-1142/checker` / `lib-1142/pali`. Nessun ramo conserva commit esclusi da master. I worktree esistenti non sono stati eliminati.

Il merge ha richiesto la risoluzione dei soli metadati di versione di Concrete e Steel e delle proprietà Version duplicate nei progetti. Le modifiche di calcolo di entrambi i rami sono conservate; gli attesi numerici non sono stati rigenerati durante l'integrazione. Il commit `4a1b0090` assegna le versioni del set coerente.

## Versioni

| Assembly | Versione | Motivo |
|---|---|---|
| GPCUtilities | 3.0.0.0 | Invariato |
| GPCGeometry | 2.3.0.0 | Invariato |
| DelaunayMesh | 2.0.0.14 | Invariato |
| GPCModel | 5.21.0.1 | Invariato |
| GPCModelData | 0.3.0.2 | Invariato |
| GPC.Model.Persistence | 3.3.0.2 | Invariato |
| GPC.Converter | 2.6.0.2 | Invariato |
| GPCChecker.Concrete | 0.25.0.0 | Integra funzionalità dei pali e correzioni del calcolo |
| GPCChecker.Geotechnics | 0.24.0.0 | Integra i pali con testa sotto il piano campagna |
| GPCChecker.Steel | 2.118.0.2 | Revisione coerente dopo il merge |
| GPCChecker.CompositeBridge | 1.19.0.5 | Ricompilazione con le dipendenze integrate |
| GPCModelChecker | 2.29.0.4 | Ricompilazione con le dipendenze integrate |
| GPC.Design | 0.41.0.4 | Ricompilazione con le dipendenze integrate |
| GPCChecker.Masonry | 0.10.0.4 | Ricompilazione con le dipendenze integrate |

I controlli delle API pubbliche passano. Le firme consumate da BridGHe e ReBear non richiedono adattamenti. Le funzionalità aggiunte restano disponibili nelle librerie, senza introdurre porte o componenti ReBear nuovi durante questo aggiornamento.

## Provenienza e collaudo

Il set è prodotto da `Checker/build/Release-Set.ps1 -Candidate` da checkout puliti: Utilities `e57b440b`, Geometry `6734eee1`, Model `2acc60ef`, Checker `4a1b0090`. Il manifest registra il branch d'integrazione al momento del collaudo; lo stesso commit è poi confluito in master.

**Stato: candidato locale.** Checker/master è 64 commit avanti a origin/master; il push attende autorizzazione. Il manifest mantiene `candidate=true` e non viene presentato come rilascio ufficiale. La configurazione **Release** dei plugin descrive l'ottimizzazione di compilazione, non cambia lo stato del set di librerie.

Tutte le suite richieste sono state eseguite: **9.046 test Checker superati** e **1.493 test Model superati**. Nessun fallimento; tre test Model sono `NotExecuted` perché richiedono fixture native facoltative. Concrete e Steel includono net472; Design include net8.0 e net472. La verifica delle versioni rispetto al set precedentemente distribuito da ReBear conferma una versione maggiore per ogni DLL con SHA-256 cambiato.

ReBear passa **25 gruppi di controllo**, compresi caricamento di 61 componenti, contratti delle porte, confronto numerico e hash delle sei DLL, tramite `build/Verify.ps1 -AllowCandidate`. Senza questa opzione il controllo di rilascio rifiuta il candidato, come previsto.

BridGHe passa **378 controlli principali**, incluso il benchmark analitico Straus, e **84 gruppi di regressione isolati**. La prima esecuzione completa ha rilevato una sola discrepanza di palette: `Bridge Moving Combinations` era in `04-Definitions`. È stato spostato in `09-Loads`, senza cambiare porte o GUID, e il gruppo `component-layout` è stato rieseguito con successo. Il resoconto conserva sia il fallimento iniziale sia la ripetizione corretta. La baseline numerica mantiene **238/238 valori esattamente uguali**, con tolleranza relativa/assoluta `1e-12`.

Sono stati caricati 220 componenti e 8 parametri, riaperti **55 GHX** con **8.724 collegamenti**, e ricalcolati senza Straus **23 esempi portabili**. La riapertura finale del REV003 passa con il set integrato. Gli audit confermano 229 GUID distinti e nessuna modifica delle porte esistenti. Compilazione finale: zero errori e zero avvisi MSBuild; il candidato viene segnalato esplicitamente nel log. Esiti e hash delle distribuzioni sono nel [resoconto JSON](validation-gpc-integration-2026-10-26.json).

Il collaudo nativo del [50 REV003](../example/50-ponte-quattro-modelli/README_REV003.md) è stato eseguito prima dell'aggiornamento finale delle DLL. Il suo JSON conserva gli hash effettivamente usati; non è attribuito retroattivamente al set integrato.

## Distribuzione

- BridGHe **0.2.0**: `bin/Release`, sette DLL GPC e `gpc-release-set.json`.
- ReBear **0.2.2**: `bin`, sei DLL GPC e `release-set.json`.
- Set completo delle librerie: `Checker/bin/Release`, 14 DLL con manifest originale e simboli disponibili.
- I cinque assembly comuni sono Utilities, Geometry, DelaunayMesh, Model e Concrete; devono avere SHA-256 identico nei due plugin.

Le cartelle `lib/GPC` contengono copie fissate dal set; le build non dipendono più da una compilazione casualmente presente nei repository fratelli. I manifest completi conservano anche le DLL non usate dal singolo plugin. Aggiornare sempre il set e il relativo manifest, non singole DLL.

La ricognizione dei progetti attivi ha individuato BridGHe e ReBear come plugin che referenziano queste librerie Model/Checker. ANTHEA e GpcModelViewerLab sono applicazioni separate: non sono state modificate nell'aggiornamento dei plugin.

Il percorso normale di compilazione e verifica richiede un set ufficiale. Le opzioni `GpcAllowCandidate=true` di BridGHe e `-AllowCandidate` di ReBear sono esclusivamente per il collaudo dichiarato di candidati, senza alterare il manifest o disattivare gli hash.
