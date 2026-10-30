# Esempio 99 dal file personale alle verifiche

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

## Stato corrente della REV004 al 25 ottobre 2026

Ritiro allineato a **εcs = −0,0003171439341670765** (circa −317 µε), calcolato dagli input attuali. Il generatore FEM e l’azione primaria usano la stessa uscita; `Actions!I4` nell’Excel è aggiornato e tutti i coefficienti delle combinazioni sono conservati.

Le quattro cache FEM erano riferite a geometrie precedenti (37/76/76/76 aste, contro 73/112/112/112 attuali) e sono state invalidate. Input, geometria e collegamenti personali sono conservati. Il GHX si riapre e il template viene letto, ma occorre **ricalcolare tutte le quattro analisi Straus**, poi Read Excel → Build → Check → Generate report. ST7/LSA, report Word e validazioni numeriche precedenti sono storici; non verificano la configurazione corrente. Vedere [validazione dell’allineamento](REV004/alignment-validation.json).

Il verificatore usa N–Mx–Vy. T, M2 e V1 sono trascurati nelle verifiche e conservati nei risultati FEM; l’ipotesi è dichiarata anche nel report.

## Configurazione e prove precedenti


## Revisione corrente 004 con soletta a graticcio

Aprire [REV004](REV004/flusso-acciaio-misto_REV004.ghx). Include la soletta trasversale di **Bridge Grillage Mesh con diaframmi pieni**, i collegamenti eccentrici e **Reinforcement Steel B450C**. Le revisioni precedenti restano disponibili sotto; gli input della REV003 salvata dall’utente sono stati conservati nella migrazione dei componenti.

### Input espliciti sul canvas

La REV004 aggiornata espone i 480 ingressi precedentemente memorizzati nei componenti: 195 slider, 126 toggle, 61 Value List, 88 pannelli e 10 parametri tipizzati per stringhe vuote o dati generici. Ogni controllo è vicino al componente che alimenta, nel gruppo `Input | …`, e riporta la descrizione della variabile. Sono stati rinominati anche i controlli preesistenti collegati: per esempio **Spessore soletta [mm]**, **Umidità relativa [%]**, **Passo longitudinale pioli [mm]** e **Età attivazione misto [giorni]**. I tooltip conservano la descrizione completa dell'ingresso.

Le Value List mostrano i nomi delle opzioni. I pannelli contengono testi, percorsi, vettori e liste ordinate; i valori numerici che uno slider arrotonderebbe restano nei pannelli. I parametri marcati `(vuoto)` forniscono una stringa vuota. Gli ingressi opzionali senza dati rimangono scollegati quando indicano assenza di filtro, selezione o override. I pulsanti **Run/Generate** restano sui componenti: l'aggiornamento non abilita l'esecuzione automatica di Straus o la scrittura del report.

L'opzione **Considera calcestruzzo nelle fasi miste** è ora esplicitamente `True`: nel precedente GHX era presente un posto vuoto, interpretato già come `True` dal componente. Il controllo numerico conferma gli stessi risultati. Non vengono modificati gli Excel, i file Straus o il report: [validazione degli input espliciti](REV004/explicit-inputs-validation.json).

### Pulizia del 30 ottobre 2026

Sono stati tolti 37 oggetti da cui non si arrivava a nessun risultato:

- comandi scollegati: `CALCOLA 4 MODELLI STRAUS`, `Tipo collegamento eccentrico`, le Value List `Results` e `Super-Element` e tre Value List senza nome;
- gli input dei traversi reticolari (altezza, pannelli per campo, schema, incrocio delle diagonali), che con i diaframmi pieni non alimentavano niente;
- tre `Build Model` senza destinatari, con i loro nomi vuoti e sistemi di unità;
- un `Load Case` isolato, con il suo nome e la gravità sulle masse non strutturali;
- il ramo `Super Element` → tre `Super Element Filter` → due `Merge`, senza uscita, con i pannelli `ID trave da selezionare`, un `List Length`, un `Series` con i suoi due slider e il pannello `Nome proprietà`.

I gruppi rimasti vuoti sono eliminati. La disposizione dei gruppi `Input | …` non cambia: il flusso andava già da sinistra a destra, e sono stati spostati soltanto gli oggetti dei 5 collegamenti in cui la sorgente sporgeva di poco oltre il bordo sinistro del destinatario. Il contenuto funzionale di tutti gli altri oggetti è identico. I conteggi del paragrafo precedente si riferiscono alla generazione del 22 ottobre. Dettagli in [tidy-validation.json](REV004/tidy-validation.json); strumento `--tidy-example 99`.

Prova nativa dopo la pulizia (`--modular-bridge-native`, set GPC del 30 ottobre pomeriggio): quattro analisi Straus e 2.736 righe di verifica GPC, con impronta identica bit per bit a quella di riferimento (`8E9756B76EC3EFFE…`).

Il generatore `--example99 rev004` produce già questa configurazione. `--example99 inputs004` aggiorna una REV004 esistente e verifica valori, alberi, riapertura e corrispondenza con il report salvato; se è già aggiornata, la controlla senza riscriverla. Questa revisione richiede anche la Release aggiornata del plugin: è stato corretto il caricamento degli schemi dei carichi distribuiti quando il tipo proviene da un controllo esterno.

### Geometria e fasi della revisione

- Geometria: due campate 12+12 m, due travi a interasse 3 m, soletta 5 m × 300 mm. Le 13 linee trasversali ogni 2 m sono suddivise in 39 aste: sbalzo destro 1 m, tratto fra travi 3 m, sbalzo sinistro 1 m. Asse della soletta a quota −150 mm; 26 link rigidi agli assi delle travi, quota −1359.318631 mm.
- G1 mantiene 37 aste di acciaio e 26 nodi. G2, Q e ritiro hanno 76 aste, 78 nodi e 26 link. Tutte le fasi conservano i sei appoggi. Gli assi longitudinali sono già suddivisi alle progressive della soletta.
- Le sezioni delle fasce sono 2000×300 mm internamente e 1000×300 mm alle estremità. Le larghezze si aggiornano dall'uscita geometrica per passo uniforme; per progressive non uniformi occorre assegnare ciascuna larghezza tributaria. Il materiale FEM trasversale usa `Ec,eff = Es/n` della stessa fase.
- Il B450C alimenta `Reinforcement Material` della proprietà mista e quindi entrambe le file definite nella soletta. Non sostituisce S355 nella carpenteria.
- `Additional Phase Members = true` in **Build BridGHe Model** verifica le aste del primo modello di riferimento, G1. Le aste cls aggiuntive restano nelle sorgenti FEM complete utilizzate anche per le reazioni; non entrano nel checker delle travi longitudinali.

Le quattro analisi sono state eseguite con Straus7 R3. Sono disponibili [controlli delle fasi e degli spostamenti](REV004/grillage-validation.json), [equilibrio](REV004/equilibrium.json), [verifiche](REV004/validation.json), [combinazioni](REV004/Combinazioni_REV004.xlsx) e [report Word](REV004/Report_REV004.docx). Il flusso produce 648 risultati di sezione e i grafici di entrambe le travi. Un calcolo completato non implica che tutti i tassi di lavoro siano inferiori a 1: leggere gli esiti delle singole verifiche e gli avvisi.

In questa revisione il ritiro è longitudinale, con effetto primario e secondario associati alla stessa azione. Non è applicato il ritiro trasversale delle fasce. Il progetto trasversale della soletta, la fessurazione e la calibrazione delle rigidezze del graticcio restano fuori da questo esempio. WIND resta calcolato ma con coefficiente zero nelle combinazioni del checker N–Mx–Vy; Tau b non è disponibile nell'attuale API GPC. I carichi e le combinazioni sono dimostrativi, come descritto sotto.

Durante il test è stato corretto un difetto di **Build Model**: nodi coincidenti potevano perdere gli attributi del nodo successivo, inclusi gli appoggi. Ora vengono uniti vincoli, link, gruppi e carichi. La precedente esecuzione di sviluppo senza appoggi era instabile e non è una soluzione valida da confrontare numericamente. Il nuovo test controlla i due ordini di ingresso, i sei appoggi di ciascuna fase, gli avvisi del solutore e la plausibilità degli spostamenti, oltre all'equilibrio.

Per ripetere: `dotnet run --project tests/BridGHe.Smoke -c Release -- --example99 replay004` riapre e verifica risultati salvati senza rilanciare Straus; `--example99 rev004` rigenera e ricalcola la revisione, richiedendo la licenza. Salvare altrove eventuali modifiche personali prima di rigenerare.

Validazione del 22 ottobre 2026: le quattro nuove analisi Straus hanno zero avvisi/errori nei log del solutore; la riapertura ricostruisce 648 risultati e 10 grafici, conserva i file nativi e conferma l'impronta numerica del report. `tools/build.ps1 -Smoke` completa 225 controlli principali, 67 gruppi di regressione e 3 gruppi grafici/report separati. Sono stati riaperti 50 GHX con 3626 collegamenti conservati e risolti i 23 esempi portabili senza richiamare Straus. La Release è stata sincronizzata in `bin/Release` con zero avvisi di compilazione e zero errori. Questo aggiornamento non riesegue le analisi FEM di tutti gli esempi storici.

## Revisioni precedenti

Aprire [REV003](REV003/flusso-acciaio-misto_REV003.ghx) per il ponte a due travi oppure [REV002](REV002/flusso-acciaio-misto_REV002.ghx) per il completamento della travata originale. Il file `flusso-acciaio-misto_REV001.ghx`, il suo Excel e i suoi calcoli sono conservati invariati. Le due nuove revisioni hanno cartelle, Excel, template e report autonomi.

## Geometria e ipotesi

Unità FEM N–mm–MPa; reazioni per gli irrigiditori in kN. REV002 mantiene la trave continua 12+12 m, discretizzata in aste di 2 m e vincolata nel piano. REV003 costruisce un graticcio spaziale con `Bridge Grillage Mesh con diaframmi pieni`: due travi a interasse 3 m, impalcato largo 5 m, due campate da 12 m, conci da 8 m e traversi ogni 2 m. Il modello ha 37 aste e 26 nodi: 24 aste longitudinali e 13 traversi. I sei appoggi sono assegnati con `Bridge Bearings`, con una traslazione longitudinale bloccata, guide longitudinali su una trave e gli altri appoggi multidirezionali; le rotazioni sono libere.

Travi S355: altezza 1800 mm, ala superiore 600×30 mm, inferiore 800×40 mm, anima 15 mm. Nelle nuove revisioni sono corretti i collegamenti delle ali, invertiti nel file originale. Soletta C32/40 spessa 300 mm, fascia efficace dimostrativa 2500 mm per trave; doppia armatura Ø16/100 con asse a 50 mm dalle facce, come nei controlli originali. Gli slider della soletta sono ora collegati. Traversi S355 H800 con ali 300×20 e anima 12 mm. Le quote degli assi della REV003 derivano dal baricentro della sezione d'acciaio e dallo spessore della soletta.

Il FEM è un graticcio di travi: la rigidezza longitudinale della soletta entra nelle proprietà omogeneizzate; la mesh della soletta è geometrica, disponibile in preview. Non si aggiunge una shell longitudinale alla stessa rigidezza omogeneizzata. La rigidezza trasversale è affidata ai traversi d'acciaio; questo esempio non verifica la soletta trasversalmente.

I carichi distribuiti del file personale restano azioni dimostrative: G1_GETTO = 10 N/mm, G2 = 10+2+1 N/mm, Q = 10 N/mm verso il basso, per trave; WIND = 5 N/mm trasversale. Non sono una determinazione normativa del traffico o dei pesi reali della geometria. In particolare `G2_SOLETTA` è il nome ereditato di un incremento da 10 N/mm: non va interpretato come un secondo peso proprio della stessa soletta. Prima di un impiego progettuale occorre sostituire queste azioni con quelle del ponte effettivo.

## Ritiro calcolato

`Creep And Shrinkage Concrete` riceve Fck=32 MPa, RH=70%, cemento N, t=365 giorni, fine cura ts=7 giorni e attivazione del vincolo composito tr=7 giorni. Ac=2500×300=750000 mm²; u=2×2500=5000 mm, assumendo due facce esposte e trascurando i bordi; h0=2Ac/u=300 mm. Queste sono ipotesi modificabili dell'esempio.

L'uscita incrementale è **Δεcs = −0,000198939016745**, circa −199 µε, tra 7 e 365 giorni. Il calcolo implementa il modello di ritiro EN 1992-1-1:2004 documentato in [RITIRO.md](../../docs/RITIRO.md). Il componente restituisce anche ritiro totale, essiccamento e ritiro autogeno. Non si sommano nuovamente queste uscite all'incremento.

La stessa uscita εcs alimenta sia `Composite Shrinkage Actions`, che produce pre-strain e gradiente equivalenti per Straus, sia `Bridge Shrinkage Action`, che ricostruisce l'effetto primario nella sezione. L'azione `RITIRO` contiene il caso FEM `RITIRO_FEM` e lo strain; `Primary Only` è falso, senza l'inversione booleana presente nel file originale. I due contributi ricevono **un solo coefficiente Excel**. Per questo esempio n ritiro è assunto uguale al coefficiente a lungo termine del componente; è un'ipotesi dichiarata, non un calcolo automatico di tutti i coefficienti reologici del ponte.

## Percorso dei componenti

1. Costruire il modello d'acciaio. In REV003 `Bridge Grillage Mesh con diaframmi pieni`, `Bridge Fabrication Segments`, `Bridge Truss Diaphragms / Bridge Solid Diaphragms`, `Bridge FEM Members` e `Bridge Bearings` alimentano il modello base. I gruppi G1/G2 individuano le due travi da verificare.
2. Conservare geometria e ID delle aste nelle quattro analisi incrementali. `Override Beam Property` assegna le proprietà per G2, Q e ritiro. I traversi della REV003 rimangono d'acciaio e sono inclusi in tutte le analisi.
3. Premere **Run** sulle quattro `Straus R3 Analysis` dopo modifiche a geometria, vincoli, proprietà o carichi. I risultati già calcolati sono memorizzati nel GHX; `Keep Running` è falso.
4. Collegare i modelli completi calcolati alle azioni: G1 nella fase 10 acciaio; G2 nella fase 20 misto a lungo termine; RITIRO nella fase 30; Q e WIND nella fase 40 a breve termine. Le liste Models e Phases sono appiattite per evitare iterazioni separate del catalogo Excel.
5. Modificare soltanto il foglio **Combinations** di `Combinazioni_REV00x.xlsx`, mantenendo i nomi esatti G1, G2, RITIRO, Q e WIND. Il foglio Actions identifica le fonti: non modificarlo manualmente. Premere **Run** nel componente Excel in modalità Read, poi **Run** in `Build BridGHe Model`.
6. Premere **Run** in `Super Element Check`: uno nella REV002, due nella REV003. L'interpolazione delle stazioni fra modelli è gestita dal flusso esistente. Le estremità delle aste devono coincidere. Le verifiche delle due travi e i relativi grafici restano separati; il riepilogo e il report le raccolgono insieme.
7. `Bridge Detail Check` aggiunge i pioli. `Bridge Detail Stations` seleziona gli irrigiditori soltanto nelle loro posizioni: intermedi ogni 2 m fuori dagli appoggi, appoggi a 0/12/24 m. Conserva trave, stazione, combinazione e struttura dell'albero, comprese le due sezioni ai lati dei giunti.
8. Per gli appoggi collegare anche Phases al selettore. La reazione deriva dalle reazioni nodali dei casi sorgente e dagli stessi coefficienti del modello assemblato. `Compression kN` alimenta ReactionKN; `End type` alimenta Location. La reazione firmata resta disponibile: un valore negativo segnala sollevamento, non una compressione positiva.
9. I cinque grafici per trave hanno preview sul canvas: sollecitazioni, tensioni, pioli Ed/Rd e inviluppo WR degli irrigiditori intermedi e di appoggio. Le Value List degli irrigiditori consentono anche la singola verifica.
10. `Bridge Report` legge il template locale. La checklist sceglie i capitoli; **Generate** scrive il DOCX solo su richiesta. Per sostituire un report esistente attivare Overwrite, chiudere il documento e premere Generate.

## Combinazioni e campo coperto

I coefficienti sono dimostrativi, non combinazioni NTC/Eurocodici generate automaticamente. Sono presenti getto, permanenti, permanenti+ritiro, totale, Q isolata e una combinazione amplificata. La colonna WIND è zero: il caso è calcolato nel FEM, ma il verificatore corrente copre N–Mx–Vy ed esclude esplicitamente T, M2 e V1. Le sei componenti rimangono nei risultati FEM; gli effetti esclusi e le loro interazioni non sono verificati.

Il report distingue WR entro limite, superamenti e dati mancanti. `Tau b` resta NaN perché manca un equivalente verificato nell'API GPC corrente; le verifiche di taglio effettivamente disponibili e i pioli sono mantenuti. Sollevamento/ancoraggi, fatica, instabilità globale, sisma, soletta trasversale, attraversamenti e fondazioni non risultano verificati da questo esempio. Le verifiche attivate possono non essere soddisfatte: il completamento del flusso non equivale al dimensionamento del ponte.

## Risultati e report

- [Report REV002](REV002/Report_REV002.docx): travata originale completata.
- [Report REV003](REV003/Report_REV003.docx): due travi e flusso completo.
- [Excel REV002](REV002/Combinazioni_REV002.xlsx) e [Excel REV003](REV003/Combinazioni_REV003.xlsx).

La selezione iniziale del report comprende riepilogo dei WR per famiglia, fasi/combinazioni e grafici. Le tabelle analitiche, le configurazioni di sezione e le schede dei casi governanti sono attivabili nella checklist; sul caso completo generano documenti molto estesi. Tutti gli input e i risultati forniti sono comunque conservati in `bridghe/report.json` dentro il DOCX. Per consultare ogni voce direttamente in GH usare `Bridge Check Summary`.

Sono state eseguite quattro analisi Straus per revisione. REV002 produce 384 risultati di sezione; REV003 ne produce 648 sulle due travi. Il controllo di equilibrio confronta la somma delle reazioni verticali con 240/312/240 kN per trave per G1/G2/Q e con zero per il ritiro. La riapertura dei GHX ricostruisce i risultati e 5/10 grafici senza richiamare il solutore né cambiare i file ST7/LSA. Evidenze in `validation.json`, `equilibrium.json` e `replay-validation.json` di ciascuna revisione.

Rigenerazione con licenze Rhino/Straus e dipendenze di sviluppo disponibili:

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release -- --example99 rev002
dotnet run --project tests/BridGHe.Smoke -c Release -- --example99 rev003
dotnet run --project tests/BridGHe.Smoke -c Release -- --example99 replay002
dotnet run --project tests/BridGHe.Smoke -c Release -- --example99 replay003
```

I comandi `rev002`/`rev003` rigenerano soltanto le relative cartelle, inclusi Excel e report: prima di usarli conservare eventuali modifiche personali. I comandi `replay` verificano la riapertura e non riscrivono il GHX o il report. Per analizzare una copia spostata, aggiornare i quattro percorsi Output Path dei componenti Straus; Excel e template usano percorsi relativi alla definizione.
