# Rilascio ufficiale GPC — 2 novembre 2026

Dal 2 novembre BridGHe e ReBear usano il set ufficiale `gpc-release-20261102-742f45f3`. Le verifiche di sezione danno gli stessi risultati bit per bit del set della [sera del 1° novembre](GPC-RILASCIO-2026-11-01-sera.md). Il set è prodotto con `Checker/build/Release-Set.ps1`, senza `-Candidate`, da checkout puliti dei commit pubblicati, con le versioni controllate rispetto al set precedente della catena (`gpc-release-20261102-89d30fd7`). Il [manifest distribuito](../lib/GPC/release-set.json) contiene `candidate=false` e `onOriginMaster=true` per tutti i repository; le 14 suite delle librerie sono superate.

| Repository | Commit |
|---|---|
| Utilities | `e57b440b` |
| Geometry | `6734eee1` |
| Model | `15587299` |
| Checker | `742f45f3` |

## Binari e versioni

| Assembly | 1° novembre (sera) | 2 novembre |
|---|---|---|
| GPCUtilities | 3.0.0.0 | 3.0.0.0 |
| GPCGeometry | 2.3.0.0 | 2.3.0.0 |
| DelaunayMesh | 2.0.0.14 | 2.0.0.14 |
| GPCModel | 5.33.0.0 | 5.37.0.2 |
| GPCModelData (solo ReBear) | 0.3.0.19 | 0.3.0.25 |
| GPCChecker.Concrete | 0.34.0.0 | 0.35.0.5 |
| GPCChecker.Steel | 2.127.0.7 | 2.129.0.4 |
| GPCChecker.CompositeBridge | 1.23.0.0 | 1.23.0.6 |

## Novità

Il set porta il lavoro pubblicato sui master fra la sera del 1° e il 2 novembre:

- **GPCModel 5.37**: il modello si costruisce e si rinomina senza le scansioni quadratiche dei contatori degli id e degli indici dei nomi, e si copia in memoria con gli stessi contratti dati del giro XML. Su una membrana di 10.201 nodi e 20.000 gusci la costruzione passa da 8,4 a 0,2 s e la copia da 11–14 a 0,5–0,8 s, con gli stessi grafi, impronte e risultati. Le revisioni 5.37.0.1 e 5.37.0.2 fanno leggere esattamente su .NET Framework gli archivi scritti su .NET; su .NET 8, dove gira Rhino 8, il comportamento non cambia.
- **GPCModel 5.34–5.36** (modulo Modelli di ANTHEA): regola 100/30 nelle varianti sismiche, componenti principali dei plate, contratti delle azioni aggiuntive, sovrapposizioni nei dati dei dettagli.
- **Concrete 0.35**: sovrapposizioni EN 8.7.2(3) allungate dell'intero interferro, verifiche EN 8.7.2(4) e 8.8 nei dettagli delle membrature Eurocodice, duttilità di curvatura anche sulla variante generica del momento-curvatura.
- **Steel 2.128–2.129**: flussi delle membrature e dei terminali in acciaio.
- **CompositeBridge 1.23.0.1–1.23.0.6**: meno allocazioni nell'analisi per fasi e ricompilazioni.

## Controlli

| Controllo | Esito |
|---|---|
| `tools/verify_gpc_set.ps1` | 7 DLL verificate contro il manifest del set rilasciato |
| Banco GPC, cattura del 50 (`--gpc-bench … --rhino --stream --parallel --batch`) | 66.960 righe (Super Element e Detail) identiche bit per bit alla cattura |
| Banco GPC, cattura del 99 | 2.736 righe identiche bit per bit |
| Memoria del banco del 50 | 224,5 GB allocati in 135 s, contro 225,5 GB in 134 s con il set della sera del 1° novembre, nelle stesse condizioni |
| Controlli principali BridGHe (`--core-only`) | 269 superati, 1 facoltativo saltato (registrazione con i plugin installati) |
| ReBear | `Verify.ps1` 41/41, `Examples.ps1` 20/20: i numeri degli esempi non cambiano; nell'esempio 14 cambia il testo del messaggio sulla lunghezza di sovrapposizione (Concrete 0.35) |
| Coerenza fra plugin | Le cinque DLL comuni (Utilities, Geometry, DelaunayMesh, GPCModel, Concrete) hanno lo stesso SHA-256 in BridGHe e ReBear |

I run nativi con Straus non sono stati ripetuti: il banco ricalcola dalle catture tutti i risultati di sezione GPC dei due esempi, con gli stessi hash d'ingresso, e il codice di BridGHe non cambia.
