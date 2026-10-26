# REV001: tracciato, appoggi e sezioni da Excel

Aprire [50-ponte-quattro-modelli_REV001.ghx](50-ponte-quattro-modelli_REV001.ghx) con la Release aggiornata. Sono stati aggiunti due gruppi sopra il flusso orizzontale: **PREPARA GLI INPUT DAL TRACCIATO** e **CONCI E SEZIONI DA EXCEL**. Il tracciato iniziale e le sezioni sono quelli della revisione personale; il controllo d'integrazione non ha eseguito Straus.

## Tracciato e progressive degli appoggi

**Bridge Alignment Inputs** prepara gli ingressi di **Bridge Parametric Layout**. Non occorre più far coincidere manualmente somma delle campate e lunghezza della curva.

1. Impostare la curva nel parametro **TRACCIATO [mm]** mediante `Set one curve` dal menu contestuale. La curva deve essere valida, aperta e con coordinate in millimetri.
2. Inserire nel pannello **APPOGGI [mm]** le distanze lungo la curva, una per riga. Sono lunghezze d'arco 3D dal principio del tracciato, non coordinate X, parametri della curva o distanze proiettate in pianta. Invertendo la curva si inverte l'origine delle progressive.
3. Con **End Supports = True**, gli appoggi agli estremi vengono aggiunti automaticamente. Per una curva lunga 90000 mm, scrivere `30000` e `60000` produce tre campate da 30000 mm. Anche gli estremi già presenti nella lista vengono riconosciuti senza duplicarli.
4. Con **End Supports = False**, inserire tutte le progressive degli appoggi. Su una curva lunga 90000 mm, `5000`, `45000`, `85000` produce due campate da 40000 mm e due sbalzi da 5000 mm.
5. Svuotando il pannello, **Span Count** suddivide l'intera curva in campate uguali, con appoggi alle due estremità. Con progressive esplicite, Span Count viene ignorato.

La lista viene ordinata. Ripetizioni entro **Station Tolerance** vengono unite; valori prossimi agli estremi vengono riportati a 0 o alla lunghezza esatta. Queste correzioni sono indicate in **Information**. Valori fuori dalla curva oltre la tolleranza vengono rifiutati. La tolleranza iniziale è 1 mm e non arrotonda le campate derivate.

Le uscite **Alignment**, **Span Lengths**, **Start Overhang**, **End Overhang** e **Span Count** sono già collegate al componente iniziale. Gli altri controlli di impalcato, numero/interasse delle travi, diaframmi e soletta restano disponibili. Con Custom Alignment collegato, Radius, Origin e Heading del generatore precedente sono ignorati.

## Conci costruttivi e mesh FEM

**Maximum Segment Length** alimenta **Bridge Segments**: senza giunti espliciti genera i conci secondo il criterio esistente; zero significa un solo concio per trave. **Joint Stations** permette giunti comuni alle travi, ordinati e deduplicati automaticamente, nel ramo Grasshopper `{0}`. Le progressive degli estremi non sono giunti interni.

**Maximum Element Length** alimenta invece **Simple Bridge** e controlla la mesh delle aste. Un concio può contenere molti elementi FEM. Gli appoggi non diventano automaticamente giunti costruttivi. I diaframmi e le fasce della soletta mantengono i propri controlli di passo.

## Numero di conci prima delle sezioni

**Bridge Segment Excel** riceve direttamente **Bridge Geometry**. Le sue uscite **Count**, **Segment IDs**, **Girder IDs** e **Counts per Girder** sono disponibili anche prima di aprire o creare Excel e senza Base Property.

Nel REV001 iniziale ci sono **24 conci complessivi, 8 per ciascuna delle 3 travi**. Ogni coppia trave/concio ha una propria riga: `G1-C1`, `G1-C2`, …, `G3-C8`. Il conteggio non rappresenta il numero di sezioni dimensionalmente diverse, né il numero di elementi FEM.

In alternativa, nell'uscita **Segment Ranges** di Simple Bridge si può usare **List Length − 1**: una riga è l'intestazione.

## Creazione e lettura dell'Excel

Il componente ha una Value List **Create / Read** e un pulsante **Run**. Accede al file solo premendo Run.

| Modalità | Operazione |
|---|---|
| **0 — Create template** | Crea un `.xlsx` con una riga per ogni concio e ID/progressive precompilati. Con Base Property collegata inserisce le dimensioni iniziali; senza Base Property inserisce `*`. Non sostituisce un file esistente se Overwrite è False. |
| **1 — Read Excel** | Importa le dimensioni compilate, controlla ID e progressive e fornisce Section Table al componente Bridge Segment Sections. |

Nel REV001 il file **[Sezioni_conci_REV001.xlsx](Sezioni_conci_REV001.xlsx)** è già inizializzato dalla tabella personale. Il componente è in **Read** e conserva l'importazione verificata. Il pannello originale visualizza ora la tabella proveniente da Excel.

1. Aprire l'Excel e compilare le celle gialle del foglio **Sezioni**.
2. Salvare il foglio. Se ci sono formule, ricalcolare e salvare prima della lettura: BridGHe usa i risultati numerici salvati, non esegue Excel.
3. Tornare in GH e premere **Run** in modalità **Read**.
4. Controllare messaggi e Section Table, quindi ricalcolare i modelli interessati dalle modifiche alle sezioni. L'importazione non esegue automaticamente Straus.

| Colonne | Significato |
|---|---|
| A: `ID` | Identificativo univoco del concio, da non modificare. |
| B–C: `s_inizio_mm`, `s_fine_mm` | Intervallo geometrico del concio, da non modificare. |
| D–E: `b_inf_mm`, `b_sup_mm` | Larghezze delle flange inferiore e superiore. |
| F: `h_mm` | Altezza totale della sezione a I. |
| G–H: `t_inf_mm`, `t_sup_mm` | Spessori delle flange. |
| I: `tw_mm` | Spessore dell'anima. |
| J: `r_mm` | Raggio di raccordo; può essere zero. |

Tutte le dimensioni sono in mm. **`*` eredita quella dimensione dalla Base Property collegata in GH**; un numero costituisce un valore esplicito nel foglio. Le celle vuote non vengono interpretate come zero o come eredità. Materiale e formulazione restano definiti dalla Base Property. La tabella riguarda le sezioni a I già gestite da Bridge Segment Sections.

Si possono riordinare le righe, mantenendo insieme tutte le colonne della riga. Il collegamento usa **ID e intervallo**, non la posizione nel foglio. Duplicati, righe mancanti, progressive non più coerenti e dimensioni non valide vengono segnalati.

Se cambia la suddivisione in conci, creare un nuovo template con un nuovo nome e riportare le dimensioni applicabili. Un cambiamento della sola mesh FEM non richiede una nuova tabella. **Create con Overwrite=True sostituisce il foglio**, quindi va usato deliberatamente; non è una sincronizzazione automatica delle modifiche.

Alla riapertura del GHX viene usata la tabella importata salvata nel componente, senza leggere automaticamente il disco. **Cached** indica questa situazione: dopo modifiche in Excel premere Run per aggiornarla. Se il file scelto cambia, la geometria dei conci non coincide o una lettura fallisce, non vengono fornite vecchie sezioni né sostituite silenziosamente con la sezione base.

Il controllo [REV001-inputs-validation.json](REV001-inputs-validation.json) documenta la conservazione della geometria e delle sezioni della revisione personale. Il backup precedente all'integrazione è nella cartella `artifacts/rev001-inputs` del repository.
