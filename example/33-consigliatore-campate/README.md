# 33 · Consigliatore delle campate

[Apri la definizione GHX](33-consigliatore-campate.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Tracciato di 100 m; regione vietata fra x=40 e 60 m, estesa ±10 m trasversalmente; franco 2 m. Luci ammesse 20–45 m, obiettivo 35 m, griglia 1 m, penalità per pila 0.1.

`Bridge Span Advisor`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Modificare Minimum/Maximum/Target Span e Clearance. Leggere ogni ramo di Support Stations come una soluzione distinta e selezionare un solo ramo per Simple Bridge. Required Supports impone progressive aggiuntive. Aumentare Pier Penalty favorisce meno pile.

## Controlli attesi

Tre alternative ordinate per costo. Nessun appoggio nella fascia 38–62 m. Estremi del tracciato sempre obbligatori. La zona esclude il punto di appoggio di riferimento: aumentare il franco per considerare l'ingombro delle fondazioni.

## Ambito

Ricerca geometrica discreta, non verifica resistente. Le luci limite sono scelte dal progettista. Un risultato vuoto può dipendere dalla griglia; non dimostra l'impossibilità di una soluzione continua.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
