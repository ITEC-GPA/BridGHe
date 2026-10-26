# 42 · Frecce e controfreccia

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](42-frecce-controfreccia.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Spostamenti SINTETICI sui nodi reali del ponte rettilineo: parabola con massimo relativo 20 mm più cedimenti lineari. Non derivano da Straus.

`Bridge Deflection & Camber`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Selezionare Case=G1, trave 1. Confrontare Absolute, Relative e Camber. Cambiare Display Scale: i numeri devono rimanere uguali. Con un modello reale collegare quello analizzato e la sua stessa Bridge Geometry.

## Controlli attesi

Alla progressiva 15 m: assoluta -28 mm, relativa -20 mm, controfreccia +20 mm. La relativa è nulla agli appoggi.

## Ambito

L'interpolazione della corda usa gli spostamenti degli appoggi. La controfreccia è una compensazione geometrica del risultato scelto, non un progetto automatico della monta.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
