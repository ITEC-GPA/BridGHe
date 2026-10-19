# Incrementi di ritiro e confronto dei risultati

Componenti: **Bridge Shrinkage Increments; Bridge Result Comparison**.

Aprire [27-ritiro-confronti.ghx](27-ritiro-confronti.ghx) con la Release aggiornata.

## Dati e scopo

Ritiro cumulativo assegnato -100, -200 e -250 microstrain ai giorni 7, 28 e 100; input adimensionali. Confronto di spostamenti campionati alle stesse posizioni fisiche con ordine differente.

## Procedura

Shrinkage Increments riceve Days, Cumulative strain e Initial strain. Collegare ogni incremento alla rispettiva Bridge Shrinkage Action. Comparison riceve chiavi fisiche univoche, valori di riferimento/candidato e tolleranze coerenti con le unità. Per confronti di mesh usare posizioni fisiche uguali, non ID che cambiano nella discretizzazione.

## Risultati attesi e campo

Incrementi: -0.0001, -0.0001, -0.00005; somma=-0.00025. Il confronto accetta abs(candidato-riferimento) <= Abs + Rel*abs(riferimento); Rel=0.01 vale 1%. Contro riferimento zero, la differenza relativa non nulla è NaN e decide la tolleranza assoluta. Non è una legge predittiva di ritiro né uno studio automatico di convergenza.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
