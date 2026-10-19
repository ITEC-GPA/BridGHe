# Grafici delle azioni e delle verifiche

## Confronto con la versione originaria

Il confronto è stato eseguito sui sorgenti originali dei due componenti di stampa delle azioni e delle tensioni e del relativo renderer. L'output non era identico a quello della prima riscrittura dei plot BridGHe.

| Aspetto | Versione originaria | BridGHe attuale |
|---|---|---|
| Uscite Grasshopper | Nessuna; solo salvataggio PNG | SVG testuale, percorso, minimo e massimo |
| File | PNG 1500×900; percorso richiesto dall'interfaccia | SVG o PNG; percorso opzionale |
| Progressive | Metri, riferimenti alla fine degli elementi | Metri, separatori degli elementi e griglia regolare |
| Inviluppo | Campitura grigia verso lo zero | Fascia grigia fra minimo e massimo effettivi di tutte le combinazioni |
| Estremi | Valori annotati sul grafico | Minimo e massimo dell'inviluppo annotati |
| Aspetto | Colori, impaginazione e watermark precedenti | Colori e impaginazione BridGHe; legenda sotto il grafico |

La prima riscrittura aveva omesso fascia d'inviluppo, separatori ed etichette degli estremi, usando inoltre progressive in millimetri. Questi aspetti sono stati corretti il 19 ottobre 2026. Non è una riproduzione pixel per pixel: l'altezza cresce con la legenda e i testi hanno lo stile BridGHe.

Le conversioni delle azioni sono le stesse: N e tagli in kN; torsione e momenti in kNm; tensioni in MPa; deformazioni adimensionali. L'indice delle sei azioni e delle tredici quantità originarie di tensione/deformazione è conservato. I grafici leggono i risultati esistenti e non ricalcolano la struttura.

La fascia grigia comprende tutte le combinazioni del modello, anche quando `Combinations` seleziona poche curve. `Show Min/Max Combo` aggiunge le combinazioni che governano gli estremi globali. Le uscite `Minimum` e `Maximum` mantengono il significato BridGHe: estremi delle curve selezionate, escluse le rette di riferimento. Per questo possono differire dai valori annotati sull'inviluppo globale.

Sono mantenute le correzioni numeriche già introdotte: nessun minimo fittizio pari a zero per risultati tutti positivi, stazioni ordinate, nessuno spostamento artificiale di 1 mm all'estremità, nessun collegamento attraverso dati mancanti o tra elementi. I tratti rettilinei collegano i campioni disponibili senza spline o smoothing. La fascia non è un inviluppo analitico continuo fra le stazioni né un nuovo inviluppo normativo.

Verifica riproducibile: `dotnet run --project tests/BridGHe.Smoke -c Release -- --regression bridge-plots`. Comprende conversioni, selezione delle combinazioni, inviluppo globale, lacune, salti, campione singolo, XML/Unicode e cultura numerica italiana.
