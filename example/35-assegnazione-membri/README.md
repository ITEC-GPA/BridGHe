# 35 · Assegnazione dei membri del ponte

[Apri la definizione GHX](35-assegnazione-membri.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Ponte curvo R=250 m, tre travi, soletta geometrica 12 m, conci indipendenti dalle campate. Una proprietà H dimostrativa per tutte le famiglie.

`Simple Bridge Solid → Bridge Member Assignment`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Modificare i pannelli del Simple Bridge. Per differenziare travi, traversi e controventi, fornire proprietà distinte e un codice Families per ognuna. -1 è il default generale; le regole Schedule prevalgono sui default.

## Controlli attesi

Unassigned vuoto. ID FEM numerici; Physical IDs del tipo Girder:2:1. Gruppi gerarchici, orientamento della sezione coerente con la normale dell'impalcato.

## Ambito

Le sezioni sono dimostrative e non dimensionate. I giunti dei conci sono partizioni geometriche, non cerniere FEM.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
