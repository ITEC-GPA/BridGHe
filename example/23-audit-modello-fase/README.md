# Controllo del modello e copia di fase

**Aggiornamento 14 novembre 2026 (BridGHe 0.4.0).** Il modello salvato nella 0.3.0 per Bridge Model Audit non si poteva più leggere nella 0.4.0: le due travi non avevano i nodi agli estremi (messaggio «Nessun nodo FEM entro tolleranza alle coordinate 10000, 0, 0»), quindi Audit e Phase Model restavano vuoti. Il modello è stato ricostruito con i costruttori GPC, con gli stessi campioni: due travi parallele B0 e B1 lunghe 10 m, a 2 m di distanza, con i nodi A, 2, 3 e 4 agli estremi (prima c'era solo A), casi G e Q, risultati solo su A e sulle travi. La proprietà di Phase Model è quella salvata, letta per valore. Componenti, GUID e collegamenti non cambiano; il file passa da 284.895 a 92.570 byte. Ora Audit segnala DISCONNECTED (atteso) e NO_SUPPORT (il modello dimostrativo non ha vincoli), Phase Model dà la copia con B0 sulla proprietà omogeneizzata e senza risultati. La copia precedente resta nella cronologia del repository (commit `9350aeb`).

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
