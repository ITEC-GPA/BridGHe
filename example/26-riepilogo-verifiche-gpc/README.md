# Riepilogo dei controlli GPC

Componenti: **Bridge Check Summary**, **Bridge Result Labels** e una **Value List** per il formato.

Aprire [26-riepilogo-verifiche-gpc.ghx](26-riepilogo-verifiche-gpc.ghx) con la Release aggiornata.

## Dati e scopo

Sezione mista H con quattro incrementi di sezione: Steel, Long, Instant, Shrinkage. Analisi locale eseguita da Bridge Section Check/GPC; nessun solutore globale.

## Procedura

Collegare Results della verifica a Results di Check Summary e lasciare **Labels vuoto**. I nomi sono automatici: in questo esempio di sezione isolata contengono posizione nell'albero e fase; non vengono inventati trave, stazione o combinazione FEM. Leggere Phase, Check, Ratio, Status, Units e Notes. Maximum finite ratio e Governing labels identificano il massimo tra i rapporti numerici disponibili.

Il file mostra anche come scegliere il formato: gli stessi Results entrano in **Bridge Result Labels**, la cui uscita Labels va al riepilogo. La Value List collegata a Format propone **Completa**, **Trave e stazione**, **Combinazione**, **Fase**; basta collegarne una nuova per compilarne automaticamente le voci. Completa è il default. Per questo calcolo locale, Fase restituisce Steel, Long, Instant e Shrinkage; trave/stazione e combinazione sono dichiarate non disponibili. Scollegando il filo Labels dal riepilogo si torna al formato completo automatico.

Per sostituire i nomi automatici, Labels accetta facoltativamente un testo non vuoto per ogni risultato, con lo stesso ordine e gli stessi rami. Nel flusso Super Element Check → Detail Check → Summary i risultati portano invece già superelemento, Beam ID, distanza locale in mm e combinazione.

## Risultati attesi e campo

Tutti i rapporti riportati sono identici ai risultati GPC. NaN resta NaN. Unavailable ratios conta i rapporti non disponibili. Il massimo non copre verifiche non eseguite, né sostituisce una verifica completa del ponte.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).

La Release aggiornata è stata compilata e il grafo è stato calcolato, salvato e riaperto in Rhino/Grasshopper. Il test verifica l'identità dei rapporti GPC, una label per risultato e il ripristino delle quattro opzioni della Value List. Le prove dedicate controllano anche la selezione dei formati, i rami dell'albero e la migrazione delle definizioni precedenti.
