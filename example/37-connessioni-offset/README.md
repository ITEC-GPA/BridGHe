# 37 · Connessioni e offset

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](37-connessioni-offset.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Due connessioni fra asse trave z=-1500 mm e soletta z=-125 mm, rappresentate come link rigidi e aste elastiche; una trave con offset locale Y=100 mm.

`Bridge Rigid Links / Bridge Connection Beams / Bridge Beam Offsets`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Confrontare i tre componenti. Bridge Rigid Links produce nodi con link rigidi nativi; Bridge Connection Beams produce aste elastiche con proprietà esplicita; Bridge Beam Offsets copia le travi e imposta Offset. Collegare gli elementi scelti a Build Model.

## Controlli attesi

Quattro nodi per due link, due aste deformabili e una trave modificata. Bridge Beam Offsets invalida eventuali risultati della copia.

## Ambito

Non sommare i tre modi sulla stessa connessione. I link richiedono nodi terminali univoci del modello. L'export nativo è testabile con --bridge-connections-native.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
