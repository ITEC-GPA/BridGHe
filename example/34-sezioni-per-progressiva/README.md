# 34 · Sezioni per progressiva e rastremazione

[Apri la definizione GHX](34-sezioni-per-progressiva.ghx) · [Report eseguito](validation.json) · [Guida completa](../../docs/BRIDGE-DESIGN.md)

## Dati e componenti

Ponte 30+40+30 m, tre travi, conci da 12 m. G2 riceve Tapered H nell'intervallo 12.345–45 m e scala verticale da 1 a 1.2.

`Bridge Section Schedule → Bridge Member Assignment`

## Procedura

Aprire il file con la Release corrente di BridGHe in Rhino 8. Gli input numerici sono in mm e le proprietà FEM in N-mm-MPa. I parametri tipizzati memorizzati sono sostituibili collegando i normali componenti di proprietà/definizione.

Modificare Start, End, Group e le scale locali X/Y. Sostituire la proprietà memorizzata con un normale componente Beam Property. Collegare più regole a Schedule solo se non si sovrappongono sulla stessa famiglia/trave/concio.

## Controlli attesi

La trave viene suddivisa esattamente a 12.345 m. La proprietà cambia soltanto su G2, gli ID fisici dei conci rimangono coerenti.

## Ambito

La rastremazione dimensionale richiede una sezione dimensionale Straus; le sezioni generiche/omogeneizzate non la supportano. Per queste usare proprietà distinte per tratto.

Il generatore risolve il documento, lo salva, lo riapre e ripete i controlli. `validation.json` riporta il risultato effettivo; `solverInvoked=false` significa che Straus non è stato eseguito.
