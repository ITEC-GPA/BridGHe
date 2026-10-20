# Dal tracciato al modello di ponte

I dodici nuovi componenti completano la geometria di Simple Bridge con assegnazione, connessioni, fasi e strumenti di lettura. Si trovano nella stessa tab **BridGHe**, nei gruppi Model, Attributes, Checks e Results secondo la funzione. Le quantità geometriche sono in **mm**, le proprietà e i risultati FEM in **N–mm–MPa**; i controlli gPC esistenti indicano separatamente gli ingressi in kN/kNm.

Partire dall'[esempio 45](../example/45-ponte-completo-fasi/README.md) per il flusso completo o dal [catalogo degli esempi](../example/README.md) per le prove 33–44. La [guida Word](BridGHe_Guida_operativa.docx) integra questa procedura con il verificatore per fasi già esistente.

## Scegliere le campate

**Bridge Span Advisor** riceve un tracciato aperto 3D e regioni chiuse orizzontali di esclusione degli appoggi. La ricerca usa la lunghezza del tracciato; l'esclusione usa la proiezione XY. Impostare luce minima, massima, obiettivo, griglia delle posizioni candidate e franco dalle regioni. L'inizio e la fine del tracciato sono obbligatori; Required Supports impone altre progressive.

La graduatoria minimizza:

`costo = somma[((luce − obiettivo locale) / obiettivo)^2] + penalità × numero di pile interne`

End Span Ratio modifica solo l'obiettivo delle campate terminali. Non introduce coefficienti normativi. La procedura cerca fino a dieci soluzioni migliori sulla griglia; griglia più fine significa più candidati e costo di ricerca maggiore. Il test automatico confronta la graduatoria con un'enumerazione esaustiva su un problema piccolo.

L'uscita contiene un ramo per alternativa: selezionare **un solo ramo** prima di collegarlo a Simple Bridge. Il franco controlla la distanza del punto di riferimento; per escludere l'intera fondazione occorre includerne l'ingombro. Un risultato vuoto significa nessuna soluzione sulla griglia corrente. Il componente non dimensiona sezioni, pile, fondazioni o stabilità: propone una distribuzione geometrica compatibile con i limiti scelti dal progettista.

## Creare e assegnare la geometria

1. Collegare tracciato, progressive degli appoggi e offsets trasversali a **Simple Bridge Solid/Truss**. Configurare **Bridge Segments**: i conci possono attraversare le pile e differire fra travi.
2. Usare l'uscita tipizzata **Bridge Geometry**. Conserva assi, nodi, progressive, sezioni trasversali, appoggi, conci e identità fisiche. È archiviabile nei GHX.
3. Aggiungere **Bridge Longitudinal Bracing** prima dell'assegnazione. X, V e Warren generano connessioni ai veri punti delle travi, che vengono divise quando necessario. Un offset normale non nullo aggiunge montanti fisici. Le X sono connesse solo se si intersecano effettivamente in 3D.
4. Collegare la geometria aggiornata a **Bridge Member Assignment**. Le proprietà default sono abbinate ai codici Families; `-1` si applica a tutte le famiglie. L'uscita Unassigned deve essere vuota.
5. Per cambi di sezione usare **Bridge Section Schedule** con famiglia, gruppo/trave, concio e intervallo di progressiva. Gruppo e concio `0` sono selettori generali. Le regole prevalgono sui default, ma non possono sovrapporsi fra loro su uno stesso elemento.

| Famiglia | Codice |
|---|---:|
| Trave longitudinale | 0 |
| Traverso pieno | 1 |
| Corrente superiore / inferiore | 2 / 3 |
| Montante / diagonale del traverso | 4 / 5 |
| Controvento longitudinale / montante di collegamento | 6 / 7 |

Gli ID FEM sono numerici. `Physical ID`, per esempio `Girder:2:1`, identifica il concio della famiglia e della trave, indipendentemente dalle suddivisioni della mesh. I gruppi gerarchici seguono la stessa struttura. Le scale di sezione X/Y definiscono rastremazioni dimensionali Straus; non sono applicabili alle sezioni generiche/omogeneizzate. I giunti di concio non impongono rilasci.

## Appoggi e collegamenti

**Bridge Bearings** produce nodi con vincoli/molle e sistema di riferimento esplicito. Il default locale usa X tangente orizzontale, Y verso sinistra e Z verticale. Orientation e Guide Angle permettono orientamenti diversi.

| Tipo | Traslazioni | Rotazioni |
|---|---|---|
| 0 Fixed translations | X/Y/Z bloccate | Libere |
| 1 Longitudinal guide | X libera, Y/Z bloccate | Libere |
| 2 Multidirectional | X/Y libere, Z bloccata | Libere |
| 3 Elastic | Rigidezze Kt in N/mm | Rigidezze Kr in N·mm/rad |

Il tipo 0 rappresenta un appoggio che blocca le traslazioni, **non un incastro rotazionale**. Non sovrapporre altri vincoli globali senza averne valutato l'effetto. La preview usa gli stessi assi assegnati al sistema UCS dell'export.

**Bridge Connections** offre tre rappresentazioni alternative:

- `0` link rigido spaziale nativo Straus: nodi e attributi di collegamento, senza travi di penalizzazione.
- `1` asta deformabile: proprietà Beam/Truss esplicita, con rigidezza reale.
- `2` offset di sezione: copia delle travi, offset locale X/Y impostato al valore fornito e risultati cancellati.

Gli estremi dei collegamenti devono coincidere con i vertici FEM previsti. Duplicati, link collassati e target ambigui vengono respinti. Collegare gli elementi in uscita a Build Model insieme agli altri elementi.

Quando questi elementi devono formare un solo modello, impostare **Flatten sull'ingresso Elements di Build Model**: i percorsi provenienti da catene GH di profondità diversa possono altrimenti produrre modelli distinti. L'esempio 45 imposta già questo comportamento e controlla che nessun elemento venga perso.

## Soletta con plate oppure sezioni omogeneizzate

**Bridge Deck Mesh** crea plate sulla superficie media, mantiene le colonne in corrispondenza delle travi e separa i getti alle progressive Pour Boundaries. Produce una nuova Bridge Geometry, divisa agli attacchi, da usare per l'assegnazione delle aste. Connection Lines va collegato a Bridge Connections scegliendo consapevolmente la meccanica della connessione. La sovrapposizione in viewport non crea connessioni.

Le mesh triangolari evitano quadrilateri sghembi. MembraneThickness e BendingThickness devono corrispondere allo spessore geometrico; la proprietà LoadPatch è trattata come non strutturale. I gruppi dei plate sono `Deck\Pour1`, `Deck\Pour2`, ecc.

Nel modello misto con **soletta strutturale shell**, assegnare alle travi proprietà di **solo acciaio**. Nel flusso a **sezioni omogeneizzate**, la rigidezza del calcestruzzo è già inclusa nelle proprietà delle travi: aggiungere shell strutturali può raddoppiarla. Bridge Model Audit segnala la possibile sovrapposizione. La scelta dipende dal modello fisico; non è una conversione automatica tra due schemi equivalenti.

## Fasi e storia

Creare una **Bridge Phase** per ciascuna fase, con Number, Name, Section State e Day. Collegarla a **Bridge Construction Sequence**, insieme al modello completo.

- Active Groups include i discendenti dei gruppi selezionati; Physical IDs aggiunge i membri richiesti. L'unione vuota dei selettori significa tutta la struttura.
- Active Bearing IDs seleziona i veri ID nodali del modello; `*` mantiene tutti gli appoggi. Enable Bearings=false rimuove vincoli e molle nodali.
- Girder Property sostituisce esplicitamente la proprietà delle travi longitudinali assegnate. Il nome della fase non modifica la rigidezza.
- Phase Model è una copia con elementi selezionati e risultati cancellati. Rimangono i carichi presenti sugli elementi attivi: controllare che siano gli incrementi voluti.

Uno snapshot non trasferisce forze, deformazioni, viscosità o tensioni pregresse. Per il flusso esistente: applicare gli incrementi corretti, analizzare ciascun modello, costruire Bridge Action/Bridge Shrinkage Action e ordinare le azioni nelle fasi del verificatore.

Native Stage è emesso solo per attivazione tramite gruppi rappresentabile con le proprietà e gli appoggi invariati, senza link rigidi e con Load Increments espliciti. Gli stage vanno collegati al **modello completo originale**, nell'ordine previsto. Negli altri casi l'uscita è intenzionalmente vuota e Information ne spiega il motivo.

Day è metadato, non un modello automatico di maturazione. Il ritiro conserva il flusso strain primario + eventuali effetti secondari FEM già implementato; non deriva dal nome della fase. Nell'esempio 45 le tre fasi illustrano l'attivazione geometrica, senza carichi o risultati inventati.

## Dettagli e lettura

**Bridge Detail Layout** usa direttamente `HSectionDimensions`, `BridgeStudOptions`, `BridgeIntermediateStiffener` e `BridgeSupportStiffener` di gPC. Distribuisce pioli e irrigidimenti, restituisce geometria e definizioni verificabili. Gli intervalli sono `[Start,End)` per non duplicare le file fra zone adiacenti. Steel Top Offset è la distanza dalla quota dell'asse al lembo superiore dell'acciaio: il componente non deduce un baricentro. Le geometrie dei dettagli non introducono automaticamente rigidezza FEM.

**Bridge Deflection & Camber** legge veri spostamenti nodali, identificati da modello/caso/step/trave. La freccia relativa sottrae la corda interpolata fra gli appoggi deformati di ogni campata. La controfreccia è `−fattore × spostamento scelto`. Display Scale cambia soltanto le curve. Il test analitico aggiunge cedimenti degli appoggi a una parabola nota.

**Bridge Fatigue History** accetta una storia numerica diretta oppure legge una storia FEM in ordine esplicito. Il rainflow mantiene le mezze escursioni residue. Il range equivalente soddisfa `Δeq^m = somma(nᵢ × Δᵢ^m) / Nref`. L'esponente e il numero di riferimento sono input; non sono scelti da una categoria normativa. I Response Factors trasformano linearmente le sei azioni in una risposta nelle unità dichiarate. Non vengono calcolati automaticamente S–N, danno, traffico annuo o effetti di sequenza non lineari.

**Bridge Check Map** associa i risultati gPC a ID asta, stazione, fase e caso. Grigio significa assenza di rapporti numerici; ambra presenza di controlli incompleti; rosso rapporto sopra 1. I massimi a pari merito conservano tutte le provenienze. La copertura della mappa dipende dai controlli forniti; le stazioni non campionate e gli stati limite non calcolati rimangono fuori dalla mappa.

## Controlli riproducibili

```powershell
# Genera i 13 GHX, risolve e riapre ciascun documento
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-design-examples
# Controlli senza riscrivere le cartelle degli esempi
dotnet run --project tests/BridGHe.Smoke -c Release -- --regression bridge-design-components
# Release, contratti, regressioni, baseline numerica e grafici
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1 -Smoke
# Richiedono licenza Straus API
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-bearings-native
dotnet run --project tests/BridGHe.Smoke -c Release -- --bridge-connections-native
```

Le regressioni comprendono ottimi discreti contro enumerazione esaustiva, identità dei conci, selettori e rastremazioni, topologia dei controventi, area della soletta, connessioni ai vertici, archivi GH, quantità dei pioli, volumi dei piatti, frecce note e cicli rainflow contati a mano. Il nuovo audit controlla inoltre copertura dell'assegnazione, assi locali modificati, conflitti degli appoggi, link e coerenza geometrica fra fasi.

Le chiamate native aggiunte sono `St7SetUCS` per gli assi degli appoggi e `St7SetRigidLink` per i collegamenti spaziali. Firma e costanti provengono dagli include ufficiali R3 installati; il wrapper è rigenerato da `tools/update_straus_bindings.py`. Il **20 ottobre 2026 entrambe le prove native sono superate**: verso UCS, vincoli, molle e connettività del link riletti dall'API. Questi due test verificano l'esportazione, senza avviare un solver; non costituiscono un'analisi strutturale dell'esempio 45. Il precedente blocco `St7Init -8` è risolto con la licenza disponibile.
