# 35 · Assegnazione dei membri del ponte

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](35-assegnazione-membri.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Ponte curvo R=250 m, tre travi, soletta geometrica 12 m, conci indipendenti dalle campate. Una proprietà H dimostrativa per tutte le famiglie.

`Bridge Grillage Mesh con diaframmi pieni → Bridge FEM Members`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Modificare i pannelli delle singole definizioni geometriche. Per differenziare travi, traversi e controventi, usare un Bridge Family Property per ogni proprietà e selezionare la famiglia dalla Value List. Unire le uscite in Family Properties di Bridge FEM Members. All steel families esclude la soletta; le Section Rules prevalgono sui default.

## Controlli attesi

Unassigned vuoto. ID FEM numerici; Physical IDs del tipo Girder:2:1. Gruppi gerarchici, orientamento della sezione coerente con la normale dell'impalcato.

## Ambito

Le sezioni sono dimostrative e non dimensionate. I giunti dei conci sono partizioni geometriche, non cerniere FEM.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
