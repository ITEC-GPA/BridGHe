# 09 Rimozione di elementi

Aprire [09-stage-removal.ghx](09-stage-removal.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Sequenza precedente, poi rimozione della seconda trave mantenendo il carico totale 2P.

Controlli di riferimento: u=2 P L/(EA)=0,000476190476 mm; unica trave rimasta con N=2000 N; nessun risultato per quella rimossa.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 09
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 09
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
