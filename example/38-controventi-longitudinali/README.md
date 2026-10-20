# 38 · Controventi longitudinali

[Apri la definizione GHX](38-controventi-longitudinali.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Due campi longitudinali delimitati da 0, 12.345 e 30 m, due maglie trasversali, diagonali X.

`Bridge Longitudinal Bracing → Bridge Member Assignment`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Cambiare Pattern: 0 X, 1 V, 2 diagonale alternata. Connect X divide gli incroci realmente coincidenti in 3D. Normal Offset non nullo aggiunge montanti fisici; Omitted Panels esclude pannelli numerati.

## Controlli attesi

Otto diagonali X; le travi vengono divise ai punti di attacco. Famiglie 6 per controventi, 7 per montanti di collegamento.

## Ambito

La coincidenza visiva di diagonali sghembe non è una connessione. Se Connect X è attivo e non esiste intersezione 3D, l'input è respinto.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
