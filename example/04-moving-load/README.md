# 04 Asse mobile su trave

Aprire [04-moving-load.ghx](04-moving-load.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Trave appoggiata L=6000, 12 elementi; asse mobile da 1000 N verso −Z; 13 divisioni di percorso.

Controlli di riferimento: 13 posizioni ai centri delle divisioni; equilibrio e reazione destra per ogni posizione; inviluppo max |BM2|=1500000 Nmm.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 04
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 04
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
