# 01 Mensola elastica

Aprire [01-cantilever.ghx](01-cantilever.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Mensola L=3000, incastro a sinistra; due forze Z di −600 e −400 N nello stesso caso P; combinazione 1,5P.

Controlli di riferimento: uZ(P)=−0,095238095238 mm; RZ=1000 N; reazione MY=−3000000 Nmm. Combinazione moltiplicata per 1,5.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 01
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 01
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
