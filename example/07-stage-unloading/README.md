# 07 Scarico e ricarico

Aprire [07-stage-unloading.ghx](07-stage-unloading.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Tre fasi: P → 0 → 0,5P, con struttura invariata.

Controlli di riferimento: Ritorno elastico a zero entro tolleranza e successivo ricarico; nessun accumulo errato dei fattori.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 07
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 07
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
