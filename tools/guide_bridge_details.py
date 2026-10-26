"""Operator tutorial for example 47; numerical values from the executed GPC report."""


def append_detail_chapters(page, h, p, table, steps, code):
    page(29, "Impostare pioli e irrigidimenti")
    p("L’**esempio 47** contiene due definizioni: 47-pioli-irrigidimenti-verifiche.ghx per imparare il calcolo locale e 47-integrazione-modello.ghx per collegarlo ai risultati Straus del superelemento. I file sono in example/47-pioli-irrigidimenti-verifiche; il README descrive tutti gli input, i limiti e i test.")
    h("Distinguere il disegno dal calcolo")
    p("Bridge Detail Layout disegna pioli e piatti sulla geometria prodotta da Bridge Grillage Mesh. Non aggiunge rigidezza o elementi FEM. Le stesse definizioni Bridge Studs, Bridge Intermediate Stiffener e Bridge Support Stiffener alimentano il verificatore GPC. Gli intermedi contribuiscono all’idoneità dei pannelli d’anima; gli irrigidimenti d’appoggio ricevono anche una reazione compressiva esplicitamente assegnata.")
    table(["Dato del benchmark locale", "Valore"], [
        ("Ponte / zona del dettaglio", "3 travi; campate 30+40+30 m; G2 tra 26 e 34 m"),
        ("Sezione ad H / soletta efficace", "Anima 1330×16; ali 450×30 e 600×40; soletta 2200×250 mm"),
        ("Materiali / armatura longitudinale", "S355, C30, B450; due file Ø16/150, asse a 40 mm"),
        ("Pioli", "2 per fila, Ø22 H180; passo 200 mm, trasversale 150 mm"),
        ("Irrigidimenti intermedi", "Coppia 200×20 mm; pannelli 2 m; N aggiuntivo 100 kN"),
        ("Irrigidimento d’appoggio interno", "Coppia 200×25 mm; R=900 kN; impronta 400×600 mm"),
        ("Saldature / eccentricità", "Gola 6 mm intermedi, 10 mm appoggio; eccentricità nulle"),
    ], [2.65, 4.29])
    steps([
        "Aprire il primo GHX. I pannelli gialli collegati agli input sono modificabili. Attivare la preview di Bridge Grillage Mesh per vedere l’intero ponte; Layout mostra il dettaglio G2.",
        "Controllare le quote: asse trave −1500 mm e Steel Top Offset=1250 mm portano la faccia superiore dell’acciaio a −250 mm. I pioli terminano a −70 mm; restano 70 mm fino all’estradosso.",
        "Leggere le quantità: 40 file da due pioli nell’intervallo [26,34) m; due coppie intermedie a 28 e 32 m e una coppia d’appoggio a 30 m. In totale 80 pioli e 6 piatti.",
    ])
    p("Width è lo sbalzo di ciascun piatto, non la larghezza della coppia. Le stazioni del layout non aggiornano automaticamente gli input delle definizioni originali: se si modifica la disposizione, riallineare i pannelli di verifica. Details di Layout è una lista mista di definizioni: selezionare tipo e stazione corretti, senza passarla interamente a Section Check.")

    page(30, "Leggere i risultati e confrontare i dettagli")
    p("Il benchmark assegna tre incrementi di taglio: 300 kN su acciaio, 200 kN sul misto lento con Phi=2, 800 kN sul misto istantaneo con Phi=0; segue ritiro −200 microstrain con Phi=2. Gli incrementi meccanici hanno N=M=T=0. Sono azioni locali dimostrative, non sollecitazioni ricavate dal ponte della preview.")
    p("Il taglio finale è 1300 kN. Il flusso di connessione è 611.137 N/mm, pari a 611.137 kN/m: 103.484 dalla fase lenta e 507.653 da quella istantanea. La fase acciaio e il ritiro uniforme non aggiungono qui flusso. Gli effetti di estremità e le azioni secondarie da vincolo richiedono dati specifici.")
    h("Pioli")
    p("Per la H dell’esempio, PEd si ottiene moltiplicando il valore assoluto del flusso per il passo e dividendo per i due pioli della fila; dividere poi per 1000 per passare da N a kN. La resistenza GPC del singolo piolo è 109.478 kN. Il controllo indipendente del ramo acciaio è descritto nel README.")
    table(["Caso", "Passo mm", "PEd kN", "PRd kN", "Eta"], [
        ("A base", "200", "61.114", "109.478", "0.558"),
        ("B passo doppio", "400", "122.227", "109.478", "1.116"),
    ], [2.14, 1.2, 1.2, 1.2, 1.2], centers=(1, 2, 3, 4))
    p("B mantiene q e PRd, raddoppia PEd e dimezza qRd. Il controllo resistente del piolo non passa. RepeatedActionDetail=True riguarda il dettaglio per azioni ripetute: non verifica la fatica. Il file non attiva le verifiche dell’armatura trasversale della soletta e del suo ancoraggio.")
    h("Irrigidimenti")
    table(["Controllo e variante", "Base A", "Variante"], [
        ("C, intermedio 8 mm: rigidezza torsionale", "0.96", "5.75"),
        ("C, intermedio 8 mm: piatti entro classe 3", "0.88", "2.19"),
        ("D, R=3000 kN: saldature alle flange", "0.42", "1.41"),
        ("D, R=3000 kN: contatto piatti/flangia", "0.32", "1.07"),
    ], [4.54, 1.2, 1.2], centers=(1, 2))
    p("I valori sono rapporti domanda/limite GPC. Il piatto da 8 mm perde l’idoneità e il beneficio dei pannelli irrigiditi. La reazione maggiore rende critici collegamento e contatto. La preview resta quella di A finché non si collegano le definizioni alternative anche a Layout.")
    p("Leggere insieme Check, Demand, Resistance, Units, Ratio, Status e Notes. NaN/Da verificare non significa utilizzo nullo. Non verificato è un esito strutturale e non richiede un componente rosso. Restano visibili i warning su trazione del CLS e ritiro; apparecchio d’appoggio, piastra di ripartizione e dettagli con intagli non sono verificati da questo esempio.")
    p("Ultimate seleziona i controlli GPC; le azioni devono essere già combinate. I coefficienti del file sono dati di prova espliciti, non una scelta normativa automatica. Le formule e le note documentano il codice locale, non attestano la completezza normativa di una verifica del ponte.")

    page(31, "Collegare i dettagli al modello calcolato")
    p("**47-integrazione-modello.ghx** riutilizza risultati Straus dell’esempio 19 REV003, memorizzati nel file: beam 36 di G2, tra 27.5 e 30 m. La sezione è H2000 con soletta efficace 3000×250 mm e n0=7, n infinito=18, n ritiro=12. Non è la sezione del benchmark locale e non deve riprodurne gli stessi valori.")
    steps([
        "Collegare modelli con risultati, Bridge Phase e tabella delle combinazioni a Build BridGHe Model. Usare Calculation=0, Cumulative, e premere Run.",
        "Collegare il modello assemblato a Super Element Filter e selezionare G2. Passare le aste a Composite beam con la proprietà fisica mista. Le aste devono provenire dal modello assemblato, con le sollecitazioni.",
        "Collegare lo stesso modello e i Composite beam a Super Element Check e premere Run. Ripetere i Run dopo variazioni alle fasi o alle combinazioni.",
        "Portare Tension Check, prima uscita del checker, a Section result di Bridge Detail Check. Questo oggetto contiene anche geometria, materiali e storia delle azioni. Non usare Tau b, attualmente NaN, né la sola lista delle forze.",
        "Collegare le definizioni di pioli, intermedi e appoggio ai rispettivi ingressi di Detail Check. Il risultato alimenta Bridge Stud Results, Bridge Verification Results e Bridge Check Summary.",
    ])
    h("Stazioni e combinazioni")
    p("Conservare i rami {indice beam; indice stazione}; gli item del ramo corrispondono alle combinazioni. Per dettagli diversi lungo il ponte selezionare zone e definizioni mantenendo queste associazioni. Un Flatten indiscriminato può perdere l’identità della sezione o abbinare una reazione al risultato sbagliato.")
    h("Stato limite e reazioni")
    p("Super Element Check usa attualmente SLE rara. Per i controlli SLU degli irrigidimenti, collegare Bridge Analysis Options con LimitState=Ultimate al Detail Check e usare azioni effettivamente combinate allo SLU. Cambiare l’opzione non applica fattori e non modifica le azioni FEM. Nel file integrato DEMO ha fattori unitari: Ultimate mostra il funzionamento dei controlli, senza qualificare la combinazione come normativa.")
    p("ReactionKN=900 kN è un dato dimostrativo indipendente, non la reazione estratta dal ponte. Bridge Bearing Envelope può aiutare a leggere le reazioni del modello analizzato, ma vanno controllati caso/combinazione, assi, segno e unità: N nei risultati N–mm, kN nell’irrigidimento. GPC usa R come inviluppo assegnato costante nella storia del calcolo locale.")
    h("Limite attuale del flusso")
    p("LinearHistory calcola lo storico N–Mx e non implementa taglio, pioli e irrigidimenti; Detail Check lo rifiuta. Cumulative va scelto per la sua ipotesi di calcolo, non per aggirare l’errore. L’adattatore dei superelementi copre una H simmetrica con soletta e N–Mx–Vy; torsione e flessione deviata richiedono strumenti ulteriori.")
    p("Entrambi i file sono verificati mediante soluzione, salvataggio e riapertura in Rhino 8. Il secondo conserva percorsi dell’albero, azioni e strain per ogni stazione. I report validation.json e validation-modello.json distinguono la verifica locale dalla nuova analisi globale: solverInvoked=false.")
