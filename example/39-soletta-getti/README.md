# 39 · Soletta strutturale e zone di getto

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](39-soletta-getti.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Ponte curvo, shell C con E=34000 MPa e spessore 250 mm; superficie media; getti divisi a 30 e 70 m; plate triangolari.

`Bridge Deck Mesh → Bridge FEM Members`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Usare BG di Deck Mesh per Member Assignment, non la geometria precedente. Collegare Plates a Build Model e Connection Lines a Bridge Rigid Links oppure Bridge Connection Beams. Cambiare mesh longitudinale/trasversale e Pour Boundaries.

## Controlli attesi

Tre gruppi Deck/Pour1..3. Ogni linea di connessione parte da un vero nodo della trave e termina su un vertice plate.

## Ambito

Con soletta strutturale assegnare travi di solo acciaio. Le sezioni omogeneizzate includono già la rigidezza del calcestruzzo. Il raffinamento della mesh va verificato con analisi di convergenza.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
