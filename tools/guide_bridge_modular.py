"""Current bridge geometry workflow, intentionally independent from construction phases."""


def append_modular_chapters(page, h, p, table, steps, code):
    page(32, "Costruire il ponte con definizioni separate")
    p("Il flusso geometrico aggiornato usa componenti con una responsabilità precisa. Aprire gli esempi 29–32 per iniziare e il 50 REV002 per il percorso fino alle verifiche. La curva rappresenta il riferimento superiore dell’impalcato; progressive e dimensioni sono in mm.")
    steps([
        "Collegare la curva a Bridge Support Lines. Support Stations contiene le distanze degli appoggi lungo la curva; vuoto significa entrambi gli estremi. Skew Angles accetta un valore comune o un angolo per appoggio. Il componente posiziona gli appoggi; i vincoli FEM si assegnano dopo.",
        "In Bridge Girder Axes impostare Transverse Offsets crescenti, positivi verso sinistra guardando lungo il tracciato. Axis Levels è una quota negativa rispetto al piano superiore: un valore comune oppure uno per trave. Bridge Regular Girders genera offsets simmetrici da numero e interasse.",
        "Bridge Slab Layout definisce larghezze sinistra/destra, spessore e pendenza trasversale. Una pendenza costante richiede un solo valore; un profilo variabile richiede anche le progressive. Slab Axis Level vuoto colloca gli assi trasversali a metà spessore.",
        "Scegliere Bridge Solid Diaphragms oppure Bridge Truss Diaphragms. Stations esplicite sostituisce il passo automatico; At Supports include le linee di appoggio. Il reticolare aggiunge altezza, pannelli, schema X/V/Warren e collegamento degli incroci.",
        "Preparare i conci come descritto nel capitolo successivo. Collegare tracciato, appoggi, travi, soletta, conci e diaframmi a Bridge Geometry Assembly. L’uscita è una definizione geometrica, ancora senza mesh né proprietà FEM.",
    ])
    h("Helper facoltativi")
    p("Bridge Support Stations ordina le distanze, accorpa duplicati entro la tolleranza e può aggiungere appoggi agli estremi o creare campate uguali. Bridge Span Stations e Alignment Curve servono invece a generare un tracciato rettilineo o circolare da campate note. Una curva Rhino può entrare direttamente nei componenti di definizione, senza ricostruire prima le luci.")

    page(33, "Definire i conci e compilare le sezioni in Excel")
    p("Un concio è una porzione fisica della trave; non coincide necessariamente con una campata o con un elemento FEM. Il suo ID G2-C3 significa terzo concio della seconda trave. Il numero dei conci è disponibile prima della mesh.")
    steps([
        "Creare uno o più Bridge Joint Stations: Joint Stations contiene le progressive, Girder IDs seleziona le travi interessate. Girder IDs vuoto applica i giunti a tutte le travi. Unire le uscite nell’ingresso Joint Sets di Bridge Fabrication Segments.",
        "Collegare anche Alignment e Girder Axes. Se una trave non ha giunti espliciti, Maximum Segment Length la suddivide in parti uguali non più lunghe del valore assegnato. Zero genera un solo concio. Le progressive esplicite sostituiscono questa suddivisione per la trave interessata.",
        "Leggere Segment Count, Segment IDs, Start Stations, End Stations e Segment Lengths. Collegare Fabrication Plan a Bridge Section Excel Template e premere Run. Il file esistente è protetto, salvo Overwrite esplicitamente abilitato.",
        "Compilare le dimensioni delle sezioni nel foglio Sezioni. Non alterare l’associazione ID/progressive. L’asterisco eredita la dimensione della proprietà base; una cella vuota è un errore. Ricalcolare le formule in Excel e salvare.",
        "Usare Bridge Read Section Excel con lo stesso piano e lo stesso percorso. Premere Run e collegare Section Table a Bridge Segment Sections, insieme a Fabrication Plan e Base Property. Il lettore conserva la tabella importata e non rilegge il disco a ogni soluzione GH.",
        "Dopo una modifica ai giunti, creare o riallineare consapevolmente la tabella. Il lettore rifiuta ID o intervalli obsoleti; una semplice modifica della mesh lascia invece validi i conci e le sezioni.",
    ])
    h("Quote e proprietà")
    p("Top Offsets espone la distanza fra baricentro e ala superiore. La modifica dell’altezza di una sezione non sposta automaticamente gli assi della carpenteria: aggiornare Axis Levels o applicare offset espliciti coerenti. Le proprietà omogeneizzate delle fasi non devono spostare i nodi tra un’analisi e l’altra.")
    p("Bridge Joint Tree e Crossfall Table sono helper per importare alberi già organizzati. Per l’inserimento manuale preferire Joint Stations e le liste esplicite della soletta.")

    page(34, "Generare la mesh e collegarla alle fasi")
    steps([
        "Bridge Mesh Settings controlla Maximum Element Length, Chord Tolerance e la discretizzazione trasversale della soletta. Slab Beam Spacing=0 usa tutte le stazioni raffinate; Slab Beam Stations permette progressive esplicite.",
        "Collegare Bridge Definition e Mesh Settings a Bridge Grillage Mesh. Un solo motore spezza le travi agli attacchi della soletta, ai conci, ai diaframmi e agli appoggi, conservando gli ID dei conci. Usare Bridge Geometry per la carpenteria e Grillage Geometry quando occorrono anche le aste della soletta.",
        "Bridge Family Property associa una proprietà a una famiglia scelta per nome. Collegare le definizioni a Bridge FEM Members insieme alle Section Rules. All steel families esclude la soletta. Verificare che Unassigned sia vuoto prima di costruire il modello.",
        "Assegnare Bridge Bearings ai punti di appoggio. Collegare Deck Connections a Bridge Rigid Links per vincoli cinematici nativi, oppure a Bridge Connection Beams per una rigidezza esplicita. Bridge Beam Offsets tratta separatamente gli offset locali. Non duplicare la stessa connessione con più rappresentazioni.",
        "Nel 50, costruire M0 in acciaio e poi le varianti n infinito, n0 e n ritiro sulla stessa geometria. Il REV002 classifica le aste sotto Dead e applica stati fessurato/non fessurato ai due modelli meccanici. Il ritiro resta non fessurato, per l’ipotesi dichiarata nell’esempio.",
        "Condividere i riferimenti dei casi fra Bridge Action e Bridge Analysis Cases. Quest’ultimo riceve il modello prima dell’analisi e verifica i nomi; la sua uscita va in Analysis Results di Straus. Così i casi richiesti dal verificatore vengono restituiti anche se si selezionano ulteriori combinazioni.",
        "Dopo le analisi, eseguire Build BridGHe Model, Super Element Check e i controlli dei dettagli. Controllare risultati, grafici e input del report. La verifica delle travi principali mantiene l’ipotesi N–Mx–Vy; T, M2 e V1 non entrano nelle verifiche di sezione.",
    ])
    h("Controlli e documenti di prova")
    p("La migrazione confronta geometria, appoggi, conci e collegamenti dei file salvati. I benchmark nativi distinguono esportazione, soluzione FEM e controlli di sezione. Consultare docs/FLUSSO-MODULARE.md e i report di validazione per l’elenco delle prove effettivamente eseguite; la riapertura di un GHX non costituisce una verifica strutturale completa.")
