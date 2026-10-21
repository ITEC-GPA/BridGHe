# Riepilogo dei controlli GPC

Componenti: **Bridge Check Summary**.

Aprire [26-riepilogo-verifiche-gpc.ghx](26-riepilogo-verifiche-gpc.ghx) con la Release aggiornata.

## Dati e scopo

Sezione mista H con quattro incrementi di sezione: Steel, Long, Instant, Shrinkage. Analisi locale eseguita da Bridge Section Check/GPC; nessun solutore globale.

## Procedura

Collegare Results della verifica a Results di Check Summary e lasciare **Labels vuoto**. I nomi sono automatici: in questo esempio di sezione isolata contengono posizione nell'albero e fase; non vengono inventati trave, stazione o combinazione FEM. Leggere Phase, Check, Ratio, Status, Units e Notes. Maximum finite ratio e Governing labels identificano il massimo tra i rapporti numerici disponibili.

Per sostituire i nomi automatici, Labels accetta facoltativamente un testo non vuoto per ogni risultato, con lo stesso ordine e gli stessi rami. Nel flusso Super Element Check → Detail Check → Summary i risultati portano invece già superelemento, Beam ID, distanza locale in mm e combinazione.

## Risultati attesi e campo

Tutti i rapporti riportati sono identici ai risultati GPC. NaN resta NaN. Unavailable ratios conta i rapporti non disponibili. Il massimo non copre verifiche non eseguite, né sostituisce una verifica completa del ponte.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
