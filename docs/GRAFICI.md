# Grafici delle azioni e delle verifiche

## Pioli e irrigiditori: domanda, resistenza e WR

Il percorso è **Super Element Check → Bridge Detail Check → plot → Bridge Plot Preview**. Collegare al plot anche lo stesso **BridGHe Model** e la lista ordinata **Super-Element Beam** usati per la verifica. `Results` è l'uscita **Section result** di Detail Check, con percorsi `{beam;station}` e combinazioni nell'ordine del modello. Il disegno legge i risultati GPC: non modifica azioni, dettagli o resistenze.

**Bridge Stud Plot** conserva le quantità 0–4 e aggiunge:

| Quantity | Grafico | Unità |
|---|---|---|
| 4 | Rapporto forza per piolo / resistenza, con riferimento 1 | WR |
| 5 | PEd e PRd sovrapposti, per ogni combinazione | kN/piolo |
| 6 | Valore assoluto del flusso longitudinale e resistenza per lunghezza | N/mm = kN/m |

La quantità 4 è il rapporto resistente del piolo, non l'inviluppo di geometria, soletta e fatica. I controlli distinti restano in Verification Results. Per un cassoncino con contributo torsionale alla soletta usare la quantità 5: il solo flusso longitudinale totale non rappresenta la domanda sulla piattabanda più caricata, già inclusa nel PEd nativo GPC. La quantità 6 rifiuta quel caso.

**Bridge Intermediate Stiffener Plot** e **Bridge Support Stiffener Plot** sono in `13-Results`. Mantengono gli ingressi e le prime quattro uscite dei plot; `Quantity` diventa **Check**. Un'eventuale Value List collegata a Check si compila automaticamente con l'inviluppo e le singole voci. La selezione si conserva alla riapertura. Il secondo componente comprende anche i controlli del terminale e della seconda coppia, quando presenti nei risultati.

- **Check = 0 — Inviluppo di tutte le verifiche:** per ciascuna posizione e combinazione, `WRmax(s,C) = max_j WRj(s,C)` sulle voci applicabili del gruppo. Usa sempre WR, qualunque sia Display.
- **Check > 0:** selezione della singola verifica GPC. **Display** sceglie 0 WR, 1 domanda, 2 resistenza/limite, 3 confronto domanda–limite. Le unità provengono dalla verifica scelta: MPa, mm, mm² o valori adimensionali. Non si sommano né si inviluppano grandezze con unità diverse.
- **Stations:** progressive reali degli irrigiditori, in **metri dall'inizio della lista ordinata delle aste del superelemento**. Non sono coordinate globali né millimetri. Devono coincidere con stazioni di risultato, tolleranza assoluta `1e-6 m`; una progressiva senza risultati viene segnalata, senza interpolare. Ad un giunto sono conservati entrambi i lati disponibili. I grafici degli irrigiditori mostrano punti separati.
- **Governing checks:** verifica governante per posizione/combinazione e fase, con tutte le voci a pari WR massimo. Conserva i percorsi originari.
- **Unavailable checks:** voci prive di WR, dettaglio disattivato, verifica non applicabile o non disponibile; riporta anche le note GPC. Le posizioni senza anomalie hanno una stringa vuota, mantenendo la corrispondenza degli item.

Il WR è quello restituito da GPC, **non un rapporto domanda/resistenza ricostruito indiscriminatamente**. Nei controlli qualitativi GPC può assegnare valori convenzionali, ad esempio 0/2: non esprimono una riserva resistente fisica. Una voce con WR assente (`null`) o non finito rende incompleto l'inviluppo di quella posizione/combinazione: viene lasciata una lacuna, accompagnata da warning e nota nel grafico esportato. È comunque possibile scegliere una singola voce disponibile. Se nessun campione è disponibile il componente segnala l'errore e non produce un grafico apparentemente verificato. In SLE diversi controlli degli irrigiditori non hanno esito: occorrono le appropriate azioni SLU a monte, senza maggiorazioni implicite nel plot.

La linea **WR = 1** è un riferimento e non entra nelle uscite Minimum/Maximum. L'inviluppo sulle **voci di verifica** è distinto dalla fascia grigia sulle **combinazioni**. Nei confronti Ed/Rd la fascia grigia e le sue annotazioni riguardano Ed; le uscite Minimum/Maximum considerano entrambe le serie selezionate. Gli appoggi richiedono una reazione coerente assegnata nel verificatore: il plot non la ricava automaticamente dal FEM. Il massimo non include verifiche che la libreria non implementa o che non appartengono al gruppo scelto.

[Esempio 47 aggiornato](../example/47-pioli-irrigidimenti-verifiche/README.md): flusso integrato, Value List, preview e file di sola anteprima già pronti. Test dedicati: `dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression bridge-detail-plots`.

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

[Esempio già collegato](../example/20-visualizzatore-grafici/plot-preview.ghx), con due grafici sintetici selezionabili tramite slider. Generazione, salvataggio e riapertura: `dotnet run --project tests/BridGHe.Smoke -c Release -- --regression plot-preview --write-plot-example`. La regressione controlla rendering SVG, cache, cambio grafico, ridimensionamento, errori, input scollegato e rilascio delle risorse.
