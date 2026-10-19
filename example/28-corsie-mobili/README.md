# Percorsi mobili paralleli

Componenti: **Bridge Moving Lanes**.

Aprire [28-corsie-mobili.ghx](28-corsie-mobili.ghx) con la Release aggiornata.

## Dati e scopo

Percorso rettilineo lungo 20000 mm, carico dimostrativo da 100000 N, offsets -3000,0,+3000 mm e casi L1,L2,L3.

## Procedura

Creare il prototipo con Moving Load Path, collegarlo a Moving Lanes, assegnare Offsets e Cases della stessa lunghezza. Collegare Paths ad Attach Moving Paths del modello, poi a Moving Static Analysis. Le preview mostrano le tre traslazioni.

## Risultati attesi e campo

I tre template sono copie indipendenti. L’offset positivo segue il terzo punto del prototipo proiettato perpendicolarmente al suo asse. Non vengono creati coefficienti normativi o simultaneità delle corsie. I percorsi curvi vanno definiti singolarmente con Moving Load Path. Nessuna analisi Straus viene lanciata aprendo questo esempio.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
