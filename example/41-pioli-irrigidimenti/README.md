# 41 · Distribuzione di pioli e irrigidimenti

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

[Apri la definizione GHX](41-pioli-irrigidimenti.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

G2, tratto [0,10 m): pioli Ø22 H180, due per fila, passo 200 mm; irrigidimenti simmetrici 200×20 mm, passo 4 m; sezione gPC con anima 1330×16 mm.

`Bridge Detail Layout`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Sostituire i parametri gPC memorizzati con Bridge Section Geometry, Bridge Studs e Bridge Intermediate/Support Stiffener. Regolare Steel Top Offset dalla quota dell'asse: non viene dedotto il baricentro. Intervalli adiacenti sono semiaperti per evitare file duplicate.

## Controlli attesi

100 pioli, 6 prismi chiusi degli irrigidimenti. Volume di ogni piatto 200×1330×20 mm³. Gli oggetti Details conservano i parametri gPC per i verificatori.

## Ambito

Il layout non aggiunge rigidezza FEM e non costituisce una verifica dei pioli o dell'anima. Copriferro, numero intero di pioli e compatibilità geometrica vengono controllati.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
