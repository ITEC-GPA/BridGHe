# 05 Incrementi non lineari

Aprire [05-nonlinear-increments.ghx](05-nonlinear-increments.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Mensola con incrementi 0,5P, P, 2P nel solutore NLA; estrazione soltanto di Half e Double.

Controlli di riferimento: uZ=−0,047619047619 e −0,190476190476 mm; reazioni e momenti; indice originale degli incrementi.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 05
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 05
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
