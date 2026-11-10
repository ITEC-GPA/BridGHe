# Set GPC fissato per BridGHe

Le otto DLL e i relativi PDB sono copiati dal medesimo set coerente. `release-set.json` conserva tutti i 15 assembly del set di origine, con versioni, SHA-256, commit ed esiti delle suite; BridGHe utilizza solo il sottoinsieme presente qui.

`tools/verify_gpc_set.ps1` controlla il manifest e i binari prima della compilazione. Il percorso ordinario richiede `candidate=false`; per il collaudo esplicito di un set candidato usare `dotnet build BridGHe.sln -c Release -p:GpcAllowCandidate=true`.

Non sostituire una singola DLL con una build esterna: aggiornare insieme binari, PDB e manifest dal set prodotto da `Checker/build/Release-Set.ps1`, poi ripetere i collaudi dei plugin. Le DLL comuni a ReBear devono conservare gli stessi hash. Il manifest distribuito dal post-build è `bin/Release/gpc-release-set.json`.

Il set corrente è quello ufficiale dell'8 novembre 2026 (`candidate=false`), adottato il 10 novembre su richiesta dell'utente e comune con ReBear: Model `ff318f87`, Checker `9e513417`. Il sottoinsieme comprende Utilities 3.0.0.0, Geometry 2.3.0.0, DelaunayMesh 2.0.0.14, Model 5.50.0.2, ModelData 0.3.0.30, Concrete 0.37.0.1, Steel 2.134.0.0 e CompositeBridge 1.31.0.1. La copia completa proviene da `ANTHEA/supporto/artefatti/librerie-20261108-av`; il manifest coincide con quello di ReBear. `build/GpcReferences.props` aggiunge System.Text.Json 10.0.13 e le sue dipendenze per .NET 8.

Il [resoconto di stabilizzazione](../../docs/STABILIZZAZIONE-2026-11-10.md) riporta il collaudo di BridGHe. Il [rilascio del 2 novembre](../../docs/GPC-RILASCIO-2026-11-02.md) e quelli del [1° novembre (sera)](../../docs/GPC-RILASCIO-2026-11-01-sera.md), del [1° novembre (mattina)](../../docs/GPC-RILASCIO-2026-11-01.md), del [30 ottobre](../../docs/GPC-RILASCIO-2026-10-30.md) e del [27 ottobre](../../docs/GPC-RILASCIO-2026-10-27.md) restano evidenze dei set precedenti.
