# Allineamento delle stazioni FEM nel merge

## Errore dell'esempio 19

Il merge richiedeva un campione originale di ogni azione in ciascuna stazione dell'unione delle griglie. Questo vincolo era errato: `St7GetBeamResultArray` restituisce almeno il numero di stazioni richiesto e può aggiungere stazioni per catturare massimi e minimi. È documentato nel manuale API R3 installato, pp. 734–735 (pagine PDF 742–743).

Nei risultati dell'esempio 19, asta 29, lunghezza 2500 mm:

| Azione | Stazioni originali [mm] |
|---|---|
| G1 | 0, 625, **835.556242556**, 1250, 1875, 2500 |
| G2 | 0, 625, **879.044933748**, 1250, 1875, 2500 |
| Q | 0, 625, **977.735583232**, 1250, 1875, 2500 |
| RITIRO | 0, 625, 1250, 1875, 2500 |

La stazione segnalata dall'errore proviene da G2; non manca un risultato dovuto di G1. Sono analisi diverse con griglie interne legittimamente diverse.

## Metodo adottato

`Build BridGHe Model` associa le aste mediante `BeamId` e controlla estremi, orientamento, offset e unità. Converte le ascisse normalizzate in distanze fisiche prima di costruire l'unione delle griglie, con tolleranza `max(GPC.Geometry.GeometryBase.Tolerance, L × 10⁻¹²)`.

Per ciascuna azione:

1. I campioni originali sono ordinati una sola volta.
2. I valori FEM esistenti sono copiati senza modificarne le sei componenti; solo ascisse coincidenti entro tolleranza vengono allineate.
3. Ogni stazione nuova è calcolata dai due campioni **originali** adiacenti del medesimo caso e della medesima asta.
4. L'algoritmo percorre in ordine le stazioni; non cerca ripetutamente in tutta la griglia e non usa punti sintetici per costruire quelli successivi.

Siano `a`, `b` le distanze dei due campioni, `h = b − a`, `t = (x − a)/h`. Per N, V1, V2 e T:

```text
F(x) = (1 − t) Fa + t Fb
```

Per ciascuna coppia di diagrammi Straus `(M1, V1)` e `(M2, V2)`:

```text
M(x) = (1 − t) Ma + t Mb + h (Va − Vb) t (1 − t) / 2
```

Il termine parabolico deriva dalla variazione lineare del taglio, con convenzione `dM/dx = V`. Il raccordo passa esattamente per **entrambi** i momenti FEM. Quando `Mb − Ma = h(Va + Vb)/2`, coincide con l'integrazione del taglio lineare:

```text
M(x) = Ma + Va (x − a) + (Vb − Va) (x − a)² / (2h)
```

Questa interpolazione è esatta per tagli lineari e momenti quadratici, come nei tratti a carico trasversale uniforme dell'esempio 19. Per diagrammi più complessi rimane un'approssimazione locale: aumentare le stazioni e verificarne la convergenza. Non viene dichiarata esatta per carichi variabili, reazioni distribuite o risposte non polinomiali.

La diagnostica riporta quanti campioni sono stati interpolati. Le porte dei componenti e le proprietà dei modelli sorgenti rimangono invariate.

## Controlli e limiti

- Devono essere presenti i risultati a entrambi gli estremi di ciascuna asta selezionata, per ogni azione. Nessuna estrapolazione o sostituzione di risultati mancanti con zero.
- Risultati duplicati alla stessa ascissa, o diversi step raccolti sotto lo stesso caso, vengono rifiutati: non si sceglie arbitrariamente un lato del salto o un incremento.
- Un'interpolazione che attraversa o tocca un carico concentrato interno noto viene bloccata. Suddividere l'asta nel punto di applicazione per mantenere i valori sui due lati come estremi di aste distinte. Il controllo comprende i casi attivi di una combinazione sorgente.
- Un salto non rappresentato né nei campioni né nei carichi del modello non può essere ricostruito dai soli risultati. Occorre conservarne i due lati o suddividere l'asta.
- Gli zeri restano ammessi per le aste esplicitamente escluse da un'azione tramite `Beam IDs`.
- Il ricampionamento conserva gli estremi catturati da ciascuna analisi; non è una ricerca continua degli estremi di ogni combinazione finale.

## Difetti riscontrati nel metodo precedente

| Riscontro nel codice precedente | Conseguenza | Gestione attuale |
|---|---|---|
| Calcolava la distanza da `NormalizedLength`, ma costruiva il risultato con `force.Station` e `NormalizedLength = false`. | Un'ascissa 0.5 su un'asta da 10000 mm diventava 0.5 mm invece di 5000 mm. | Conversione unica prima dell'allineamento; test con griglie normalizzate e assolute. |
| Associava le aste mediante indice della lista. | Un diverso ordinamento poteva associare sollecitazioni a un'altra asta. | Matching per ID e controllo geometrico, già presenti nel nuovo workflow; ora testati anche con griglie diverse. |
| Integrava il momento soltanto a partire dal punto sinistro. | Se i momenti FEM non chiudevano l'integrale del taglio lineare, il tratto ricostruito non raccordava il momento destro. Per esempio, V nullo e momento linearmente variabile per una coppia distribuita. | Parabola vincolata ai due momenti originali. |
| Non controllava esplicitamente i campioni agli estremi. | Riferimenti nulli oppure intervalli nulli durante la ricerca dei punti adiacenti. | Errore esplicito prima del ricampionamento; nessuna estrapolazione. |
| Inseriva i campioni in una cella unica per caso/ascissa, sovrascrivendo eventuali duplicati. | Perdita silenziosa di un lato di una discontinuità o di uno step. | Duplicati e step multipli rifiutati. |
| Usava `Math.Round(..., 15)` e confronti esatti in `HashSet`/`IndexOf`. | Ascisse numericamente quasi coincidenti potevano creare intervalli molto piccoli e duplicati spuri. | Tolleranza dimensionale, controllo di ambiguità e griglia canonica. |
| Riallocava la matrice delle stazioni per ogni modello e ripeteva `ToList`, `IndexOf` e ricerche in avanti. | Allocazioni e scansioni inutili. | Ordinamento per caso e cursore monotono sui soli campioni originali. |

Il riuso del precedente punto interpolato non era di per sé errato per diagrammi esattamente lineari/quadratici coerenti; aumentava però la dipendenza da dati sintetici e gli arrotondamenti accumulati. Il metodo attuale evita questa ricorsione.

## Validazione eseguita e riproduzione

Il test nativo rilegge gli input e gli `.LSA` già calcolati dell'esempio 19, poi richiede a Straus i risultati direttamente all'unione delle stazioni con `St7GetBeamResultArrayPos`. Confronta le sei componenti su tutte le 98 aste dei quattro modelli.

- **12.192 valori confrontati**.
- Massimo scarto sui momenti: **9.53674316 × 10⁻⁷ N·mm**.
- Il file GHX corrente, alimentato con questi risultati, produce **378 risultati di sezione**, senza componenti in errore.
- Gli hash degli input, dei modelli Straus e dei risultati vengono controllati prima e dopo: il test non modifica i file nativi e non avvia nuove analisi.
- La fixture dell'asta 29 conserva i campioni originali e quelli richiesti direttamente a Straus, per ripetere la regressione anche senza licenza.
- Tolleranze del confronto nativo: 10⁻⁵ N sulle forze e 10⁻² N·mm sui momenti; soglie assolute anche vicino allo zero.

Il report riproducibile è [validation-stations.json](../example/19-flusso-acciaio-misto/validation-stations.json).

```powershell
dotnet build tests/BridGHe.Smoke -c Release
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression bridge-stations
# Richiede API/licenza e i file .st7/.LSA dell'esempio 19:
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --workflow-stations-native
```

`BridgeWorkflowBuilder.Build` mantiene la propria firma; cambia la gestione delle griglie diverse. La sola nuova dichiarazione API è `St7GetBeamResultArrayPos`, usata per la validazione indipendente. Il merge rimane utilizzabile offline e non apre Straus.
