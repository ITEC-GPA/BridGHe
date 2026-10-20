# 40 · Sequenza costruttiva per gruppi e conci

[Apri la definizione GHX](40-sequenza-costruttiva.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Fasi 10/20/30 ai giorni 0/7/28: concio 1 della trave G2, tutta G2, intera carpenteria.

`Bridge Phase → Bridge Construction Sequence`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Confrontare Physical IDs e Active Groups. La selezione è l'unione dei due criteri; liste entrambe vuote selezionano tutta la struttura. Enable Bearings=false rimuove vincoli e molle nodali. Girder Property cambia esplicitamente le proprietà delle travi principali.

## Controlli attesi

Tre snapshot con numero crescente di elementi, nessun risultato FEM conservato. Number/Name/Day identificano la fase.

## Ambito

Gli snapshot sono analisi indipendenti. Non trasferiscono automaticamente storia, viscosità o deformazioni. Native Stage è disponibile soltanto per attivazioni rappresentabili, con incrementi assegnati e proprietà/appoggi invariati.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
