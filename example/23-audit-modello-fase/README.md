# Controllo del modello e copia di fase

**Aggiornamento 14 novembre 2026 (BridGHe 0.4.0).** Il modello salvato nella 0.3.0 per Bridge Model Audit non si poteva più leggere nella 0.4.0: le due travi non avevano i nodi agli estremi (messaggio «Nessun nodo FEM entro tolleranza alle coordinate 10000, 0, 0»), quindi Audit e Phase Model restavano vuoti. Il modello è stato rifatto con Build Model sugli stessi dati della 0.3.0, dati con i costruttori GPC: il nodo A vincolato (Tx, Ty, Tz) e le travi parallele B0 e B1 lunghe 10 m, a 2 m di distanza, sulla proprietà «Longitudinale fase 0»; Build Model aggiunge i nodi 1, 2 e 3 agli altri estremi. Il modello porta le bozze dei suoi elementi, quindi Decompose Model e Super Element Filter lo accettano come ogni modello di Build Model. I risultati salvati (casi G e Q, sulle travi e sul nodo A) sono gli stessi campioni di prima. La proprietà di Phase Model è quella salvata, letta per valore. Componenti, GUID e collegamenti non cambiano; il file passa da 284.895 a 104.339 byte. Ora Audit segnala DISCONNECTED (atteso) e Phase Model dà la copia con B0 sulla proprietà omogeneizzata e senza risultati. La copia precedente resta nella cronologia del repository (commit `9350aeb`).

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
