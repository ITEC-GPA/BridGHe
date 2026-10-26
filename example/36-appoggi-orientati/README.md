# 36 · Appoggi orientati

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](36-appoggi-orientati.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Dodici appoggi sul ponte curvo: centro prima spalla fisso; centri delle altre linee guidati longitudinalmente; esterni multidirezionali.

`Bridge Bearings`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Types segue l'ordine supporto/trave riportato in Labels. 0 blocca le tre traslazioni; 1 libera X; 2 libera X/Y; 3 usa Kt/Kr. Le rotazioni sono libere salvo le molle assegnate. Guide Angle ruota gli assi locali.

## Controlli attesi

Dodici Nodes e dodici Frames, orientati secondo la tangente orizzontale. La preview rappresenta gli stessi assi usati dall'export.

## Ambito

Verificare stabilità e vincoli del ponte reale. Il test offline controlla i dati; la lettura nativa UCS/rigidezze richiede la licenza Straus.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
