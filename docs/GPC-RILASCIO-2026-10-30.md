# Rilascio ufficiale GPC — 30 ottobre 2026

BridGHe e ReBear passano al set ufficiale costruito da `origin/master` dopo il lavoro sull'acciaio successivo ad ANTHEA 1.16.0:

| Repository | Commit |
|---|---|
| Utilities | `e57b440b` |
| Geometry | `6734eee1` |
| Model | `9ca8abcf` |
| Checker | `2592493f` |

Il set è prodotto con `Checker/build/Release-Set.ps1`, senza `-Candidate`, da checkout puliti di quei commit. Le versioni sono controllate rispetto al set ufficiale del 29 ottobre ("an", Model `9f40d98a`, Checker `bc2b3435`). Il [manifest distribuito](../lib/GPC/release-set.json) contiene **`candidate=false`** e `onOriginMaster=true` per tutti i repository.

## Binari e versioni

| Assembly | 27 ottobre | 30 ottobre |
|---|---|---|
| GPCUtilities | 3.0.0.0 | 3.0.0.0 |
| GPCGeometry | 2.3.0.0 | 2.3.0.0 |
| DelaunayMesh | 2.0.0.14 | 2.0.0.14 |
| GPCModel | 5.21.0.1 | 5.30.0.0 |
| GPCChecker.Concrete | 0.25.0.0 | 0.32.0.1 |
| GPCChecker.Steel | 2.118.0.2 | 2.127.0.0 |
| GPCChecker.CompositeBridge | 1.19.0.5 | 1.20.0.1 |

Il set comprende la cache dei domini di rottura, la cache dei punti di Gauss delle mesh di calcestruzzo e il riuso delle soluzioni nelle analisi di CompositeBridge (Checker `4fcf8bfe`). Comprende anche il lavoro sull'acciaio del 29–30 ottobre. Le versioni dei plugin restano BridGHe **0.2.0** e ReBear **0.3.0**.

## Controlli eseguiti il 30 ottobre

| Controllo | Esito |
|---|---|
| Suite Model | 1.662 superate, zero fallimenti; 12 prove non eseguite (fixture native facoltative) |
| Suite Checker | 9.875 superate, zero fallimenti, inclusi Concrete/Steel net472 (contratto K0) e Design net8.0/net472 |
| Compatibilità API | Nessuna rimozione inattesa |
| Compilazione di BridGHe | Release riuscita senza opzioni per candidati, zero errori e zero avvisi |
| Controlli principali BridGHe | 269 superati, regressioni comprese, senza analisi native |
| 99 REV004 nativo | 2.736 righe GPC identiche bit per bit alla baseline del 29 ottobre (impronta `8E9756B7`) |
| 50 REV003 nativo | 66.960 righe GPC identiche bit per bit alla baseline (impronta `C1CF95A0`); 712 s contro i 3.047 s della baseline |
| ReBear | `Verify.ps1` 40/40, `Examples.ps1` 20/20; esempi con gli stessi numeri |
| Coerenza fra plugin | Le cinque DLL comuni (Utilities, Geometry, DelaunayMesh, GPCModel, Concrete) hanno lo stesso SHA-256 |

Le impronte confrontano tutti i risultati di sezione di Super Element Check e Bridge Detail Check con la baseline del 29 ottobre, calcolata col set del 27 ottobre ([procedura](PROVE-GPC-DOMINIO.md)). Il lavoro sull'acciaio non cambia quindi i risultati dei due esempi.

Nei risultati degli esempi di ReBear cambiano soltanto la versione del motore e alcuni riferimenti normativi dei dettagli, corretti in GPC (per esempio NTC 2018 §4.1.6.1.3 al posto di "§4.1.6.1 .3").
