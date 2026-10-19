# 14 Ponte curvo a una campata

Aprire [14-curvo-1campata.ghx](14-curvo-1campata.ghx). Geometria: **30 m, R = 80 m**; 48 elementi per campata e longitudinale.

## Procedura

1. Controllare [parameters.json](parameters.json), i quattro percorsi `.st7` dentro `calcoli/` e le sezioni.
2. Eseguire CALCOLA 4 MODELLI con licenza Straus disponibile, poi riportarlo a false.
3. Controllare le azioni G1, G2, Q e RITIRO e le relative Bridge Phase.
4. Premere Run su Build BridGHe Model e Super Element Check; consultare tensioni, dettagli e plot.

Le analisi sono incrementali: primario e secondario del ritiro appartengono alla stessa azione. Il nuovo assemblaggio è stato verificato con risultati archiviati, come riportato in [phase-workflow-validation.json](phase-workflow-validation.json). Per i ponti curvi, torsione e flessione minore restano nei risultati FEM e sono fuori dal campo del checker H: il messaggio di blocco non è una verifica completa.

## File del caso

- `parameters.json`: geometria, discretizzazione e, per le spine, sezioni distinte.
- `geometry.json`, `nodes.csv`, `forces.csv`: geometria e risultati storici numerici.
- `validation.json`: controlli analitici, equilibrio e sensibilità alla mesh.
- `calcoli/`: modelli, risultati, raffinamenti di mesh e cache dei singoli casi.
- `grasshopper-stresses.json` e `details.json`, quando presenti: verifiche di sezione e dettagli compatibili.

[Ipotesi, materiali, vincoli e limiti della famiglia](../00-documentazione/ponti-12-18.md). [Procedura aggiornata delle fasi](../../docs/FASI-E-AZIONI.md).

## Riproduzione dalla root

Senza nuove analisi, usando gli archivi locali:

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-gh-replay 14
```

Con licenza e nuove analisi, dopo aver salvato separatamente eventuali modifiche manuali al GHX:

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-suite 14 --gh
```
