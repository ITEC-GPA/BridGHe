# 08 Attivazione di elementi

Aprire [08-stage-activation.ghx](08-stage-activation.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Asta assiale incastrata L=3000; prima una trave con P=1000 N, poi attivazione di una seconda trave coincidente e aumento del carico totale a 2P. Morph/Rotate attivi.

Controlli di riferimento: Prima u=P L/(EA); dopo u=1,5 P L/(EA), forze 1500 e 500 N. Nessun risultato per la seconda trave prima della nascita.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 08
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 08
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
