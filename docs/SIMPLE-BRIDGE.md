# Geometria del ponte e gestione dei conci

L’interfaccia attuale è descritta nel [flusso modulare](FLUSSO-MODULARE.md). Il motore geometrico resta condiviso: Bridge Support Lines, Girder Axes, Slab Layout, Fabrication Segments e Solid/Truss Diaphragms entrano in Geometry Assembly; Bridge Grillage Mesh esegue la discretizzazione con Mesh Settings. I vecchi generatori Simple Bridge sono nascosti.

## Convenzioni geometriche e unità

Tutte le lunghezze, comprese le coordinate della curva, sono in mm. Non viene eseguita una conversione implicita dalle unità del documento Rhino: una curva lunga 100 unità viene interpretata come lunga 100 mm. Scalare esplicitamente una geometria costruita in metri.

Le progressive sono lunghezze d'arco sulla curva di riferimento, non parametri UV né distanze misurate sulle singole travi in curva.

Guardando nel verso del tracciato, l'offset positivo è verso sinistra. Le larghezze vengono fornite nell'ordine **sinistra, destra** e sono misurate lungo la sezione trasversale inclinata. Le quote delle travi sono distanze lungo la normale della soletta; devono essere negative e inferiori all'intradosso. Una singola quota si applica a tutte le travi; una lista assegna una quota per trave.

Le quote definiscono assi geometrici scelti dall'utente. Non si deduce il baricentro da una sezione ancora non assegnata. Per il successivo modello FEM occorre verificare assi di riferimento, eccentricità e collegamenti alla sezione reale.

L'uscita `Frames` contiene piani con X verso sinistra, Y normale alla soletta e Z tangente al tracciato. Il riferimento deriva dalla verticale globale e dalla tangente, evitando il ribaltamento del riferimento di Frenet nei flessi.

## Campate e appoggi sghembi

`Support Stations` deve contenere almeno due progressive univoche e crescenti. Sono ammessi tratti del tracciato oltre la prima e l'ultima linea di appoggio: vengono conservati come sbalzi.

`Skew Angles` accetta un angolo oppure un angolo per appoggio. Zero significa linea trasversale; l'angolo positivo ruota la linea trasversale verso la tangente, nel riferimento locale. Il modulo deve essere inferiore a 75°.

Il generatore trova l'intersezione della linea/piano sghembo con ciascun asse longitudinale. Queste progressive possono differire fra le travi. I traversi sulle linee di appoggio seguono lo stesso sghembo.

**Per estremità sghembe fornire un tracciato sufficientemente esteso**: se la linea non interseca una trave all'interno del tracciato, il componente si ferma con un messaggio. Non vengono inventati prolungamenti. Le estremità della soletta rimangono quelle del tracciato, trasversali; il componente non rifila automaticamente la soletta sulla linea sghemba.

I punti restituiti sono posizioni geometriche: non bloccano traslazioni o rotazioni.

## Conci indipendenti da campate e discretizzazione

`Bridge Fabrication Segments` ha due ingressi:

| Ingresso | Significato |
|---|---|
| `Joints` | Albero di progressive dei giunti, in mm. |
| `Maximum Length` | Massima lunghezza automatica del concio, quando non sono assegnati giunti espliciti alla trave. Zero indica un unico concio. |

I percorsi di `Joints` sono:

```text
{0}  12000, 24000, 36000, 48000, 60000, 72000, 84000   giunti comuni
{2}  18000                                           giunto aggiuntivo solo su G2
```

Le travi sono numerate da 1 nell'ordine di `Transverse Offsets`, quindi G1 è quella più a destra. I giunti specifici si aggiungono a quelli comuni. Se alla trave si applica almeno un giunto esplicito, `Maximum Length` non aggiunge altri giunti a quella trave. In modalità automatica si suddivide l'intera lunghezza in conci uguali, entro il massimo assegnato.

Un concio fra 24 e 36 m attraversa la pila a 30 m. Il generatore introduce comunque il nodo necessario all'appoggio e conserva lo stesso identificativo di concio sui tratti appartenenti alle due campate.

La griglia geometrica comune comprende tutti i giunti, i punti di attacco dei traversi e gli appoggi. Perciò un giunto specifico di G2 può introdurre un **nodo geometrico** anche su G1/G3, ma non divide i loro **conci**. Questo permette collegamenti coerenti senza confondere il pezzo di carpenteria con l'elemento discretizzato.

Un giunto non introduce automaticamente una cerniera, un nodo duplicato o una fase di montaggio. La numerazione dei conci resta distinta dalle `Bridge Phase` del calcolo.

## Traversi pieni e reticolari

`Bridge Truss Diaphragms / Bridge Solid Diaphragms` è comune ai due generatori.

| Ingresso | Uso |
|---|---|
| `Stations` | Progressive esplicite delle linee dei traversi. Se presenti, sostituiscono il passo automatico. |
| `Maximum Spacing` | Passo massimo per suddivisione uniforme di ogni campata/sbalzo. Zero disabilita la suddivisione automatica. |
| `At Supports` | Aggiunge i traversi sugli appoggi. False li esclude dalla griglia automatica; una stazione assegnata esplicitamente viene comunque rispettata. |
| `Truss Height` | Altezza totale del reticolo centrato sugli assi longitudinali, in mm. |
| `Panels per Bay` | Numero di pannelli fra due travi adiacenti. |
| `Pattern` | 0 = X, 1 = V, 2 = Warren. |
| `Connect X` | True suddivide le due diagonali nel punto d'incrocio, generando un nodo comune. |

Per i traversi pieni vengono usate solo le posizioni: un asse collega ogni coppia di travi adiacenti alle rispettive quote.

Per il reticolo si generano:

- correnti superiori e inferiori;
- montanti d'estremità suddivisi in corrispondenza dell'asse della trave, per un collegamento geometrico effettivo;
- montanti interni per X e V;
- diagonali doppie per X, convergenti sul corrente inferiore per V, alternate per Warren;
- nessun montante interno per Warren.

I montanti sono aste geometriche: non sono automaticamente link rigidi o elementi con una determinata rigidezza. Per X non collegata, il solo incrocio visivo non costituisce un nodo; un successivo remeshing indiscriminato potrebbe cambiare questa scelta.

Il corrente superiore deve rimanere sotto la soletta. Le intersezioni locali con flange, irrigidimenti e piastre reali non sono definite finché non vengono assegnate geometrie di dettaglio.

## Pendenza trasversale

`Crossfall` può essere lasciato vuoto, con pendenza zero. Accetta:

- un singolo valore, per esempio `2` = 2%;
- un albero con una coppia `progressiva, pendenza %` per ramo:

```text
{0}  0,      -2
{1}  50000,   2
{2}  100000,  3
```

Le progressive devono crescere e coprire l'intero tracciato. La pendenza varia linearmente fra le sezioni assegnate. Un valore positivo solleva il bordo sinistro.

## Uscite e collegamento al modello BridGHe

`Members` contiene tutte le curve, ordinate nell'albero `{famiglia;gruppo;concio o baia}`. `Member IDs` ha esattamente la stessa struttura e lo stesso ordine.

| Famiglia | Elementi | Gruppo e terzo indice |
|---|---|---|
| 0 | Longitudinali | trave, concio |
| 1 | Traverso pieno | linea traverso, baia |
| 2 | Corrente superiore | linea traverso, baia |
| 3 | Corrente inferiore | linea traverso, baia |
| 4 | Montanti | linea traverso, trave d'attacco o baia interna; ID univoco per ogni asta |
| 5 | Diagonali | linea traverso, baia |

`Longitudinals` usa percorsi più semplici `{trave;concio}`. `Diaphragm Members` contiene tutte le famiglie diverse da zero. Gli indici di travi, conci, campate e nodi sono a base 1.

Per costruire il modello successivamente:

1. Selezionare una famiglia o un gruppo di curve.
2. Assegnare la proprietà voluta con `Beam`.
3. Applicare gli ID corrispondenti con `Element ID`, conservando gli stessi percorsi.
4. Assegnare eventualmente il `Super Element` alla singola trave e le altre proprietà necessarie.
5. Assemblare con `Build Model`, scegliendo unità N-mm.

L'orientamento delle sezioni non viene assegnato dalle sole curve: usare i riferimenti geometrici restituiti per impostare coerentemente gli assi locali, soprattutto in presenza di pendenza trasversale e tracciato 3D.

Non appiattire curve e ID in modi diversi. Gli identificativi sono deterministici per gli stessi input; se cambia la discretizzazione, possono cambiare gli ID dei segmenti di asta. Tutti i modelli delle fasi devono derivare dalla stessa geometria di base.

`Topology` espone ID, famiglia, gruppo, concio/baia, campata, nodi iniziale/finale e progressive. Per i traversi, Span=0 indica “non assegnato a una campata”; per i longitudinali Span=0 e Span=numero di appoggi identificano gli sbalzi prima e dopo gli appoggi.

## Discretizzazione, preview e campo geometrico

`Maximum Element Length` limita le corde longitudinali, comprendendo le travi esterne in curva e i bordi della soletta. `Chord Tolerance` controlla lo scostamento della geometria esatta ai punti 1/4, 1/2 e 3/4 di ciascun intervallo. Vengono conservati anche i limiti degli span NURBS. È un controllo adattivo geometrico campionato, non una prova di convergenza FEM o un limite analitico globale su qualsiasi curva arbitraria.

La preview colora i conci longitudinali e distingue le famiglie dei traversi, con soletta trasparente. `Labels` mostra giunti e appoggi. Disattivando la preview del componente si nasconde l'intera geometria; i suoi output possono essere usati separatamente.

Sono rifiutati tracciati chiusi, spigoli nella tangente, tangenti verticali, offset localmente ripiegati, lunghezze degeneri e input non finiti. Il generatore non è un rilevatore generale di autointersezioni lontane lungo un tracciato arbitrario, né gestisce pile, spalle o dettagli di carpenteria.

## Esempi e test

| Cartella | Configurazione |
|---|---|
| [29](../example/29-simple-bridge-pieno/README.md) | Rettilineo, traversi pieni, appoggi interni sghembi. |
| [30](../example/30-simple-bridge-pieno-curvo/README.md) | Curva circolare R=250 m, traversi pieni. |
| [31](../example/31-simple-bridge-reticolare/README.md) | Rettilineo, reticolo a X con incroci non collegati. |
| [32](../example/32-simple-bridge-reticolare-curvo/README.md) | Curva R=250 m, reticolo Warren. |

Tutti hanno campate 30+40+30 m, tre travi, pendenza trasversale variabile, giunto sfalsato su G2 e un concio che attraversa una pila.

```powershell
dotnet build tests/BridGHe.Smoke -c Release
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression simple-bridge
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression simple-bridge-components
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression bridge-deck-grillage
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression reinforcement-material
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression gh-node-merge
# Rigenera i quattro GHX nelle cartelle 29–32: salvare prima eventuali modifiche personali.
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --simple-bridge-examples
```

I test comprendono volume analitico della soletta, coordinate concentriche in curva, sghembo analitico, pendenze, limiti delle corde, collegamenti e assenza di aste duplicate, conci attraverso le pile, tutti gli schemi reticolari, incroci collegati/separati, input non validi, archiviazione delle configurazioni, abbinamento curve/ID e soluzione/riapertura dei quattro GHX.

I test del graticcio controllano anche l'ortogonalità degli assi trasversali, le quote sulla normale locale, la coincidenza esatta degli attacchi con i nodi longitudinali, le larghezze tributarie e l'assegnazione esplicita della famiglia 8. Il test di unione dei nodi verifica che appoggi e link sopravvivano in entrambi gli ordini di ingresso a `Build Model`, senza perdere i carichi. L'[esempio 99 REV004](../example/99-Ponte%20completo%20GPC/README.md) completa il percorso con quattro analisi Straus, verifiche e report.
