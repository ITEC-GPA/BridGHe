# 43 · Estrazione e conteggio delle storie per fatica

**Aggiornamento 14 novembre 2026 (BridGHe 0.4.0).** Questa copia è stata risalvata con i dati GPC della 0.4.0: nel file non ci sono più dati FEM (né il blocco `FemmR6` né modelli FeMM). Componenti, GUID, collegamenti, valori di input, fasi, unità e selezioni sono quelli di prima: cambia solo il modo in cui i dati sono salvati, e quindi la dimensione del file (da 266.946 a 123.167 byte). Riaperto e ricalcolato senza Straus, il file dà gli stessi risultati della copia precedente, valore per valore (18 uscite e messaggi dei componenti, tutti uguali). La copia precedente resta nella cronologia del repository (commit `9350aeb`).

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](43-storie-fatica.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Storia 0,10,2,8,0: a sinistra input diretto MPa; a destra valori assiali N da un modello con campioni FEM SINTETICI.

`Bridge Fatigue History`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Confrontare Ranges, Means, Cycle Counts ed Equivalent Range. Con Model inserire l'ordine reale dei casi/posizioni di transito. Per una tensione fornire sei Response Factors coerenti con sezione, fase e unità.

## Controlli attesi

Un ciclo di range 10 e uno di range 6, includendo le mezze escursioni residue. Con m=3 e N=1 il range equivalente vale radice cubica di 1216.

## Ambito

Nessun ordinamento alfabetico dei casi, nessuna categoria di dettaglio/S-N/danno implicita. L'interpolazione FEM rifiuta discontinuità note e campioni ambigui.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
