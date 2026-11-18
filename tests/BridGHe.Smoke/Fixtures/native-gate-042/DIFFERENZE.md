# Riferimenti nuovi del gate nativo, esempi 50 REV004 e 99 REV005 (0.4.2, parte 1) — PROPOSTI, da registrare solo dopo l'OK

Prima = riferimenti della 0.4.1 (50 REV003 e 99 REV004, `P2B\results\t179ab-native-50` e `-99`); dopo = nativi della 0.4.2 sul commit `972cb722` (set AX ufficiale, senza flag da candidato). I valori attesi sono in `references.json`; le righe complete (`.rows.tsv`, `.rows.jsonl.gz`, `.summary.json`) sono fuori dal repository, nella cartella delle evidenze `INT\rapporti\bridghe-042-parte1-20261117\catture-proposta\t042-native-50` e `t042-native-99`.

Il codice di calcolo non cambia: lo stesso nativo sul 50 REV003 dà le 66.960 righe della 0.4.1, uguali una per una (impronta `F9426C0A…`). Le differenze qui sotto vengono solo dalle definizioni nuove (carichi delle fasi dello script di riferimento).

### Esempio 50: `example50-rev003-native` → `example50-rev004-native`

| Controllo | Prima | Dopo |
|---|---:|---:|
| Righe di verifica nell'impronta | 66.960 | 68.184 |
| Impronta delle righe (SHA-256) | `F9426C0A9261A234…` | `C94CEC82F0CBEACB…` |
| Errori di serializzazione | 0 | 0 |
| Righe con la stessa chiave: uguali / diverse | 1.204 / 65.396 | |
| Righe solo prima / solo dopo | 360 | 1.584 |
| Utilizzo massimo delle tensioni | 0.365611 | 0.583235 |
| Rapporto massimo delle verifiche locali | 1.50371 | 3.03381 |
| Punti di tensione oltre 1 | 0 | 0 |
| Verifiche locali oltre 1 | 192 | 321 |
| P2K · Analyses | 5 | 5 |
| P2K · PhaseCount | 4 | 4 |
| P2K · Beams | 498 | 498 |
| P2K · Nodes | 488 | 488 |
| P2K · MovingPositions | 31 | 31 |
| P2K · SlabForceAssignments | 120 | 240 |
| P2K · Combinations | 36 | 36 |
| P2K · SectionChecks | 32832 | 33444 |
| P2K · DetailBranches | 3 | 3 |
| P2K · Plots | 6 | 6 |
| P2K · ReportInputs | True | True |
| P2K · CachedRunFalse | True | True |
| P2K · ChangedPathInvalidatesResults | True | True |
| P2K · ResultsSavedInGHX | False | False |
| P2K · misure di equilibrio e posizioni | 198 | 204 |
| Report Word: allegati | — | 36 |
| Report Word: risultati | — | 34740 |

Massimo utilizzo prima: section · BridgeDetailCheckComponent · Sezioni e pioli — tutte le travi · {149;4} · 14 — G3 / Beam 150 / s=1000 mm / DEMO_CUMULATIVA + Q_MOV_0010; dopo: section · BridgeDetailCheckComponent · Sezioni e pioli — tutte le travi · {149;4} · 14 — G3 / Beam 150 / s=1000 mm / DEMO_CUMULATIVA + Q_MOV_0010.

Massimo rapporto prima: section · BridgeDetailCheckComponent · Verifica irrigiditori agli appoggi · {90;0} · 20 — G2 / Beam 91 / s=0 mm / DEMO_CUMULATIVA + Q_MOV_0016 — Appoggio · saldature alle flange; dopo: section · BridgeDetailCheckComponent · Verifica irrigiditori agli appoggi · {150;0} · 21 — G3 / Beam 151 / s=0 mm / DEMO_CUMULATIVA + Q_MOV_0017 — Appoggio · saldature alle flange.

| Componente | Righe | Utilizzo max | Rapporto max | Utilizzo > 1 | Rapporto > 1 |
|---|---|---|---|---|---|
| BridgeDetailCheckComponent — Sezioni e pioli — tutte le travi | 32832 → 33444 | 0.365611 → 0.583235 | 0.942857 → 0.942857 | 0 → 0 | 0 → 0 |
| BridgeDetailCheckComponent — Verifica irrigiditori agli appoggi | 432 → 432 | 0.365611 → 0.583235 | 1.50371 → 3.03381 | 0 → 0 | 192 → 194 |
| BridgeDetailCheckComponent — Verifica irrigiditori ai giunti | 864 → 864 | 0.175204 → 0.30393 | 0.958583 → 0.958583 | 0 → 0 | 0 → 0 |
| SuperElementCheckComponent — Verifiche di tutte le travi — Run dopo Build | 32832 → 33444 | 0.362593 → 0.555462 | 0.36524 → 0.596759 | 0 → 0 | 0 → 0 |

| Verifica locale · esito | Prima | Dopo |
|---|---:|---:|
| Anima · resistenza a taglio — Da verificare **(cambia)** | 32.832 | 33.444 |
| Anima · resistenza a taglio — Entro limite **(cambia)** | 34.128 | 34.740 |
| Anima · tensione equivalente elastica (inviluppo) — Entro limite **(cambia)** | 66.960 | 68.184 |
| Appoggio · contatto piatti/flangia — Entro limite **(cambia)** | 432 | 334 |
| Appoggio · contatto piatti/flangia — Non verificato **(cambia)** | 0 | 98 |
| Appoggio · freccia fuori piano — Entro limite | 432 | 432 |
| Appoggio · gola minima — Entro limite | 432 | 432 |
| Appoggio · impronta entro l’estremità — Entro limite | 216 | 216 |
| Appoggio · impronta longitudinale — Entro limite | 432 | 432 |
| Appoggio · impronta trasversale sotto i piatti — Entro limite | 432 | 432 |
| Appoggio · ingombro tra piattabande — Entro limite | 432 | 432 |
| Appoggio · lunghezza efficace cordone — Entro limite | 432 | 432 |
| Appoggio · piatti entro classe 3 — Entro limite | 432 | 432 |
| Appoggio · rigidezza torsionale — Entro limite | 432 | 432 |
| Appoggio · saldature alle flange — Entro limite **(cambia)** | 240 | 238 |
| Appoggio · saldature alle flange — Non verificato **(cambia)** | 192 | 194 |
| Appoggio · saldature verticali all’anima — Entro limite | 432 | 432 |
| Appoggio · stabilità e pressoflessione II ordine — Entro limite **(cambia)** | 432 | 403 |
| Appoggio · stabilità e pressoflessione II ordine — Non verificato **(cambia)** | 0 | 29 |
| Interazione N–M–V · inviluppo elastico — Entro limite **(cambia)** | 0 | 864 |
| Intermedio · freccia fuori piano — Entro limite | 864 | 864 |
| Intermedio · gola minima — Entro limite | 864 | 864 |
| Intermedio · ingombro tra piattabande — Entro limite | 864 | 864 |
| Intermedio · lunghezza efficace cordone — Entro limite | 864 | 864 |
| Intermedio · piatti entro classe 3 — Entro limite | 864 | 864 |
| Intermedio · rigidezza pannelli — Entro limite | 864 | 864 |
| Intermedio · rigidezza torsionale — Entro limite | 864 | 864 |
| Intermedio · saldature alle flange — Entro limite | 864 | 864 |
| Intermedio · saldature verticali all’anima — Entro limite | 864 | 864 |
| Intermedio · stabilità e pressoflessione II ordine — Entro limite | 864 | 864 |
| Pioli · bordo libero piattabanda — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · copriferro superiore — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · diametro testa — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · diametro/spessore piattabanda — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · passo longitudinale massimo — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · passo longitudinale minimo — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · passo trasversale minimo — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · resistenza a fatica — Da verificare **(cambia)** | 34.128 | 34.740 |
| Pioli · spessore testa — Entro limite **(cambia)** | 34.128 | 34.740 |
| Pioli · testa sopra armatura inferiore — Entro limite **(cambia)** | 34.128 | 34.740 |
| Piolo · taglio longitudinale — Entro limite **(cambia)** | 34.128 | 34.740 |
| Soletta · taglio longitudinale e splitting — Da verificare **(cambia)** | 34.128 | 34.740 |


### Esempio 99: `modular-flusso-acciaio-misto_REV004` → `modular-flusso-acciaio-misto_REV005`

| Controllo | Prima | Dopo |
|---|---:|---:|
| Righe di verifica nell'impronta | 2.832 | 15.004 |
| Impronta delle righe (SHA-256) | `64672EA70025F061…` | `9DE143576C252CF9…` |
| Errori di serializzazione | 0 | 0 |
| Righe con la stessa chiave: uguali / diverse | 0 / 2.754 | |
| Righe solo prima / solo dopo | 78 | 12.250 |
| Utilizzo massimo delle tensioni | 0.142824 | 0.171396 |
| Rapporto massimo delle verifiche locali | 0.958583 | 0.9976 |
| Punti di tensione oltre 1 | 0 | 0 |
| Verifiche locali oltre 1 | 0 | 0 |
| P2K · Phases | 4 | 5 |
| P2K · Combinations | 6 | 31 |
| P2K · SectionChecks | 1272 | 6758 |
| P2K · Details | 6 | 6 |
| P2K · Plots | 10 | 10 |
| P2K · posizioni mobili (fase 45) | — | 25 |
| P2K · reazione massima = peso del veicolo [N] | — | 600000 |

Massimo utilizzo prima: section · BridgeDetailCheckComponent · Pioli G2 · {11;3} · 5 — G2 / Beam 36 / s=1000 mm / DEMO_AMPLIFICATA; dopo: section · BridgeDetailCheckComponent · Pioli G2 · {12;0} · 5 — G2 / Beam 37 / s=0 mm / DEMO_AMPLIFICATA.

Massimo rapporto prima: section · BridgeDetailCheckComponent · Verifica intermedi · {1;3} · 0 — G1 / Beam 2 / s=1000 mm / DEMO_GETTO — Intermedio · rigidezza torsionale; dopo: section · BridgeDetailCheckComponent · Verifiche appoggi G2 · {12;0} · 5 — G2 / Beam 37 / s=0 mm / DEMO_AMPLIFICATA — Appoggio · saldature alle flange.

| Componente | Righe | Utilizzo max | Rapporto max | Utilizzo > 1 | Rapporto > 1 |
|---|---|---|---|---|---|
| BridgeDetailCheckComponent — Pioli G2 | 630 → 3441 | 0.142824 → 0.171396 | 0.942857 → 0.942857 | 0 → 0 | 0 → 0 |
| BridgeDetailCheckComponent — Pioli sulle azioni di fase | 642 → 3317 | 0.142824 → 0.163077 | 0.942857 → 0.942857 | 0 → 0 | 0 → 0 |
| BridgeDetailCheckComponent — Verifica agli appoggi | 24 → 124 | 0.142824 → 0.163077 | 0.942857 → 0.968525 | 0 → 0 | 0 → 0 |
| BridgeDetailCheckComponent — Verifica intermedi | 120 → 620 | 0.113859 → 0.133469 | 0.958583 → 0.958583 | 0 → 0 | 0 → 0 |
| BridgeDetailCheckComponent — Verifiche appoggi G2 | 24 → 124 | 0.142824 → 0.171396 | 0.942857 → 0.9976 | 0 → 0 | 0 → 0 |
| BridgeDetailCheckComponent — Verifiche intermedi G2 | 120 → 620 | 0.113859 → 0.135488 | 0.958583 → 0.958583 | 0 → 0 | 0 → 0 |
| SuperElementCheckComponent — Super Element Check | 642 → 3317 | 0.118524 → 0.139661 | 0.157683 → 0.204127 | 0 → 0 | 0 → 0 |
| SuperElementCheckComponent — Verifiche G2 | 630 → 3441 | 0.136023 → 0.163234 | 0.170814 → 0.228532 | 0 → 0 | 0 → 0 |

| Verifica locale · esito | Prima | Dopo |
|---|---:|---:|
| Anima · resistenza a taglio — Da verificare **(cambia)** | 1.272 | 6.758 |
| Anima · resistenza a taglio — Entro limite **(cambia)** | 1.560 | 8.246 |
| Anima · tensione equivalente elastica (inviluppo) — Entro limite **(cambia)** | 2.832 | 15.004 |
| Appoggio · contatto piatti/flangia — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · freccia fuori piano — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · gola minima — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · impronta entro l’estremità — Entro limite **(cambia)** | 24 | 124 |
| Appoggio · impronta longitudinale — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · impronta trasversale sotto i piatti — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · ingombro tra piattabande — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · lunghezza efficace cordone — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · piatti entro classe 3 — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · rigidezza torsionale — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · saldature alle flange — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · saldature verticali all’anima — Entro limite **(cambia)** | 48 | 248 |
| Appoggio · stabilità e pressoflessione II ordine — Entro limite **(cambia)** | 48 | 248 |
| Intermedio · freccia fuori piano — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · gola minima — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · ingombro tra piattabande — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · lunghezza efficace cordone — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · piatti entro classe 3 — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · rigidezza pannelli — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · rigidezza torsionale — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · saldature alle flange — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · saldature verticali all’anima — Entro limite **(cambia)** | 240 | 1.240 |
| Intermedio · stabilità e pressoflessione II ordine — Entro limite **(cambia)** | 240 | 1.240 |
| Pioli · bordo libero piattabanda — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · copriferro superiore — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · diametro testa — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · diametro/spessore piattabanda — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · passo longitudinale massimo — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · passo longitudinale minimo — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · passo trasversale minimo — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · resistenza a fatica — Da verificare **(cambia)** | 1.560 | 8.246 |
| Pioli · spessore testa — Entro limite **(cambia)** | 1.560 | 8.246 |
| Pioli · testa sopra armatura inferiore — Entro limite **(cambia)** | 1.560 | 8.246 |
| Piolo · taglio longitudinale — Entro limite **(cambia)** | 1.560 | 8.246 |
| Soletta · taglio longitudinale e splitting — Da verificare **(cambia)** | 1.560 | 8.246 |


## Catture non native che cambiano con gli esempi nuovi

Le catture stanno fuori dal repository; le copie proposte sono nella cartella delle evidenze (`catture-proposta`). Le voci già catturate restano uguali in tutte: cambiano solo voci nuove dei due GHX nuovi.

| Cattura | Prima | Dopo | Differenze |
|---|---:|---:|---|
| replay `gpc-only-elements` | 218 chiavi | 253 | 35 nuove, 0 diverse, 0 mancanti |
| replay `gpc-only-generators` | 76 | 89 | 13 nuove |
| replay `gpc-only-properties` | 634 | 773 | 139 nuove; restano le 2 differenze della 0.4.1 approvate il 16/11 e non registrate |
| replay `gpc-only-models` | 614 | 666 | 52 nuove |
| R6.0 `scan` | 24.120 voci | 27.584 | 3.464 nuove (2.572 di 50 REV004, 892 di 99 REV005) |
| R6.0 `payloads`, `corpus` | 4.475, 70.403 | uguali | nessuna |

Riscontro sulle copie proposte (stesse corse del gate, cattura proposta in `compare:`): scan 27.584 voci, 0 nuove, 0 mancanti; properties 773/773 viste, solo le 2 differenze della 0.4.1; models, elements e generators nel rapporto.
