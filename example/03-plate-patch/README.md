# 03 Piastra in trazione

Aprire [03-plate-patch.ghx](03-plate-patch.ghx) in Rhino 8 / Grasshopper.

## Schema e obiettivo

Shell rettangolare 1000×500, t=10; UX bloccato sul bordo sinistro, UY in un angolo, moti fuori piano bloccati; 1000 N in X su ciascuno dei due nodi destri.

Controlli di riferimento: uX=0,001904761905 mm; contrazione trasversale −0,000285714286 mm; Nxx=4 N/mm; somma RX=−2000 N.

## Procedura

1. Verificare il modello incorporato e il pannello del file `.st7`, diretto a `calcoli/grasshopper/model.st7`.
2. Con licenza Straus disponibile, avviare Run nel componente di analisi; per il caso mobile usare il toggle e poi riportarlo a false.
3. Leggere spostamenti e sollecitazioni nei componenti collegati. Unità N, mm, MPa, senza peso proprio.
4. Confrontare i risultati con i valori sopra e con le [formule e tolleranze comuni](../00-documentazione/benchmark-01-10.md).

## Riproduzione dalla root

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --examples 03
dotnet run --project tests/BridGHe.Smoke -c Release -- --gh-examples 03
```

Le due esecuzioni richiedono la licenza. La prima scrive `calcoli/native/<data-ora>/`, la seconda `calcoli/grasshopper/`. I report aggregati si trovano in `../00-documentazione/validazioni/`. Il modello iniziale è internalizzato: la ricetta modificabile è in [NativeExamples.cs](../../tests/BridGHe.Smoke/NativeExamples.cs).
