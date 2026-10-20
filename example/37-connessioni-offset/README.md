# 37 · Connessioni e offset

[Apri la definizione GHX](37-connessioni-offset.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Due connessioni fra asse trave z=-1500 mm e soletta z=-125 mm, rappresentate nei modi 0 e 1; una trave con offset locale Y=100 mm nel modo 2.

`Bridge Connections`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Confrontare i tre componenti. Mode 0 produce nodi con link rigidi nativi; Mode 1 produce aste elastiche con proprietà esplicita; Mode 2 copia le travi e imposta Offset. Collegare gli elementi scelti a Build Model.

## Controlli attesi

Quattro nodi per due link, due aste deformabili e una trave modificata. Il modo 2 invalida eventuali risultati della copia.

## Ambito

Non sommare i tre modi sulla stessa connessione. I link richiedono nodi terminali univoci del modello. L'export nativo è testabile con --bridge-connections-native.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
