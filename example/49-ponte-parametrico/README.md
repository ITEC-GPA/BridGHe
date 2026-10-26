# 49 · Ponte parametrico: dagli input al modello FEM

Aggiornamento 26 ottobre 2026: questa definizione usa il [flusso modulare](../../docs/FLUSSO-MODULARE.md). Appoggi, travi, soletta, conci, diaframmi e mesh hanno componenti separati; gli input salvati sono stati conservati. I report precedenti descrivono la geometria di riferimento; il nuovo collaudo è raccolto nella [validazione del refactoring](../../docs/VALIDAZIONE-MODULARE-2026-10-26.md).

Aprire **[49-ponte-parametrico.ghx](49-ponte-parametrico.ghx)** con Rhino 8 e la Release aggiornata di BridGHe. Non occorre la licenza Straus per generare il modello.

La definizione procede **da sinistra a destra per dipendenze**, con gli input nominati a sinistra del componente che li usa e gli output conservati. I valori assegnati sono esterni: slider, toggle, Value List e pannelli. Anche gli ingressi opzionali hanno un parametro esterno, inizialmente vuoto. Ogni parametro conserva nel tooltip la spiegazione del relativo ingresso.

Ogni uscita sulla destra ha il nome completo sia nel parametro sia nella didascalia del relativo gruppo: resta leggibile anche con la visualizzazione a icone di Grasshopper.

Configurazione iniziale: tre campate da 30 m, tre travi a interasse 3,5 m, soletta larga 10 m e spessa 300 mm, diaframmi pieni, conci con lunghezza massima 12 m. Le sezioni iniziali sono dati dimostrativi modificabili, non un predimensionamento verificato. Unità: **N, mm, MPa**.

## Procedura

1. Nel gruppo **01**, impostare numero e luce delle campate, numero e interasse delle travi. Il tracciato, gli appoggi, i bordi della soletta e la disposizione proposta dei vincoli si aggiornano insieme.
2. Nel gruppo **02**, impostare quota degli assi delle travi, spessore e pendenza della soletta, mesh, sghembi e passo delle aste trasversali. La soletta è posta automaticamente a metà spessore; il parametro opzionale della quota consente di cambiarla.
3. Nel gruppo **03**, scegliere i conci e i diaframmi. Il toggle **Diaframmi reticolari** seleziona anima piena oppure reticolo; la Value List permette di scegliere X, V o Warren. I giunti di concio identificano pezzi di officina: non introducono cerniere.
4. Nel gruppo **04**, attivare o disattivare i controventi longitudinali e impostarne schema, campi, offset e connessione degli incroci.
5. Nel gruppo **05**, modificare materiali e sezioni base di travi principali, diaframmi pieni, correnti, montanti e diagonali. Nel gruppo **05b**, la tabella assegna una sezione a I a ogni concio delle travi principali: seguire la procedura qui sotto. Le altre famiglie conservano le proprie sezioni. L'uscita **Assi privi di proprietà** deve essere vuota.
6. Nel gruppo **06**, verificare lo schema degli appoggi e scegliere i collegamenti trave–soletta. Il modo iniziale usa link cinematici rigidi. Per aste deformabili selezionare il modo 1 e fornire la relativa proprietà; non è previsto un materiale artificialmente rigido implicito.
7. Nel gruppo **07**, sono disponibili B450C e la soletta con due file di armature per costruire successivamente le sezioni miste. La larghezza efficace è un input esplicito, distinto dalla larghezza geometrica totale dell'impalcato.
8. Collegare il modello scelto, a destra, al flusso di caricamento/esportazione/analisi Straus. La definizione non avvia analisi e non applica carichi. **Audit** controlla input e connessioni; non risolve la matrice di rigidezza.

## Configurazioni non regolari

| Parametro | Utilizzo e precedenza |
|---|---|
| Lunghezze campate diverse | Una lista, ad esempio `25000`, `40000`, `25000`, sostituisce numero e luce regolare. |
| Offset trasversali non uniformi | Lista crescente, ad esempio `-5000`, `-1500`, `2000`, `5500`, sostituisce numero e interasse regolare. Guardando verso le progressive crescenti: destra negativa, sinistra positiva. |
| Raggio in pianta | `0` rettilineo; positivo curva a sinistra, negativo a destra. Il generatore circolare ammette un angolo totale inferiore a 180°. |
| Tracciato Rhino 3D | Impostare una curva aperta e tangente-continua nel parametro Curve. La lunghezza 3D deve coincidere con la somma delle campate e dei prolungamenti; origine, rotazione e raggio vengono ignorati. |
| Giunti | Albero di progressive: ramo `{0}` per giunti comuni, `{1}` per la prima trave, ecc. I giunti espliciti sostituiscono la suddivisione automatica sulla trave interessata. |
| Progressive soletta / diaframmi | Liste ordinate in mm. Se presenti sostituiscono il passo automatico; gli estremi sono inclusi nelle fasce di soletta. |
| Pendenza trasversale | Lo slider imposta un valore costante. Per variazioni lungo il tracciato sostituirlo con un albero avente una coppia `progressiva, pendenza %` per ramo. |
| Quote degli assi delle travi | Lo slider applica una quota comune; può essere sostituito da una lista con una quota per trave. |
| Sghembi | Lo slider applica un angolo comune; può essere sostituito da una lista per appoggio. Gli appoggi di estremità sghembi richiedono prolungamenti sufficienti del tracciato. |

Per assegnare una lista a un parametro opzionale: collegare un pannello con un valore per riga, oppure usare **Set multiple…** nel menu del parametro, quando disponibile. Per curve, piani e alberi usare i corrispondenti parametri/componenti GH. Non scrivere segnaposto testuali negli ingressi numerici vuoti.

## Una sezione per ogni concio

Il componente **Bridge Segment Sections**, già collegato nell'esempio, genera sezioni a I in acciaio, anche asimmetriche, e le assegna alle travi tramite **Bridge FEM Members**. Non occorre creare a mano una catena di componenti per ogni concio. Le proprietà geometriche seguono lo stesso calcolo GPC del componente **I Section**.

1. Impostare geometria, giunti e **sezione base** mediante gli slider a sinistra.
2. Nel gruppo **05b**, aprire con doppio clic il pannello **SEZIONI CONCI [mm]**. È già compilato con una riga per concio. `G1-C2` indica la prima trave e il secondo concio di officina; la numerazione non è quella delle campate né quella delle aste FEM.
3. Lasciare `*` nelle dimensioni che devono seguire gli slider della sezione base. Sostituire `*` con un numero soltanto nelle celle da differenziare. La modifica entra immediatamente nelle aste del modello e nella preview.
4. Se si cambia il numero di travi, la lunghezza del ponte o la posizione dei giunti, premere **Tabella conci** sul componente, oppure scegliere **Aggiorna tabella conci** dal suo menu. Il comando aggiorna il pannello esterno. Conserva gli override solo quando coincidono sia ID sia progressive; i conci nuovi o modificati ripartono dalla sezione base (`*`). L'uscita **Diagnostica sezioni e aggiornamento tabella conci** elenca aggiunte, reset e rimozioni. Undo di GH ripristina il pannello precedente.
5. Controllare **Dimensioni effettive di ciascuna sezione [mm]**, gli ID e le **Proprietà FEM di ciascun concio** a destra. L'ordine è sempre trave, poi concio, anche se le righe del pannello vengono riordinate.

Ogni riga ha **10 colonne**, separate da `;` oppure da tabulazioni:

| Colonna | Significato | Modifica |
|---|---|---|
| ID | Identità `Gx-Cy` del concio | Generata dalla geometria |
| s_inizio_mm, s_fine_mm | Progressive lungo il tracciato | Generate; non usarle per spostare i giunti |
| b_inf_mm, b_sup_mm | Larghezze ala inferiore e superiore | Numero oppure `*` |
| h_mm | Altezza totale della sezione | Numero oppure `*` |
| t_inf_mm, t_sup_mm | Spessori ala inferiore e superiore | Numero oppure `*` |
| tw_mm | Spessore dell'anima | Numero oppure `*` |
| r_mm | Raggio dei raccordi anima–ali | Numero, `0` per spigoli vivi, oppure `*` |

Esempio: il primo concio dell'esempio iniziale va da 0 a 11250 mm. Per aumentare **solo l'ala inferiore a 50 mm**, lasciare le altre dimensioni legate agli slider:

```text
G1-C1; 0; 11250; *; *; *; 50; *; *; *
```

Per definire tutte le quote di quel concio, la stessa riga diventa:

```text
G1-C1; 0; 11250; 600; 600; 2500; 50; 30; 16; 0
```

Le altre righe devono restare nel pannello. Sono ammessi sia punto sia virgola decimale, senza separatori delle migliaia. L'intestazione che inizia con `#` è un commento. Una cella vuota è un errore: per ereditare la dimensione base scrivere `*`. Per tornare completamente agli slider, svuotare il pannello e premere **Tabella conci**.

L'aggiornamento è esplicito: il componente non sovrascrive il testo durante il calcolo o la riapertura del file. In presenza di righe duplicate, mancanti, estranee o con progressive non più corrispondenti, blocca l'assegnazione e indica il problema. Una variazione della sola mesh non richiede aggiornamenti, perché le sezioni sono associate ai conci fisici.

**Sezioni miste e fasi:** utilizzare l'uscita **Sezione acciaio di ciascun concio** insieme agli **ID ordinati delle sezioni**, poi generare le sezioni miste e gli override delle fasi. La singola uscita **Sezione acciaio BASE** è soltanto il riferimento degli slider: non rappresenta tutte le sezioni dopo gli override della tabella. Materiale, formulazione Beam e impostazione torsionale sono ereditati dalla proprietà base; questo componente non dimensiona i conci e non assegna verifiche o carichi.

**Quote delle travi:** modificare altezza o asimmetria può spostare il baricentro e la quota dell'ala superiore rispetto all'asse FEM. L'uscita **Quota ala superiore rispetto al baricentro, per concio [mm]** permette di controllarlo. Il componente segnala differenze rispetto alla sezione base, ma non sposta automaticamente gli assi né i link: adeguare geometria/offset se si vuole mantenere l'estradosso allineato alla soletta.

## Uscite disponibili

- **Geometria:** tracciato, assi longitudinali, diaframmi per famiglia, controventi, punti degli appoggi, giunti dei conci, nodi geometrici, volume e bordi della soletta, assi trasversali alla quota reale, eccentricità, piani locali, progressive, larghezze tributarie, topologia e ID.
- **Entità FEM:** carpenteria, fasce di soletta, proprietà per famiglia e per fascia, nodi vincolati, piani/etichette degli appoggi, link rigidi o aste di collegamento, tutti i nodi e i beam del modello assemblato.
- **Modelli:** sola carpenteria per la fase acciaio/getto; carpenteria più graticcio trasversale per il seguito del flusso. Il secondo modello aggiunge le fasce di soletta e i collegamenti eccentrici.
- **Dati per il misto:** sezioni di ogni concio con ID e proprietà FEM, sezione base, materiali acciaio/cls/B450C e soletta con due file di armature.
- **Controlli:** elementi privi di proprietà, esito e descrizioni dell'audit.

L'uscita degli assi dei controventi mostra i candidati generati dal relativo componente. Per sapere quali siano effettivamente assemblati usare **Geometria carpenteria con opzione controventi** e il modello FEM, che rispettano il toggle di attivazione.

## Convenzioni meccaniche da mantenere

La quota degli assi delle travi è indipendente dalle dimensioni della sezione. Il valore iniziale `−1550 mm` corrisponde a una sezione simmetrica alta 2500 mm sotto una soletta da 300 mm. Cambiando sezione, aggiornare il riferimento al baricentro desiderato. I link collegano tale riferimento alle aste della soletta.

Gli appoggi proposti sono: prima trave fissa nelle traslazioni al primo appoggio, guidata longitudinalmente agli altri; tutte le altre posizioni sono multidirezionali. Le rotazioni sono libere. La lista di override permette un codice unico oppure un codice per punto, in ordine **appoggio → trave**: `0` fisso, `1` guida longitudinale, `2` multidirezionale, `3` elastico. Il modo elastico richiede rigidezze appropriate. Per selezioni parziali con distribuzioni particolari, collegare direttamente una lista di tipi al componente Bearings, coerente con i punti selezionati. Verificare l'orientamento delle guide per ponti curvi/sghembi.

Le fasce trasversali usano sezioni rettangolari di spessore pari alla soletta e larghezza pari al tratto tributario lungo il tracciato, anche con passo non uniforme. Le larghezze alle estremità sono ridotte. Non sono shell FEM: la mesh chiusa in uscita è il volume geometrico della soletta. La rigidezza torsionale e la discretizzazione del graticcio curvo/sghembo richiedono calibrazione.

Il modello iniziale conserva le proprietà **in acciaio delle travi longitudinali**. L'azione composta longitudinale deve essere introdotta tramite le proprietà omogeneizzate e gli override delle fasi già usati nel plugin. Il calcestruzzo delle fasce può essere sostituito con un materiale a modulo efficace coerente con la fase. Non sovrapporre una shell longitudinale al contributo della soletta già incluso nelle sezioni omogeneizzate.

Il reticolo genera geometria; la formulazione iniziale dei suoi elementi è **Beam** con giunti continui. Le Value List delle proprietà permettono **Truss** solo assiale dove appropriato. Svincoli, collegamenti e stabilità devono seguire lo schema meccanico scelto.

## Rigenerazione e test

Eseguire dalla root del repository:

```powershell
& '.\example\49-ponte-parametrico\genera.ps1'
```

Se il GHX esiste già, lo script richiede `-Force` e ne salva una copia con data e ora in `artifacts/parametric-bridge-backups`. Il generatore è [ParametricBridgeExample.cs](../../tests/BridGHe.Smoke/ParametricBridgeExample.cs); il modello finale si modifica normalmente da Grasshopper.

Il report [validation.json](validation.json) viene scritto soltanto dopo soluzione, salvataggio, riapertura e controlli delle varianti. I test verificano aggiornamenti del numero di campate/travi, diaframmi reticolari, curve nei due sensi, quantità di elementi, appoggi, connettività, proprietà assegnate, assenza di risultati FEM inventati e posizione degli input sul canvas. Non costituiscono un'analisi Straus: qui il solutore non viene invocato.

Sono verificati anche nomi e didascalie delle uscite, compilazione/aggiornamento del pannello conci, modifica locale di `G1-C1`, permanenza degli override alla riapertura e indipendenza dalla mesh. Il test `--regression bridge-segment-sections` confronta area, baricentro e inerzia di una sezione asimmetrica con un calcolo indipendente a tre rettangoli e rifiuta tabelle incomplete, duplicate, non aggiornate e dimensioni non valide.
