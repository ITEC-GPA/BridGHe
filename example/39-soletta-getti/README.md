# 39 · Soletta strutturale e zone di getto

[Apri la definizione GHX](39-soletta-getti.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Ponte curvo, shell C con E=34000 MPa e spessore 250 mm; superficie media; getti divisi a 30 e 70 m; plate triangolari.

`Bridge Deck Mesh → Bridge Member Assignment`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Usare BG di Deck Mesh per Member Assignment, non la geometria precedente. Collegare Plates a Build Model e Connection Lines a Bridge Connections. Cambiare mesh longitudinale/trasversale e Pour Boundaries.

## Controlli attesi

Tre gruppi Deck/Pour1..3. Ogni linea di connessione parte da un vero nodo della trave e termina su un vertice plate.

## Ambito

Con soletta strutturale assegnare travi di solo acciaio. Le sezioni omogeneizzate includono già la rigidezza del calcestruzzo. Il raffinamento della mesh va verificato con analisi di convergenza.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
