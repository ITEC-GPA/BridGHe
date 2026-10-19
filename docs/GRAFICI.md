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

L'asse verticale ha tacche ancorate allo zero, con passi della serie 1–2–5 per potenze di dieci: ad esempio 5, 10, 20, 50, oppure 0.005 per valori piccoli. Lo zero ha sempre la propria etichetta sulla linea di riferimento. Il passo dipende dall'ampiezza dei risultati: un massimo di 14.754 può avere tacche 0, 5, 10, 15. SVG, PNG e preview usano la stessa griglia; campioni, inviluppo, annotazioni degli estremi e uscite numeriche conservano i valori originali.

Sono mantenute le correzioni numeriche già introdotte: nessun minimo fittizio pari a zero per risultati tutti positivi, stazioni ordinate, nessuno spostamento artificiale di 1 mm all'estremità, nessun collegamento attraverso dati mancanti o tra elementi. I tratti rettilinei collegano i campioni disponibili senza spline o smoothing. La fascia non è un inviluppo analitico continuo fra le stazioni né un nuovo inviluppo normativo.

Verifica riproducibile: `dotnet run --project tests/BridGHe.Smoke -c Release -- --regression bridge-plots`. Comprende conversioni, selezione delle combinazioni, inviluppo globale, lacune, salti, campione singolo, XML/Unicode e cultura numerica italiana.

## Preview sul canvas di Grasshopper

Inserire **BridGHe → 15-Preview → Bridge Plot Preview** e collegare `SVG` del plot a `SVG` del visualizzatore. Non serve compilare `File Path`. Il bottone **Draw** del plot aggiorna il grafico; con `Keep Running = true` il plot si aggiorna a ogni nuova soluzione e la preview lo segue.

- `SVG`: testo SVG da uno o più plot. La lista viene appiattita.
- `Index`: indice del grafico da mostrare, a partire da 0; default 0.
- `Width`: larghezza sul canvas, da 240 a 1600 pixel; default 600. Le proporzioni sono conservate e l'altezza è limitata a 900 pixel.
- Uscita `SVG`: testo selezionato, invariato.

Doppio clic sul grafico o menu contestuale **Apri grafico** apre una finestra che segue gli aggiornamenti, con adattamento alla finestra e zoom 100%/200%. La rasterizzazione è mantenuta in cache finché non cambiano SVG o dimensioni. Dati mancanti o non validi cancellano la preview precedente. Immagini e finestra vengono rilasciate alla rimozione del componente/chiusura del documento.

Il visualizzatore accetta i grafici SVG autonomi di BridGHe: niente browser, script, risorse esterne o file temporanei. Per l'esportazione vettoriale resta disponibile lo stesso SVG originale. La preview usa **SVG.NET 3.4.8** e **ExCSS 4.2.3**, incluse nella Release: il renderer SVG nativo di Rhino ha restituito immagini nulle nei test e non è usato. [API del renderer](https://svg-net.github.io/SVG/api/Svg.SvgDocument.html); licenze distribuite in `docs/licenses/` (MS-PL e MIT).

[Esempio già collegato](../examples/plot-preview/plot-preview.ghx), con due grafici sintetici selezionabili tramite slider. Generazione, salvataggio e riapertura: `dotnet run --project tests/BridGHe.Smoke -c Release -- --regression plot-preview --write-plot-example`. La regressione controlla rendering SVG, cache, cambio grafico, ridimensionamento, errori, input scollegato e rilascio delle risorse.
