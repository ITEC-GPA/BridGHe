# 45 · Ponte curvo completo con tre fasi

[Apri la definizione GHX](45-ponte-completo-fasi.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Tracciato curvo R=250 m, campate 30+40+30 m, tre travi, conci da 12 m, Warren longitudinali, soletta shell 250 mm, link rigidi e dodici appoggi.

`Simple Bridge → Bracing → Deck Mesh → Assignment + Bearings + Connections → Build Model → Audit + Construction Sequence`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Seguire il flusso da sinistra a destra. Deck Mesh restituisce la geometria aggiornata da assegnare. Build Model raccoglie travi, plate, nodi dei vincoli e link. I tre snapshot attivano acciaio, primo getto e soletta intera; Audit confronta anche le fasi.

## Controlli attesi

Modello geometricamente connesso, nessun errore dell'audit, tre quantità di plate crescenti: zero, primo getto, soletta completa. Nessun risultato è presente prima di un'analisi.

## Ambito

Esempio di assemblaggio privo di carichi, non verifica strutturale. Assegnare azioni incrementali, materiali e sezioni di progetto; eseguire Straus; poi usare Bridge Action, Bridge Shrinkage Action e Build BridGHe Model. Il ritiro e la storia non vengono dedotti dal nome della fase.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
