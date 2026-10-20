# 22 Fasi di sezione e benchmark del ritiro

Questa cartella raccoglie le prove numeriche senza definizione GHX dedicata.

Riesecuzione del **20 ottobre 2026**: `--bridge-native` supera **30 confronti** sui cinque contributi di sezione. Report: [validation-native.json](validation-native.json). Il warning `Stages on load cases not supported` riguarda i metadata di fase destinati a gPC: questa prova risolve tre casi meccanici Straus e applica due contributi di ritiro nel checker; non esegue una nuova sequenza di costruzione staged nel solutore. I due benchmark di ritiro libero/vincolato riportati sotto restano le esecuzioni storiche.

- `calcoli/fasi-sezione/`: tre incrementi meccanici e due incrementi di deformazione imposta su una sezione composta, con confronto indipendente delle tensioni.
- `calcoli/benchmark-shrink-free/`: ritiro con deformazione libera.
- `calcoli/benchmark-shrink-fixed/`: ritiro con vincoli alle deformazioni.

Le [ipotesi e formule delle cinque fasi](../00-documentazione/benchmark-01-10.md#cinque-contributi-di-fase-bridgechecks) e i [dettagli del ritiro](../00-documentazione/ponti-12-18.md#ritiro-primario-e-secondario) descrivono il campo dei test. I riepiloghi storici sono [bridgeChecks.json](../00-documentazione/validazioni/bridgeChecks.json) e [benchmarks.json](../00-documentazione/benchmarks.json).

Dalla root, con licenza Straus disponibile:

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-native
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-suite --probe
```

Questi benchmark controllano trasferimento, segni, tensioni ed effetti del ritiro. Non sostituiscono la verifica completa di un ponte.
