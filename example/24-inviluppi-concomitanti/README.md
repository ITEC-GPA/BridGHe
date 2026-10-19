# Inviluppi delle azioni e degli spostamenti

Componenti: **Bridge Result Cases; Bridge Beam Envelope; Bridge Bearing Envelope; Bridge Displacement Envelope**.

Aprire [24-inviluppi-concomitanti.ghx](24-inviluppi-concomitanti.ghx) con la Release aggiornata.

## Dati e scopo

Campioni controllati G e Q in N e mm, con passo Q=2 e StepType=Moving. Sono dati di test, non risultati di una nuova analisi Straus.

## Procedura

Result Cases elenca i casi effettivamente memorizzati. Collegare un modello analizzato agli inviluppi, selezionare IDs e Cases oppure lasciare vuoti per tutti. Component sceglie la grandezza; Mode=0 minimo, 1 massimo, 2 estremo assoluto con segno. Leggere Case, Step, Step type e il ramo Concomitant della stessa riga.

## Risultati attesi e campo

M3 in valore assoluto governa nel caso Q: la sestupla contiene N=-50 N, V2=13 N, V3=-17 N, T=19 Nmm, M2=-23 Nmm e M3 negativo. Fz minimo=-120 N; Uz assoluto=-10 mm. Coverage segnala i casi mancanti alle stazioni; nessuna interpolazione è eseguita. La posizione del mezzo è identificata dal caso/passo disponibile, non convertita in distanza.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
