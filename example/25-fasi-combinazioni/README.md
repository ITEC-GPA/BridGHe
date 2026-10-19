# Fasi ordinate e combinazioni per azione

Componenti: **Bridge Phase Table; Bridge Action Combinations**.

Aprire [25-fasi-combinazioni.ghx](25-fasi-combinazioni.ghx) con la Release aggiornata.

## Dati e scopo

Due fasi con numeri 10 e 20, Action ID G1 e CS. G è un campione di prova; CS è ritiro primario con strain=-0.0002. Primary Only esclude le azioni secondarie da vincolo.

## Procedura

Phase Table ordina le fasi e controlla numeri, identità, giorni e Action ID. Passare le fasi a Action Combinations. Action IDs definisce le colonne; Names una combinazione per ramo di Coefficients. Collegare Table al Build BridGHe Model, poi premere Run. Non appiattire la matrice.

## Risultati attesi e campo

Le colonne di ingresso CS,G1 con coefficienti 0.7,1 generano la riga SLS in ordine G1,CS con coefficienti 1,0.7. Le azioni omesse ricevono zero e sono segnalate. I coefficienti sono assegnati dall’utente: il componente non genera combinazioni normative.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
