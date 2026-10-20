# Esempi BridGHe

Un unico punto di ingresso per definizioni Grasshopper, dati, risultati e istruzioni. La numerazione identifica il caso e rimane stabile quando si rigenerano i file.

- **01–05**: benchmark FEM elementari e carico mobile di base.
- **06–10**: cinque prove delle fasi native Straus.
- **11–18**: ponti misti a graticcio, continui, curvi e con spina centrale.
- **19**: procedura corrente acciaio → misto con Action, Phase e ritiro, riferimento REV003.
- **20**: preview SVG sul canvas Grasshopper.
- **21–22**: benchmark aggiuntivi dei carichi mobili e delle fasi di sezione.

## Come leggere una cartella

Il `README.md` descrive il caso e il file `.ghx` alla radice è la definizione da aprire. Parametri JSON, risultati CSV e report di verifica appartengono allo stesso caso. `calcoli/` contiene modelli Straus, risultati nativi, cache e log rigenerabili; `archivio/` conserva le revisioni precedenti. I dati numerici non sono stati ricalcolati dalla riorganizzazione.

`00-documentazione/` raccoglie soltanto informazioni trasversali: descrizioni delle famiglie, tavole riepilogative, report aggregati e pacchetti storici. I report e i PDF storici conservano la data e il contesto originari.

## Indice

| N | Esempio | Aprire |
|---|---|---|
| 01 | [Mensola elastica](01-cantilever/README.md) | [GHX](01-cantilever/01-cantilever.ghx) |
| 02 | [Trave semplicemente appoggiata](02-simply-supported/README.md) | [GHX](02-simply-supported/02-simply-supported.ghx) |
| 03 | [Piastra in trazione](03-plate-patch/README.md) | [GHX](03-plate-patch/03-plate-patch.ghx) |
| 04 | [Asse mobile su trave](04-moving-load/README.md) | [GHX](04-moving-load/04-moving-load.ghx) |
| 05 | [Incrementi non lineari](05-nonlinear-increments/README.md) | [GHX](05-nonlinear-increments/05-nonlinear-increments.ghx) |
| 06 | [Fasi di carico](06-stage-loading/README.md) | [GHX](06-stage-loading/06-stage-loading.ghx) |
| 07 | [Scarico e ricarico](07-stage-unloading/README.md) | [GHX](07-stage-unloading/07-stage-unloading.ghx) |
| 08 | [Attivazione di elementi](08-stage-activation/README.md) | [GHX](08-stage-activation/08-stage-activation.ghx) |
| 09 | [Rimozione di elementi](09-stage-removal/README.md) | [GHX](09-stage-removal/09-stage-removal.ghx) |
| 10 | [Selezione degli incrementi](10-stage-selection/README.md) | [GHX](10-stage-selection/10-stage-selection.ghx) |
| 11 | [Ponte misto a graticcio](11-ponte-graticcio/README.md) | [GHX](11-ponte-graticcio/11-ponte-graticcio.ghx) |
| 12 | [Ponte continuo a due campate](12-continuo-2campate/README.md) | [GHX](12-continuo-2campate/12-continuo-2campate.ghx) |
| 13 | [Ponte continuo a tre campate](13-continuo-3campate/README.md) | [GHX](13-continuo-3campate/13-continuo-3campate.ghx) |
| 14 | [Ponte curvo a una campata](14-curvo-1campata/README.md) | [GHX](14-curvo-1campata/14-curvo-1campata.ghx) |
| 15 | [Ponte curvo a due campate](15-curvo-2campate/README.md) | [GHX](15-curvo-2campate/15-curvo-2campate.ghx) |
| 16 | [Spina centrale a una campata](16-spina-1campata/README.md) | [GHX](16-spina-1campata/16-spina-1campata.ghx) |
| 17 | [Spina centrale a tre campate](17-spina-3campate/README.md) | [GHX](17-spina-3campate/17-spina-3campate.ghx) |
| 18 | [Spina centrale curva a due campate](18-spina-curva-2campate/README.md) | [GHX](18-spina-curva-2campate/18-spina-curva-2campate.ghx) |
| 19 | [Workflow acciaio misto con fasi e ritiro](19-flusso-acciaio-misto/README.md) | [GHX](19-flusso-acciaio-misto/19-flusso-acciaio-misto_REV003.ghx) |
| 20 | [Visualizzatore dei grafici](20-visualizzatore-grafici/README.md) | [GHX](20-visualizzatore-grafici/plot-preview.ghx) |
| 21 | [Cinque benchmark dei carichi mobili](21-benchmark-carichi-mobili/README.md) | [Istruzioni](21-benchmark-carichi-mobili/README.md) |
| 22 | [Fasi di sezione e benchmark del ritiro](22-fasi-sezione-gpc/README.md) | [Istruzioni](22-fasi-sezione-gpc/README.md) |

## Esecuzione e stato delle prove

Aprire una definizione con Rhino 8 e BridGHe installato. Verificare i pannelli dei percorsi prima di eseguire: i file generati su questa macchina puntano alla cartella locale del rispettivo esempio. Se si sposta il repository su un altro PC, aggiornare quei percorsi o rigenerare la definizione.

Le analisi Straus richiedono la licenza API. Gli esempi 11–18 sono stati riallineati alle fasi numerate usando risultati nativi già archiviati. Il nuovo ponte **19 REV003** e i benchmark **21** richiedono ancora il collaudo nativo descritto nei rispettivi README. Le revisioni archiviate del 19 non usano la nuova interfaccia.

- [Procedura delle fasi](../docs/FASI-E-AZIONI.md)
- [Guida Word](../docs/BridGHe_Guida_operativa.docx)
- [Benchmark 01–10 e formule](00-documentazione/benchmark-01-10.md)
- [Ipotesi dei ponti 12–18](00-documentazione/ponti-12-18.md)
- [Riepilogo della validazione delle fasi](00-documentazione/validazioni/numbered-phases.json)

Verifica della struttura e dei collegamenti, senza Straus: `python tools/validate_example_layout.py`. Riapertura delle 20 definizioni correnti: `dotnet run --project tests/BridGHe.Smoke -c Release -- --example-layout`.

I calcoli e i pacchetti nativi rimangono disponibili localmente ma sono esclusi da Git e dal pacchetto leggero del plugin. La generazione non deve creare una nuova cartella `examples` né distribuire calcoli di esempio in `artifacts`.

## Strumenti operativi 23–28

- [23 · Controllo del modello e copia di fase](23-audit-modello-fase/README.md)
- [24 · Inviluppi delle azioni e degli spostamenti](24-inviluppi-concomitanti/README.md)
- [25 · Fasi ordinate e combinazioni per azione](25-fasi-combinazioni/README.md)
- [26 · Riepilogo dei controlli GPC](26-riepilogo-verifiche-gpc/README.md)
- [27 · Incrementi di ritiro e confronto dei risultati](27-ritiro-confronti/README.md)
- [28 · Percorsi mobili paralleli](28-corsie-mobili/README.md)

## Geometria parametrica Simple Bridge 29–32

- [29 · Traversi pieni, rettilineo](29-simple-bridge-pieno/README.md)
- [30 · Traversi pieni, curva](30-simple-bridge-pieno-curvo/README.md)
- [31 · Traversi reticolari, rettilineo](31-simple-bridge-reticolare/README.md)
- [32 · Traversi reticolari, curva](32-simple-bridge-reticolare-curvo/README.md)

I quattro esempi generano geometria senza solutore: campate 30+40+30 m, tre travi, conci indipendenti dalle campate, giunti sfalsati, pendenza trasversale e appoggi geometrici sghembi. Ogni README contiene prove guidate e il report di soluzione/riapertura. [Guida ai componenti](../docs/SIMPLE-BRIDGE.md).

## Progettazione del modello 33–45

- [33 · Consigliatore delle campate](33-consigliatore-campate/README.md)
- [34 · Sezioni per progressiva e rastremazione](34-sezioni-per-progressiva/README.md)
- [35 · Assegnazione dei membri del ponte](35-assegnazione-membri/README.md)
- [36 · Appoggi orientati](36-appoggi-orientati/README.md)
- [37 · Connessioni e offset](37-connessioni-offset/README.md)
- [38 · Controventi longitudinali](38-controventi-longitudinali/README.md)
- [39 · Soletta strutturale e zone di getto](39-soletta-getti/README.md)
- [40 · Sequenza costruttiva per gruppi e conci](40-sequenza-costruttiva/README.md)
- [41 · Distribuzione di pioli e irrigidimenti](41-pioli-irrigidimenti/README.md)
- [42 · Frecce e controfreccia](42-frecce-controfreccia/README.md)
- [43 · Estrazione e conteggio delle storie per fatica](43-storie-fatica/README.md)
- [44 · Mappa delle verifiche locali](44-mappa-verifiche/README.md)
- [45 · Ponte curvo completo con tre fasi](45-ponte-completo-fasi/README.md)

Tredici definizioni GHX con soluzione e riapertura verificate senza solutore. [Procedura e limiti](../docs/BRIDGE-DESIGN.md).
