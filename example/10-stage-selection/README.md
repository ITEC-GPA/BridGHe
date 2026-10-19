# 10 Selezione degli incrementi

Aprire [10-stage-selection.ghx](10-stage-selection.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Construction: 0,5G e G; Service: G+0,5Q e 1,35G+1,5Q, G=Q=1000 N verso −Z.

Controlli di riferimento: Estratti solo gli incrementi 2 e 4; uZ=−0,095238095238 e −0,271428571429 mm; verifica della fase nativa associata.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 10
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 10
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
