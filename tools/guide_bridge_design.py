"""Additional chapters of the existing Italian operator guide."""

def append_design_chapters(page, h, p, table, steps, code):
    page(22, "Scegliere le campate e generare il ponte")
    p("**Bridge Span Advisor** propone distribuzioni geometriche degli appoggi lungo un tracciato 3D. Le zone vietate sono curve chiuse orizzontali: viene considerata la loro proiezione XY. Il progettista assegna le luci minime, massime e obiettivo; il componente non verifica sezioni, pile o fondazioni.")
    steps([
        "Aprire l’esempio 33. Il tracciato è lungo 100 m; la zona vietata occupa x=40–60 m. Il franco di 2 m esclude il punto di appoggio nell’intervallo 38–62 m.",
        "Impostare Candidate Grid in mm. La griglia e le progressive obbligatorie definiscono i candidati. Gli estremi del tracciato sono sempre appoggi obbligatori.",
        "Leggere un ramo per alternativa in Support Stations. Il costo è la somma degli scarti quadratici normalizzati dalla luce obiettivo, più Pier Penalty per ogni pila interna. End Span Ratio modifica l’obiettivo delle sole campate terminali.",
        "Selezionare un solo ramo e collegarlo a Support Stations di Bridge Support Lines, insieme al tracciato. Aumentare il franco per includere l’ingombro delle fondazioni.",
    ])
    p("Un risultato vuoto significa nessuna soluzione sulla griglia attuale. Raffinare la griglia può trovare altre posizioni, con un costo di ricerca maggiore. Il test automatico confronta le prime dieci alternative con una ricerca esaustiva su un caso piccolo.")
    h("Flusso modulare e conci")
    p("Gli esempi 29–32 separano Bridge Support Lines, Girder Axes, Slab Layout, Fabrication Segments e Solid o Truss Diaphragms. Bridge Geometry Assembly riunisce le definizioni; Bridge Grillage Mesh le discretizza usando Mesh Settings. Le unità sono mm. Il capitolo 32 descrive il percorso completo, compreso l’Excel dei conci prima della mesh.")
    p("Usare l’uscita tipizzata **Bridge Geometry**, che conserva topologia, progressive, appoggi e identità dei conci. Una suddivisione non crea una cerniera FEM. Le quote degli assi non vengono dedotte dalle proprietà di sezione.")

    page(23, "Assegnare sezioni controventi e appoggi")
    h("Proprietà e regole per progressiva")
    p("Nell’esempio 35, **Bridge FEM Members** trasforma la geometria in aste con proprietà, assi locali e gruppi. Ogni **Bridge Family Property** abbina una proprietà a una famiglia selezionata per nome. All steel families esclude la soletta; DeckTransverse la seleziona esplicitamente. Section Rules riceve le sezioni dei conci o le regole per progressiva.")
    p("Nell’esempio 34, **Bridge Section Schedule** assegna una proprietà a G2 fra 12.345 e 45 m, con scala verticale da 1 a 1.2. Le aste vengono divise alle estremità della regola. Regole sovrapposte sulla stessa selezione sono respinte. La rastremazione dimensionale non è disponibile per sezioni generiche/omogeneizzate.")
    p("Gli ID FEM sono numerici. Physical IDs, ad esempio Girder:2:1, identificano il concio anche cambiando la mesh. Unassigned deve essere vuoto prima dell’esportazione. Le sezioni degli esempi sono dimostrative.")
    h("Controventi")
    p("Inserire **Bridge Longitudinal Bracing** prima dell’assegnazione. Nell’esempio 38, due pannelli fra 0, 12.345 e 30 m generano otto diagonali X nelle due maglie trasversali. Pattern sceglie X, V o Warren. Un offset non nullo genera montanti fisici. Connect X richiede un’intersezione reale in 3D e divide i membri nell’incrocio.")
    h("Vincoli orientati")
    p("**Bridge Bearings**, esempio 36, restituisce nodi e riferimenti dei vincoli. X segue la tangente orizzontale, Y punta a sinistra e Z è verticale. Guide Angle ruota il piano; Orientation permette un riferimento esplicito.")
    table(["Tipo", "Comportamento"], [
        ("0", "Traslazioni bloccate, rotazioni libere"),
        ("1", "Guida longitudinale: X libera, Y/Z bloccate"),
        ("2", "Multidirezionale: X/Y libere, Z bloccata"),
        ("3", "Molle Kt in N/mm e Kr in N mm/rad"),
    ], [0.65, 6.29])
    p("Il tipo 0 non è un incastro rotazionale. I dodici appoggi dell’esempio seguono l’ordine supporto/trave indicato da Labels. Non sovrapporre altri vincoli globali involontariamente.")

    page(24, "Collegare travi e soletta strutturale")
    h("Tre rappresentazioni di connessione")
    p("Nell’esempio 37, **Bridge Rigid Links** produce link rigidi spaziali nativi Straus. **Bridge Connection Beams** crea aste deformabili con proprietà Beam/Truss esplicita. **Bridge Beam Offsets** modifica copie delle travi con offset locali X/Y, cancellando i risultati precedenti. Ogni operazione ha un componente distinto.")
    p("Collegare a Build Model gli elementi del modo scelto. Non sommare le tre rappresentazioni sulla stessa connessione. I link richiedono estremi univoci coincidenti con i vertici FEM previsti; duplicati, link collassati e target ambigui sono respinti.")
    h("Mesh della soletta")
    steps([
        "Aprire l’esempio 39. Collegare Bridge Geometry a Bridge Deck Mesh e assegnare una proprietà plate con spessore coerente con la soletta.",
        "Impostare dimensioni massime longitudinali e trasversali. Le progressive già presenti nella geometria vengono mantenute. Le colonne comprendono gli attacchi delle travi.",
        "Aggiungere Pour Boundaries: 30 e 70 m nell’esempio. Si ottengono i gruppi Deck\\Pour1, Deck\\Pour2 e Deck\\Pour3.",
        "Usare la nuova uscita BG per Bridge FEM Members. Collegare Plates a Build Model e Connection Lines a Bridge Rigid Links oppure Bridge Connection Beams, secondo la meccanica voluta.",
    ])
    p("I plate giacciono sulla superficie media. Le linee di connessione uniscono gli assi delle travi ai vertici plate. Le mesh triangolari evitano quadrilateri sghembi; la convergenza FEM richiede comunque analisi successive.")
    h("Evitare il doppio conteggio del calcestruzzo")
    p("Con **shell strutturali della soletta**, usare proprietà di **solo acciaio** per le travi. Nel flusso con sezioni omogeneizzate, la rigidezza del calcestruzzo è già inclusa nelle travi: aggiungere la soletta strutturale può contarla due volte. Bridge Model Audit segnala questa possibile sovrapposizione.")
    p("Le due schematizzazioni hanno ipotesi diverse. L’esempio 45 usa shell e travi di acciaio; l’esempio 19 usa il flusso di override delle sezioni omogeneizzate. Nessuna conversione automatica rende i due modelli equivalenti.")

    page(25, "Preparare snapshot e fasi di costruzione")
    p("**Bridge Construction Sequence** riceve un modello completo e una Bridge Phase numerata. Produce uno snapshot indipendente con gli elementi selezionati e senza risultati FEM. Number, Name e Day rendono riconoscibile la fase; non trasferiscono automaticamente la storia.")
    steps([
        "Aprire l’esempio 40: fase 10 concio iniziale G2, fase 20 intera G2, fase 30 struttura completa, ai giorni 0/7/28.",
        "Usare Active Groups per gruppi e discendenti, Physical IDs per conci. Le due selezioni si sommano; entrambe vuote selezionano tutta la struttura.",
        "Usare gli ID nodali del modello in Active Bearing IDs, oppure * per mantenerli tutti. Enable Bearings=false elimina vincoli e molle nodali.",
        "Collegare Girder Property se occorre cambiare esplicitamente la proprietà delle travi longitudinali. Section State non applica automaticamente questa modifica.",
        "Verificare i carichi che rimangono sugli elementi attivi. Applicare gli incrementi voluti, analizzare lo snapshot e costruire le azioni del workflow del verificatore.",
    ])
    h("Snapshot indipendente e stage nativo")
    p("Uno snapshot non trasferisce forze, deformazioni, viscosità o tensioni accumulate nella fase precedente. I risultati vanno composti con Bridge Action, Bridge Shrinkage Action, Bridge Phase e Build BridGHe Model secondo il flusso illustrato nei primi capitoli.")
    p("Native Stage è disponibile solo per attivazioni tramite gruppi rappresentabili con proprietà e appoggi invariati, senza link rigidi e con Load Increments espliciti. Gli stage ordinati vanno collegati al modello completo originale. Negli altri casi questa uscita resta vuota; Information ne spiega la ragione.")
    h("Ritiro e controlli")
    p("Day è metadato, non una legge di maturazione o viscosità. Il ritiro usa lo strain incrementale e gli eventuali effetti secondari FEM già descritti nei capitoli 5–8. Il nome di una fase non genera carichi.")
    p("L’audit esteso controlla copertura dell’assegnazione, modifiche degli assi locali, conflitti fra riferimenti degli appoggi, link e differenze geometriche fra i modelli di fase. La connettività geometrica non dimostra la stabilità della matrice di rigidezza.")

    page(26, "Disporre dettagli e leggere le risposte")
    h("Pioli e irrigidimenti")
    p("**Bridge Detail Layout**, esempio 41, usa le definizioni gPC della sezione, dei pioli e degli irrigidimenti. Per G2 su [0,10 m), due pioli per fila ogni 200 mm producono 100 pioli; irrigidimenti ogni 4 m producono sei prismi. Il volume di un piatto è 200×1330×20 mm³.")
    p("Gli intervalli sono semiaperti per evitare file duplicate fra zone adiacenti. Steel Top Offset definisce esplicitamente la quota del lembo superiore rispetto all’asse. Copriferro, numero intero dei pioli e compatibilità geometrica sono controllati. Le geometrie non aggiungono elementi FEM; Details restituisce gli oggetti gPC per le verifiche.")
    h("Frecce e controfreccia")
    p("**Bridge Deflection & Camber**, esempio 42, legge spostamenti nodali per trave, caso e step. La relativa sottrae la corda fra gli appoggi deformati della campata. La controfreccia è l’opposto dello spostamento scelto moltiplicato per Camber Factor. Display Scale modifica solo le curve.")
    p("L’esempio usa dati sintetici: a 15 m la freccia assoluta è −28 mm, la relativa −20 mm e la controfreccia +20 mm. Non sono risultati Straus. Con dati reali servono il modello analizzato e la stessa Bridge Geometry.")
    h("Storie per fatica")
    p("**Bridge Fatigue History**, esempio 43, riceve valori ordinati o estrae una storia FEM nell’ordine di transito dichiarato. Il rainflow mantiene le mezze escursioni residue. Per la storia 0,10,2,8,0 si ottengono un ciclo di range 10 e uno di range 6. Con esponente 3 e un ciclo di riferimento il range equivalente è la radice cubica di 1216.")
    p("Response Factors trasforma linearmente le sei azioni in una tensione o un flusso nelle unità dichiarate. Nessuna curva S–N, categoria di dettaglio, danno o traffico annuo è scelto automaticamente.")
    h("Mappa delle verifiche")
    p("**Bridge Check Map**, esempio 44, abbina risultati gPC, ID asta, stazione e caso. Grigio significa dati numerici assenti, ambra controlli parziali, rosso rapporto sopra 1. Governing conserva fase/caso/stazione e massimi a pari merito. Le stazioni e gli stati limite non forniti restano fuori dalla mappa.")

    page(27, "Eseguire gli esempi del modello completo")
    p("Aprire **example/45-ponte-completo-fasi/45-ponte-completo-fasi.ghx**. Seguire da sinistra a destra: definizioni geometriche, Geometry Assembly, Grillage Mesh, controventi, mesh soletta, FEM Members, appoggi e connessioni, Build Model, audit e tre snapshot.")
    p("Il ponte curvo ha campate 30+40+30 m, tre travi, conci da 12 m, controventi Warren e soletta shell da 250 mm. Le fasi attivano acciaio, primo getto e soletta completa. Non ci sono carichi o risultati: è un esempio di assemblaggio da completare con dati di progetto e analisi.")
    table(["Esempi", "Funzione da provare"], [
        ("33", "Distribuzione delle campate e zone vietate"),
        ("34–35", "Sezioni per progressiva e assegnazione dei membri"),
        ("36–37", "Appoggi, link rigidi, aste elastiche e offset"),
        ("38–39", "Controventi e mesh soletta per getti"),
        ("40–41", "Snapshot delle fasi e layout dei dettagli"),
        ("42–44", "Frecce, storie per fatica e mappa delle verifiche"),
        ("45", "Flusso completo fino al modello e alle fasi"),
    ], [0.8, 6.14])
    h("Rigenerare e controllare")
    code("dotnet run --project tests/BridGHe.Smoke -c Release -- `\n  --bridge-design-examples\npowershell -NoProfile -ExecutionPolicy Bypass `\n  -File tools/build.ps1 -Smoke")
    p("Il generatore risolve, salva e riapre tredici GHX; ogni cartella contiene README e validation.json. Il test offline controlla dati, geometria, archivi e risultati noti senza avviare Straus. Fare una copia prima di rigenerare file modificati manualmente.")
    h("Verifica nativa completata")
    p("Il 20 ottobre 2026 sono superati --bridge-bearings-native e --bridge-connections-native: UCS, vincoli, molle e link spaziali riletti da Straus R3. Le prove verificano l’esportazione e non avviano il solver. L’esempio 45 resta un modello geometrico senza carichi né nuova analisi strutturale; il precedente blocco della licenza è risolto.")
