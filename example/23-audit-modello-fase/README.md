# Controllo del modello e copia di fase

Componenti: **Bridge Model Audit; Bridge Phase Model**.

Aprire [23-audit-modello-fase.ghx](23-audit-modello-fase.ghx) con la Release aggiornata.

## Dati e scopo

Il modello contiene due travi parallele volutamente disconnesse. Audit segnala DISCONNECTED. Phase Model modifica B0 con la proprietà omogeneizzata e cancella risultati, combinazioni e fasi precedenti. Il modello originale non cambia.

## Procedura

Collegare il modello di acciaio ad Audit, leggere Severity/Code/Element/Description; collegare Model a Phase Model. Assegnare Name, Beam IDs e Property. Clear loads=true prepara un nuovo incremento; false conserva i carichi. Assegnare poi i carichi della fase e calcolare con Straus.

## Risultati attesi e campo

Due beam, stessi ID e geometria; zero risultati nella copia. Il warning di disconnessione è atteso. La diagnostica geometrica non prova la stabilità della matrice FEM.

## Riproduzione

Dalla root: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --utility-examples`. Il comando rigenera i sei esempi 23–28: conservare prima eventuali modifiche manuali. Il test calcola il grafo e ripete i controlli dopo salvataggio e riapertura. Nessuna licenza Straus richiesta.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
