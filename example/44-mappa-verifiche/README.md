# 44 · Mappa delle verifiche locali

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](44-mappa-verifiche.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Due fasi locali gPC con azioni assegnate direttamente. Soltanto l'asta FEM 1 è associata ai controlli; il modello globale non è analizzato.

`Bridge Section Check → Bridge Check Map`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Collegare Results, un Beam ID, una Station e un'etichetta Case per ciascun risultato. Attivare Labels per leggere il massimo in viewport. Governing conserva fase, caso, stazione e tutti i massimi a pari merito.

## Controlli attesi

Aste senza rapporti numerici grigie; controlli parziali ambra; rapporti sopra 1 rossi. NaN non è convertito in zero.

## Ambito

Il colore riassume solo i controlli forniti. Non verifica automaticamente ogni sezione, stabilità, fatica o altri stati limite.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
