# Set GPC fissato per BridGHe

Le sette DLL e i relativi PDB sono copiati dal medesimo set coerente. `release-set.json` conserva tutti i 14 assembly del set di origine, con versioni, SHA-256, commit ed esiti delle suite; BridGHe utilizza solo il sottoinsieme presente qui.

`tools/verify_gpc_set.ps1` controlla il manifest e i binari prima della compilazione. Il percorso ordinario richiede `candidate=false`; per il collaudo esplicito di un set candidato usare `dotnet build BridGHe.sln -c Release -p:GpcAllowCandidate=true`.

Non sostituire una singola DLL con una build esterna: aggiornare insieme binari, PDB e manifest dal set prodotto da `Checker/build/Release-Set.ps1`, poi ripetere i collaudi dei plugin. Le DLL comuni a ReBear devono conservare gli stessi hash. Il manifest distribuito dal post-build è `bin/Release/gpc-release-set.json`.

Il set corrente è ufficiale (`candidate=false`): [secondo rilascio del 30 ottobre 2026](../../docs/GPC-RILASCIO-2026-10-30.md#secondo-set-batch-per-stazione), comune con ReBear. Il [rilascio del 27 ottobre](../../docs/GPC-RILASCIO-2026-10-27.md) e il [collaudo di integrazione del 26 ottobre](../../docs/GPC-INTEGRAZIONE-2026-10-26.md) restano come evidenze dei set precedenti.
