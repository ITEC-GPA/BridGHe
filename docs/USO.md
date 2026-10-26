# Uso e convenzioni

## Flusso del modello

1. Costruire materiali, sezioni e proprietà con i componenti esistenti. Collegare le proprietà a nodi, beam e plate; assegnare vincoli, carichi, gruppi e superelementi.
2. Assemblare il modello con `Build Model`, dichiarando le unità. Per il flusso BridgeChecks utilizzare **N–mm–MPa**; il controllo dei superelementi rifiuta modelli con unità differenti.
3. Esportare con `Straus7 R3 Export`, oppure eseguire `Straus7 R3 Analysis` scegliendo i casi richiesti. I risultati sono restituiti negli elementi di una copia del modello.
4. Per le fasi composte, analizzare i modelli con le proprietà di fase appropriate, combinarli mediante `Build BridGHe Model` e associare ogni caso a una sola fase: solo acciaio, lungo termine, istantanea, deformazione differita o istantanea.
5. Collegare i superelementi al verificatore BridgeChecks e i risultati ai componenti di estrazione/stampa.

Il verificatore adottato assume le azioni al **baricentro lordo della sezione della rispettiva fase**. Il motore gPC effettua il trasporto al baricentro efficace se necessario. Un modello con azioni riferite ad altri punti richiede un adattamento esplicito prima della verifica.

`Composite Beam Property` legge `n ritiro`, `Consider Concrete` e `Consider Rebars` per nome, anche nei GHX salvati con un ordine diverso degli ultimi tre ingressi. Il fallback a n∞ si applica soltanto se `n ritiro` è omesso. La lettura per indice poteva ignorare il valore collegato nei documenti con il vecchio ordine: nel GHX REV001, n ritiro = 12 diventava 18. La correzione ripristina il valore dichiarato e può quindi cambiare le proprietà omogeneizzate e le tensioni da ritiro di quei documenti; non modifica le formule gPC né i risultati FEM già memorizzati.

## Estrarre una proprietà dalle beam

**Get Beam Property**, in **14-Tools**, riceve `Beam` e restituisce `Property`: la Beam Property completa, con nome, ID, sezione, materiale e formulazione. Collegando una lista o un albero restituisce una proprietà per beam, mantenendo ordine e rami. Le proprietà sono copie indipendenti: modificarle a valle non cambia le beam originali. Le proprietà omogeneizzate conservano anche i dati di preview della sezione mista.

## Sostituire proprietà con coppie esplicite

**Beam Property Override**, in **14-Tools**, ha tre ingressi:

| Ingresso | Contenuto |
|---|---|
| `Beams` | Tutte le beam da aggiornare, comprese quelle che devono mantenere la proprietà attuale. |
| `Original Properties` | Lista di oggetti Beam Property da cercare nelle aste; si può ricavare con Get Beam Property. |
| `Replacement Properties` | Nuove Beam Property, abbinate per posizione alla lista precedente. I nomi possono essere diversi. |

Per esempio, la coppia `G1-C1_acciaio → G1-C1_n_inf` cambia tutte le beam con proprietà denominata esattamente `G1-C1_acciaio`, anche se sono sparse nella lista. Traversi e altre proprietà non elencate mantengono la propria assegnazione. La ricerca usa il **nome esatto della proprietà originale, distinguendo maiuscole e minuscole**, non l'ID, la posizione della beam o l'identità dell'oggetto in memoria. Le caratteristiche numeriche della proprietà originale non sono un ulteriore filtro.

Le due liste di proprietà devono avere la stessa lunghezza, per ciascun ramo elaborato da GH. Entrambe vuote restituiscono copie invariate. Coppie ripetute identiche sono ammesse; uno stesso nome originale associato a destinazioni diverse, o un nome sostitutivo con definizioni diverse, genera errore. I nomi originali non trovati sono segnalati in `Information` e come avviso. `Replaced` restituisce un booleano per beam nell'ordine dell'uscita `Beams`.

L'assegnazione sostituisce l'intera proprietà, **inclusi materiale, densità e formulazione**. Non applica la correzione di massa prevista da Bridge Segment Override: per le sezioni omogeneizzate preparare la densità voluta nella proprietà sostitutiva. Restano invariati ID, geometria, offset, rotazione, carichi, gruppi e attributi. Gli oggetti in ingresso non vengono modificati.

Se almeno una beam trova una sostituzione, il componente **svuota i risultati FEM di tutte le beam restituite**, comprese quelle con proprietà mantenuta: una variazione di rigidezza può cambiare l'equilibrio dell'intero modello. Riassemblare e ricalcolare il modello. Se non avviene alcuna sostituzione, anche i risultati sono conservati. La preview delle beam viene aggiornata con la proprietà assegnata, comprese le sezioni miste omogeneizzate.

## Selezione dei risultati

`Linear Static` propone casi ordinari e combinazioni globali. `Nonlinear Static` propone solo le combinazioni che definiscono gli incrementi. `Staged analysis` propone le combinazioni delle fasi nel formato `Combinazione [Fase]`; selezionare il nome completo. Un nome inesistente o ambiguo viene rifiutato prima dell'esportazione. La selezione filtra i risultati letti senza rinumerare gli incrementi del modello.

I casi presenti solo nelle combinazioni di fase vengono inclusi nel catalogo di esportazione. I carichi usano una numerazione locale per nome: gli `Id` dei casi del modello non sono modificati. Definizioni fisiche incompatibili con lo stesso nome generano errore. Le fasi sono abilitate esplicitamente nel solutore NLA. Prima della lettura sono controllati numero degli incrementi, convergenza dell'intera storia e fase nativa associata. Gli elementi non ancora attivi o rimossi non ricevono risultati di forza fittizi: l'assenza resta distinta dal valore zero.

Gli errori di esportazione impediscono l'avvio dell'analisi. Configurazioni attualmente non rappresentabili, come più casi di cedimenti imposti, rilasci multipli o supporti non gestiti, non vengono accettate come una soluzione parziale.

## Sezioni native Straus

I nuovi componenti hanno input dimensionali distinti, un output `Section` compatibile con `Beam Property`, e calcolano le proprietà tramite API R3. Richiedono la licenza API già al calcolo della sezione. Le dimensioni hanno le unità di lunghezza del modello; le aree sono L², le inerzie e J sono L⁴. Gli angoli BGL sono in gradi.

| Forma standard | Input dimensionali nell'ordine del componente |
|---|---|
| Circular Solid | D |
| Circular Hollow | D, T |
| Rectangle Solid | B, D |
| Rectangle Hollow | B, D, T1, T2 |
| Lipped Channel | B, D, L, T1, T2, T3 |
| Top Hat | B, D, L, T1, T2, T3 |
| I | B1, B2, D, T1, T2, T3 |
| T | B, D, L, T1, T2, T3 |
| Angle | B, D, T1, T2 |
| Z | B1, B2, D, T1, T2, T3 |
| Trapezoid Solid | B1, B2, D |
| Trapezoid Hollow | B1, B2, D, T1, T2 |
| Triangle Solid | B, D |
| Triangle Hollow | B, D, T1, T2 |
| Cruciform | B, D, T1, T2 |

Le sei geometrie BGL parametriche sono rettangolare cava, I, channel, T, angle e bulb flat; includono raggi di raccordo e inclinazioni dove previsti. Nomi, ordine e indici dei parametri sono in `migration/straus-sections.json`. La corrispondenza è stata ricavata dalle tabelle e dai disegni del manuale API R3 installato, pagine stampate 1003–1009. Per forme non familiari consultare quei disegni: `T1/T2` o `B1/B2` non devono essere reinterpretati per analogia con un'altra libreria.

`Straus BXS Section` legge un file BXS esistente. `Straus Library Section` legge un profilo BSL o BGL dalle librerie installate: nome libreria senza estensione e nome esatto del profilo. I materiali rimangono quelli del componente `Beam Property`; il materiale della libreria non viene importato.

Per BXS/BSL/BGL da libreria specificare `Length unit`: 0 m, 1 cm, 2 mm, 3 ft, 4 in. La stessa unità deve essere usata nel modello; una differenza genera errore. Conservare i file BXS e le librerie per le successive riesportazioni.

Restano `Generic Section` per proprietà assegnate numericamente e le sezioni da geometria della base, esportate mediante BXS. Le sezioni native nuove conservano geometria e proprietà nel modello, con contorni e anteprima solida estrusa discretizzata; i moduli resistenti elastici non letti dalla chiamata nativa sono `NaN`.

## Carichi mobili

| Componente | Funzione |
|---|---|
| Moving Point Force | Forza a tre componenti: grouped o floating; coordinate locali/globali, adiacenza e flag centrifugo. |
| Moving Distributed Force | Distribuzione rettangolare: grouped, leading, trailing, full length o floating. |
| Moving Heat Source | Sorgente termica rettangolare; esportazione per il solutore termico Straus. |
| Moving Vehicle | Raggruppa gli oggetti di carico; corsia singola/doppia, verso, velocità, tempo iniziale, gruppo e corsie abilitate. |
| Moving Centrifugal Data | Espressioni `K0`, `K1`, eventuali limiti e unità per `Fc = K0 + K1 Fz`. |
| Moving Load Template | Veicoli, fattori di corsia, multicorsia, transizione e tolleranze di distribuzione. |
| Moving Library Template | Template MLT della libreria Straus installata, con impostazioni native. |
| Moving Load Path | Geometria straight/curved/quadratic, superficie flat/curved, discretizzazione e target. |
| Attach Moving Paths | Duplica il modello e associa i percorsi; crea i casi di carico mancanti. |
| Moving Static Analysis | Converte i percorsi selezionati in casi statici, esegue LSA e legge i risultati negli elementi. |

Sequenza: **Point/Distributed → Vehicle → Template → Path → Attach Moving Paths → Moving Static Analysis**. In alternativa Template può provenire dalla libreria MLT. Il percorso richiede i tre punti della convenzione nativa Straus; non devono essere collineari. Numerazione delle corsie da 1; elenco vuoto nel veicolo significa tutte le corsie. Le coordinate e le azioni devono rispettare le unità del modello, la velocità è L/s, il tempo è in secondi.

Le modalità disponibili espongono gli oggetti dell'API: non sono stati introdotti veicoli normativi o coefficienti di traffico inventati. La definizione dei modelli di carico di progetto resta esplicita nei dati del template o nelle librerie MLT disponibili.

`Generate static cases=true` abilita la conversione durante l'esportazione. Per leggere automaticamente questi casi utilizzare `Moving Static Analysis`; i nomi generati sono restituiti nell'output `Cases`. Si tratta di posizioni discrete: convergenza con il numero di divisioni e ricerca delle posizioni critiche devono essere controllate. Non è implementata l'analisi a linee/superfici d'influenza con inviluppo continuo.

Target supportati nel modello nuovo: Any, Beam e Plate. Entity Set e Brick sono esplicitamente rifiutati in esportazione perché questa trasposizione non crea set nativi né elementi brick. La sorgente termica si esporta, ma non viene risolta da `Moving Static Analysis`, che è strutturale. Con template MLT occorre verificare il contenuto della libreria: il componente non ispeziona tutti gli oggetti nativi del template.

## Convenzioni delle azioni

`Build BridGHe Model` riceve le fasi da **Bridge Phase**, che raggruppa gli incrementi definiti con **Bridge Action** e **Bridge Shrinkage Action**. Quest'ultimo associa deformazione primaria e, quando previsti, modello/caso FEM degli effetti secondari; per omettere esplicitamente questi ultimi serve `Primary Only`. Le combinazioni hanno una colonna per Action ID, senza prefissi legati all'ordine dei modelli. `Composite Beam Strain` è mantenuto come componente obsoleto per le definizioni esistenti: i suoi carichi diretti a monte del Build vengono eliminati e ricostruiti dalle azioni. Vedere [fasi e azioni incrementali](FASI-E-AZIONI.md).

In `BeamForceResultModel`, i nomi storici `Mx/My` conservano rispettivamente i valori Straus `BM1/BM2`, cioè i momenti **nei piani** 1/2. Nel riferimento positivo della sezione si usa `(M1, M2) = (BM2, -BM1)`; successivamente si ruotano anche i valori nel riferimento di destinazione del checker, spesso `(-x,-y)`. Assegnare quel riferimento ai valori senza trasformarli invertiva impropriamente i segni: corretto e verificato nativamente il 17 ottobre 2026. Il momento positivo nel piano verticale comprime il lembo superiore. Gli assi misurati per una trave da X=0 a X=L con Angle=0 sono 1=+Y, 2=+Z, 3=+X. Vedere [convenzioni ufficiali Strand7 R3](https://www.strand7.com/strand7r3help/Content/Topics/ResultsInterpretation/ResultsInterpretationBeamInternalForceConvention.htm) e [assi locali/principali](https://www.strand7.com/strand7r3help/Content/Topics/Elements/ElementsBeamsPrincipalAxes.htm). Rigenerare le verifiche BridgeChecks ottenute con la precedente conversione.

Le prove del convertitore sono eseguite; la verifica completa dei segni dopo esportazione, analisi e rilettura richiede la licenza nativa. Non modificare nuovamente segni o ordine delle componenti nei componenti downstream per compensare la conversione.

## Riepilogo con etichette automatiche

Collegare **Tension Check** di **Super Element Check**, oppure **Results** di **Bridge Detail Check**, direttamente a **Results** di **Bridge Check Summary**. Lasciare **Labels vuoto**: non servono Panel, Series o altri componenti per costruire i nomi.

Il riepilogo usa i riferimenti contenuti nel risultato: per esempio `G2 / Beam 36 / s=1250 mm / DEMO`. `s` è la distanza dall'inizio della singola asta FEM, in mm; non è una coordinata globale né la progressiva del tracciato. Il superelemento è riportato quando presente. La fase resta nella colonna `Phase`. I riferimenti seguono il risultato anche dopo il controllo dei dettagli, un riordino o un Flatten; quest'ultimo raggruppa tutti gli esiti in un unico riepilogo.

Per **Bridge Section Check** e risultati senza riferimenti FEM, l'etichetta contiene percorso dell'albero, indice dell'item e fase, ad esempio `Result {0}[1] / Long`: non vengono dedotti nomi di aste o combinazioni inesistenti. Per ottenere i riferimenti completi da un vecchio documento, ricalcolare **Super Element Check** con la versione aggiornata. I documenti precedenti mantengono gli eventuali nomi manuali: scollegare il filo e cancellare eventuali dati persistenti di `Labels` per passare all'automatismo.

`Labels` rimane disponibile per nomi personalizzati: se usato, richiede un testo non vuoto per risultato, con lo stesso ordine e gli stessi rami. La generazione delle etichette non modifica sollecitazioni, fasi, rapporti, stati o note di verifica.

Per scegliere un formato senza scrivere testi, usare **Bridge Result Labels**, in **BridGHe → 13-Results**:

1. Collegare gli stessi risultati a `Results` del riepilogo e a `Results` di Bridge Result Labels.
2. Collegare una normale **Value List** a `Format`: le quattro voci vengono compilate automaticamente. La scelta è conservata nei ricalcoli e al salvataggio.
3. Collegare l'uscita `Labels` del nuovo componente all'ingresso `Labels` del riepilogo.

| Formato | Esempio |
|---|---|
| Completa (default) | `G2 / Beam 36 / s=1250 mm / DEMO` |
| Trave e stazione | `G2 / Beam 36 / s=1250 mm` |
| Combinazione | `DEMO` |
| Fase | Nome della fase del risultato |

Il componente conserva numero, ordine e rami dei risultati. Quando mancano i riferimenti FEM, segnala il campo non disponibile e usa la posizione nell'albero; il formato Fase usa sempre il nome della fase. I formati abbreviati possono avere nomi ripetuti: per identificare una sezione e combinazione insieme usare Completa. La Value List è un menu a scelta singola; non serve una checklist. L'[esempio 26](../example/26-riepilogo-verifiche-gpc/README.md) contiene il collegamento completo; l'[esempio 47 integrato](../example/47-pioli-irrigidimenti-verifiche/README.md) mostra il riepilogo automatico con Labels vuoto.

## Dettagli di ponti misti

I quattro nuovi componenti `Bridge Studs`, `Bridge Intermediate Stiffener`, `Bridge Support Stiffener` e `Bridge Detail Check` si trovano in **BridGHe-Checks / Bridge details**. Le definizioni espongono le opzioni scalari native gPC; l'ultimo componente riceve un risultato BridgeChecks e ricalcola la stessa storia con i dettagli assegnati. Unità degli irrigiditori: **kN**, mm; pioli: mm, MPa. I risultati elencano esplicitamente le unità. Un rapporto NaN indica una verifica non espressa da gPC, non un esito favorevole.

I dettagli non modificano il graticcio FEM. Lo stato limite resta quello del risultato sorgente: nessuna conversione automatica SLS→SLU. Esempi collegati e limiti: [ponti continui e curvi](../example/00-documentazione/ponti-12-18.md).


## Componenti gPC e grafici aggiunti

`Bridge Section Geometry`, `Bridge Rebar Row`, `Bridge Phase` e `Bridge Analysis Options` costruiscono direttamente i record gPC. `Bridge Section Check` accetta fasi attive in ordine, tre tipi di sezione (H, H inclinata, cassone) e dettagli opzionali. Le forze delle fasi sono in **kN**, i momenti in **kNm**, il ritiro in **microstrain**, le geometrie in **mm** e le tensioni in **MPa**. Le azioni devono essere già combinate/fattorizzate per lo stato limite scelto.

`Bridge Shear Results`, `Bridge Stud Results` e `Bridge Verification Results` espongono i risultati nativi. Per i pioli: flusso in N/mm (= kN/m), domanda/resistenza per piolo in kN. I controlli non applicabili o incompleti mantengono stato/avvisi e rapporto NaN. Non vengono trasformati in un esito positivo.

`Bridge Transverse Reinforcement`, `Bridge Fatigue` e `Bridge Box Options` aggiungono i dati richiesti dai controlli gPC; il componente non ricava automaticamente una storia di fatica dal traffico. `Bridge Detail Check` accetta anche opzioni e dettagli aggiuntivi.

`Bridge Shear Plot` e `Bridge Stud Plot`, insieme ai grafici delle azioni e tensioni, accettano un albero `{beam;station}` con le combinazioni nell'ordine del modello. Il percorso del file è opzionale; l'SVG è sempre restituito. Il PNG è rigenerato solo quando cambiano dati o destinazione. Le lacune non vengono unite e i salti alle stazioni non vengono smussati.

`Bridge Stud Plot` aggiunge i confronti PEd/PRd (Quantity 5) e flusso/resistenza per lunghezza (6). `Bridge Intermediate Stiffener Plot` e `Bridge Support Stiffener Plot` selezionano una voce GPC o l'inviluppo dei **WR** con `Check=0`. Collegando una Value List a Check, le voci si compilano automaticamente. `Stations` richiede le progressive reali in metri dall'inizio del superelemento; i dettagli sono rappresentati con punti separati. `Display` permette WR, domanda, limite o confronto per la singola voce. Un WR mancante rende incompleto l'inviluppo e viene segnalato, senza sostituirlo con zero. [Procedura e significato dei WR](GRAFICI.md#pioli-e-irrigiditori-domanda-resistenza-e-wr).

Per vedere i grafici nel canvas: collegare `SVG` a **Bridge Plot Preview**, gruppo **15-Preview**. Doppio clic sull'immagine apre la vista ingrandita. L'inviluppo grigio usa tutte le combinazioni; le uscite min/max riguardano le curve selezionate. Progressive in metri, azioni in kN/kNm e tensioni in MPa. [Guida e confronto con la versione originaria](GRAFICI.md).

## Input semplificati e preview

### Stati meccanici per asta dentro lo stesso concio

`Beam Moment Dispatch` separa le aste di un **singolo caso FEM** in Positive, Negative, Mixed, Zero e Missing. Legge tutte le stazioni disponibili, compresi gli estremi: non usa la somma o la media dei momenti. Richiedere al solutore stazioni e massimi/minimi sufficienti: non ricostruisce estremi non campionati. `Component=1` legge **My/BM2**, la flessione principale usata dall'adattatore del ponte; con assi di sezione coerenti il valore positivo comprime la soletta. `Sign=-1` inverte esplicitamente la convenzione. `Tolerance` è in N mm, quindi 1000 N mm = 0,001 kN m. Step multipli, stazioni duplicate o estremi mancanti sono rifiutati; risultati assenti producono Missing.

Collegare `States` a `Bridge State Override`, insieme alle beam originali in acciaio, agli ID dei conci e alle **Composite Sections complete**, una per concio. `Response=1` usa n∞, `Response=2` usa n0. Il componente assegna proprietà omogeneizzate non fessurate alle aste positive e proprietà **acciaio più armature, senza contributo del calcestruzzo**, alle negative. La geometria, i giunti costruttivi, gli ID e i carichi rimangono quelli di partenza. Le copie perdono i risultati FEM e devono essere ricalcolate. `Preserve Mass=True` conserva la massa fisica per unità di lunghezza, correggendo solo la densità della proprietà omogeneizzata. La preview conserva la geometria della sezione composta.

Un concio può quindi contenere entrambe le proprietà senza duplicare righe Excel. L'asta FEM che presenta entrambi i segni richiede `Mixed Policy`: **0 blocca**, **1 non fessurata sull'intera asta**, **2 fessurata sull'intera asta**. Le ultime due sono approssimazioni uniformi e nessuna è un limite generale di sicurezza. `Zero Policy` sceglie lo stato delle aste entro tolleranza. Per affinare il confine, ridurre la lunghezza massima degli elementi nella geometria comune e ricalcolare tutti i modelli; non spezzare soltanto un ramo dopo l'analisi. Questo flusso non introduce automaticamente nodi nello zero del momento.

La mappa sotto peso proprio è un'**ipotesi preliminare di rigidezza**, non la delimitazione normativa delle zone fessurate. Eventuali variazioni di carichi, appoggi, sezioni o mesh richiedono una nuova analisi di riferimento. Il controllo interno intercetta cambi di geometria e rigidezza delle aste, ma non certifica la validità di una vecchia cache FEM rispetto all'intero modello.

`Build BridGHe Model` conserva ora lo stato locale **per azione e per ID di asta**. `Super Element Check` usa questo dato nell'analisi GPC per mantenere coerenti inclusione del cls e baricentro lordo di applicazione delle sollecitazioni. Nel metodo Cumulative i contributi meccanici e il ritiro restano separati; le ipotesi accompagnano i risultati fino al report. Il ritiro non passa per `Bridge State Override`: nel [REV002 dell'esempio 50](../example/50-ponte-quattro-modelli/README.md) è esplicitamente non fessurato, sia nel FEM sia nel calcolo primario.

Metodi interessati: nuovi `BeamMomentClassification.Classify`, `BridgeStateOverride.Apply` e `BridgeCheckAdapter.AnalyzeWorkflow`; estesi `BridgeWorkflowBuilder.Build` e il percorso di `SuperElementCheckComponent` per leggere gli stati locali. Le porte dei componenti preesistenti sono invariate; i flussi senza attributi di stato conservano il precedente percorso Cumulative. Non sono cambiate chiamate Straus API.

`Bridge Report`, in **13-Results**, legge un template Word e genera una relazione dai risultati GPC. Collegare una Value List a Topics per scegliere i capitoli; il riepilogo di copertura resta sempre presente. La generazione è esplicita tramite Generate, senza scritture automatiche alla riapertura. [Template, impostazioni e limiti](REPORT.md) · [Esempio 48](../example/48-report-word/README.md).

La sezione finale di `Composite beam` è opzionale e assume quella iniziale. Il materiale delle armature è opzionale quando non vi sono armature attive. I nuovi record gPC hanno input con default e opzionalità esplicita. I componenti preesistenti con valori persistenti usano tali default; gli input fisicamente necessari restano obbligatori. L'inventario runtime delle porte è in `migration/current-components.json`.

La preview del modello comprende nodi, beam, plate e definizioni dei percorsi mobili. Le 21 famiglie parametriche Straus hanno contorni e solidi estrusi discretizzati; la precisione geometrica della preview non sostituisce le proprietà meccaniche native. Materiali, combinazioni, risultati numerici e opzioni non richiedono una geometria autonoma. I percorsi rettilinei mostrano l'asse; quelli curvi/quadratici mostrano i tre punti di definizione: controllare la traiettoria completa in Straus. Le nuove opzioni/sezioni gPC del verificatore sono dati; la geometria del ponte viene mostrata dagli elementi FEM.

`Beam Support`: K nelle unità F/L² del modello, direzioni -1/+1/-2/+2 principali, gap in L. Il comportamento compression-only richiede NLA. Riferimenti e convenzioni: `docs/beam-support-fix.json`.
