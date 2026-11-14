# Inviluppi delle azioni e degli spostamenti

**Aggiornamento 14 novembre 2026 (BridGHe 0.4.0).** Questa copia è stata risalvata con i dati GPC della 0.4.0: nel file non ci sono più dati FEM (né il blocco `FemmR6` né modelli FeMM). Componenti, GUID, collegamenti, valori di input, fasi, unità e selezioni sono quelli di prima: cambia solo il modo in cui i dati sono salvati, e quindi la dimensione del file (da 1.013.146 a 241.754 byte). Riaperto e ricalcolato senza Straus, il file dà gli stessi risultati della copia precedente, valore per valore (41 uscite e messaggi dei componenti, tutti uguali). La copia precedente resta nella cronologia del repository (commit `9350aeb`).

Componenti: **Bridge Result Cases; Bridge Beam Envelope; Bridge Bearing Envelope; Bridge Displacement Envelope**.

Aprire [24-inviluppi-concomitanti.ghx](24-inviluppi-concomitanti.ghx) con la Release aggiornata.

## Dati e scopo

Campioni controllati G e Q in N e mm. Nella 0.3.0 Q era al passo mobile 2 (StepType=Moving); i risultati GPC della 0.4.0 non hanno passi, quindi Q esce con Step 0 e senza Step type. Sono dati di test, non risultati di una nuova analisi Straus.

## Procedura

Result Cases elenca i casi effettivamente memorizzati. Collegare un modello analizzato agli inviluppi, selezionare IDs e Cases oppure lasciare vuoti per tutti. Component sceglie la grandezza; Mode=0 minimo, 1 massimo, 2 estremo assoluto con segno. Leggere Case, Step, Step type e il ramo Concomitant della stessa riga.

## Risultati attesi e campo

M3 in valore assoluto governa nel caso Q: la sestupla contiene N=-50 N, V2=13 N, V3=-17 N, T=19 Nmm, M2=-23 Nmm e M3 negativo. Fz minimo=-120 N; Uz assoluto=-10 mm. Coverage segnala i casi mancanti alle stazioni; nessuna interpolazione è eseguita. La posizione del mezzo è identificata dal caso/passo disponibile, non convertita in distanza.

## Riproduzione

Dal 14 novembre 2026 non usare `--utility-examples` per riprodurre i file distribuiti: quel comando riscrive gli esempi 23–28 e per il 23 ricostruisce un modello diverso (vedere il suo README).

Per il solo esempio 24 usare una copia degli esempi a `9350aeb` come origine: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --u2-remake-examples only=24 from=<cartella>`. Senza `write` il risultato resta nella cartella temporanea dello strumento; non aggiungere `write` senza aver confrontato le chiavi e ottenuto l'approvazione prevista per le catture. Per l'uso ordinario aprire il file distribuito su una copia e controllare gli inviluppi concomitanti: non occorre rigenerarlo.

[Rapporto della prova](validation.json) · [Indice degli esempi](../README.md).
