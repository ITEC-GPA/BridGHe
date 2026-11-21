# BridGHe 0.6.0

Stazioni di verifica e sezioni notevoli. Sono le decisioni del 20 novembre 2026: Super Element Check su tutta la trave, Detail Check solo nelle sezioni notevoli, numero di stazioni regolabile.

Le librerie GPC sono le stesse della 0.5.0 (set ufficiale **BA** del 18 novembre 2026).

## Perché

Sul Ponte sul Pescara (226 m, 2 travi, 114 posizioni del mobile) la verifica completa è arrivata a 53 GB di memoria dopo 2 ore senza finire.
- Ogni asta da 1 m veniva verificata in 5 stazioni, tutte per ogni combinazione.
- Il Detail Check ripeteva la verifica, con pioli e irrigidenti, su tutte le stazioni.

Nel graticcio BridGHe i carichi arrivano alle travi ai nodi, quindi fra due nodi il momento è lineare: le stazioni intermedie non aggiungono massimi. I massimi del Detail Check cadevano tutti su appoggi e giunti dei conci.

| Pescara, 39 posizioni, definizione risolta | Tempo | Picco di memoria |
|---|---|---|
| 0.5.0: 5 stazioni, Detail su tutte | 294 s | 30,3 GB |
| 0.6.0: 2 nodi, Detail sulle sezioni notevoli | 167 s | 12,5 GB |
| 0.6.0 con 114 posizioni | 433 s | 30,0 GB |

Con 39 posizioni i massimi di trave della 0.6.0 sono gli stessi della 0.5.0, in tutte le verifiche del Super Element Check, del Detail Check e degli irrigiditori agli appoggi.

## Super Element Check Options (componente nuovo)

Raccoglie gli ingressi facoltativi che erano sul Super Element Check e aggiunge il numero di stazioni di analisi per asta.

| Ingresso | Significato | Default |
|---|---|---|
| Web / Top Flange / Bottom Flange Local Instability | instabilità locale; non usate se ci sono le Analysis Options | vero |
| Analysis Options | Bridge Analysis Options (norma, stato limite, Class 4, coefficienti) | — |
| ULS Combinations | combinazioni verificate allo SLU: nomi, o prefisso con * | — |
| Stations per Element | stazioni di analisi per asta, distribuite sull'asta: 2 = i due nodi, 3 = nodi e metà, 0 = tutte | **2** |

Con aste da 1 m la verifica ai 2 nodi è una verifica ogni metro, sui due lati di ogni nodo.

## Super Element Check

- Gli ingressi sono ora tre: BridGHe Model, Super-Element Beam e Options. Options accetta Super Element Check Options, oppure le sole Bridge Analysis Options.
- **Senza opzioni il check verifica i 2 nodi di ogni asta.** Con «Stations per Element» = 0 i risultati sono quelli della 0.5.0, bit per bit.
- I percorsi delle uscite {asta; stazione} tengono l'indice della stazione fra tutte le stazioni dell'asta, come li leggono i grafici e Bridge Girder Results: con 5 stazioni Straus e 2 di verifica i percorsi sono {i;0} e {i;4}.
- Una definizione salvata prima della 0.6.0 porta il vecchio WLI all'ingresso Options: dà un errore che dice di ricollegare gli ingressi a Super Element Check Options, invece di usare in silenzio i valori predefiniti.

## Bridge Notable Stations (componente nuovo)

Dà i punti in cui fare il Detail Check, da collegare a Bridge Detail Stations > Locations:
- i due estremi e la metà di ogni tratto a sezione costante di ogni trave;
- gli appoggi (ingresso Supports, per esempio Bridge Grillage Mesh > Support Points);
- il massimo momento positivo di ogni campata, dalla tensione della fibra inferiore nei risultati del Super Element Check.

Tutti i punti sono sui nodi delle travi, perché Bridge Detail Stations non interpola.

Con i risultati collegati, i punti sono quelli delle sole travi che i risultati coprono: un Detail Check alimentato dal Super Element Check di G2 riceve i punti di G2.

L'ingresso **Additional** tiene i punti degli altri selettori che leggono lo stesso Detail Check, per esempio gli irrigidenti. Un punto che coincide con una sezione notevole non viene ripetuto.

L'uscita **Guard** segnala i tratti dove il taglio o il flusso dei pioli del Super Element Check sono più alti lontano dai punti scelti: sono le grandezze delle verifiche che fa solo il Detail Check.

## Stazioni Straus7

Il numero di stazioni di uscita di Straus7 si sceglie con il componente esistente **Beam Output Stations**. Nel template 50 è dopo Bridge FEM Members, con un cursore a 5, il valore di prima.

## Esempi

- **50 REV005** (da REV004) e **99 REV006** (da REV005), le nuove definizioni correnti del catalogo:
  - Super Element Check Options con le instabilità di prima e 2 stazioni per asta;
  - Beam Output Stations a 5 dopo i Bridge FEM Members;
  - i Detail Check alimentati dal Super Element Check sulle sezioni notevoli, con i punti degli irrigiditori; gli irrigiditori leggono da lì;
  - nel 50 i grafici di tensioni e taglio leggono dal Super Element Check.
- **Tutte le altre definizioni del catalogo con un Super Element Check** (11–19, 47, 48, 50 REV001–REV004 e base, 99 REV002, REV004, REV005) sono migrate solo nei collegamenti: Super Element Check Options con le sorgenti di prima e **tutte le stazioni**. I loro risultati restano quelli della 0.5.0. Le modifiche sono in `example/migrazione-060.json`.
  - I file restano quelli della 0.5.0 byte per byte: è sostituito solo il Super Element Check, e in fondo sono aggiunti le opzioni e il cursore delle stazioni.
  - Così i dati salvati nei formati precedenti, che le prove di lettura usano, restano com'erano.
- Comandi del runner per le definizioni dell'utente fatte dal template 50 (le sorgenti dei vecchi ingressi sono lette dall'archivio):
  - `--example-stations migrate-wiring <in.ghx> <out.ghx>`: solo i collegamenti, risultati invariati;
  - `--example-stations migrate <in.ghx> <out.ghx>`: la regola completa (2 nodi, Beam Output Stations, sezioni notevoli).
  - Su una definizione già migrata il comando non ripete i passi già fatti.

## Prove

- Nativi con Straus7 (`Fixtures/native-gate-060`), confrontati con la 0.5.0:
  - 50 REV005: 16.776 righe invece di 68.184, in 2 minuti invece di 20;
  - 99 REV006: 5.952 righe invece di 15.004;
  - in tutti e due: utilizzo massimo, rapporto massimo delle verifiche locali e verifiche oltre 1 uguali, nello stesso punto.

- `slab-reinforcement`:
  - il predefinito a 2 nodi dà le due stazioni di estremità della verifica su tutte le stazioni, bit per bit;
  - 3 stazioni danno nodi e metà; 1 è rifiutata;
  - le combinazioni SLU dentro le opzioni;
  - il componente di opzioni.
- `bridge-notable-stations`, nuova: stazioni per asta, catena della trave, estremi e metà dei tratti, appoggi, massimi di campata, controllo su taglio e flusso.
- Le prove che costruiscono definizioni nel codice collegano le opzioni con tutte le stazioni, per tenere i risultati di prima.
