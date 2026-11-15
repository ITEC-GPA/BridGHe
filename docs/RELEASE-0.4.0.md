# BridGHe 0.4.0

Stato: testo F8 da pubblicare dopo il gate del pacchetto definitivo. Le librerie GPC sono quelle del set ufficiale del 14 novembre 2026 (AW): GPCModel 5.51.0.1, GPCModelData 0.3.0.31, GPC.Model.Persistence 3.15.0.3, GPC.Archive 1.2.0.4, GPC.Converter 2.22.0.3, GPCChecker.Concrete 0.37.0.2, GPCChecker.Steel 2.134.0.1, GPCChecker.CompositeBridge 1.31.0.2, GPCUtilities 3.0.0.0, GPCGeometry 2.3.0.0 e DelaunayMesh 2.0.0.14.

## Cosa c'è di nuovo

Le cutoff bar si possono esportare in Straus7; Beam Property supporta anche il doppio angolare. Sono casi che nella 0.3.0 si fermavano all'esportazione. La definizione della struttura, l'analisi e le verifiche usano ora i dati GPC comuni.

Gli esempi aggiornati comprendono combinazioni Excel, fasi, verifiche e report. I modelli analizzati internalizzati nel formato attuale conservano i risultati. Per trasferire i risultati esterni conserva il relativo `.bghr` e aggiorna Output Path; il `.st7` serve a riaprire il modello nel solutore, non a leggere il sidecar. Bridge Report gestisce il grande esempio 50 senza la precedente duplicazione dei dati in memoria; il Word può comunque richiedere diversi minuti.

## Cosa cambia nei risultati

Nelle travi composte omogeneizzate **I11 è l'inerzia rispetto all'asse orizzontale e I22 rispetto a quello verticale**, con angolo zero. Per alcune solette larghe questo corregge un errore già presente nella 0.3.0: nei file esaminati I11 cambia del −11,97% nel 19 REV003 e del −5,08% nel 19 REV002 archiviato e nel 99 REV001. Le revisioni confrontate del 50 e del 99 REV002–REV004 non cambiano per questa correzione.

Torsione e aree efficaci a taglio delle sezioni Brep e composte possono differire dalla precedente valutazione; cambiano anche alcuni valori del modulo elastico del calcestruzzo e le ultime cifre delle verifiche. **Ricalcola analisi e verifiche dei tuoi modelli**: il confronto favorevole di un esempio non garantisce uguaglianza per ogni struttura. Nei modelli in PSI la dilatazione termica di Custom Material è ora convertita da 1/°F a 1/°C, quindi i risultati termici cambiano. Table Material accetta la tabella sforzi-deformazioni anche con dilatazione termica e densità (prima l'errore «Stress list length != strain list length»); Model Audit riconosce correttamente gli incrementi validi delle fasi.

## Cosa è stato tolto

Il valore dell'ingresso Strain del componente obsoleto Composite Beam Strain non viene più usato: per il ritiro usa Bridge Shrinkage Action. Il componente resta riconoscibile nei vecchi documenti.

Le funi Cable vengono rifiutate con un messaggio prima di Straus7. Cutoff Bar e Truss sono alternative solo quando rappresentano il comportamento strutturale voluto. Restano non supportati spostamenti imposti, piastre con più di quattro vertici e sezioni Brep formate da regioni separate. Gli script che usavano i vecchi tipi FEM interni devono essere adattati.

## Compatibilità dei file

Conserva gli originali 0.3.0. Gli ingressi storici supportati vengono letti per valore, ma risultati e cache precedenti richiedono un nuovo Run; dati di analisi internalizzati non recuperabili vanno ricostruiti dai componenti a monte. La 0.3.0 **non legge** i nuovi dati internalizzati salvati dalla 0.4.0. I dati con schema 2 vengono rifiutati: usa la versione che li ha prodotti, senza alterare il file.

Super Element Check richiede **Build BridGHe Model**; un superelemento contiene solo travi. Build Model rifiuta ID uguali con dati diversi. Negli inviluppi Step è sempre zero: caso e posizione si distinguono dal nome del caso. Controlla i coefficienti delle combinazioni dopo la riapertura.

## Correzioni completate nei passi 89–92

- **Staged Construction / Load Combinations:** i coefficienti salvati restano anche se Build Model non ha ancora restituito il catalogo dopo la riapertura. Un catalogo danneggiato viene segnalato senza cancellare le combinazioni; se i casi non sono noti, il dialogo chiede Build Model e non consente di confermare.
- **Aggiornamento dei cataloghi:** scompare l'errore «object expired during a solution»; la modifica del parametro è differita alla fine della soluzione.
- **Alberi di combinazioni:** aggiunta e rimozione dei casi aggiornano tutti i rami, preservano i percorsi e sono annullabili con Undo. Il dialogo resta chiuso sugli alberi con più rami.
- **Cataloghi in conflitto:** un parametro autonomo collegato a componenti con cataloghi differenti conserva i dati, segnala i componenti/casi incompatibili e non entra in un ricalcolo continuo. Il dialogo riapre quando i cataloghi coincidono.
- **Messaggi dei dati:** i risultati validi di Node, Beam e Plate mostrano l'elemento analizzato, senza la precedente dicitura Null. L'errore d'identità di Composite Beam distingue sezione iniziale/finale, riporta il nome e indica Composite Beam Property per ricostruirla.
- **File 0.3.0:** Bridge Family Property mostra il nome della proprietà al posto della vecchia descrizione FEM completa; il testo delle regole di sezione resta.
- **Esempi 23, 24, 42, 43, 46, 47, 48:** rifatti nel formato 0.4.0 con approvazione dell'utente. Il 23 contiene di nuovo i nodi agli estremi e il vincolo A; DISCONNECTED è voluto. Il 46 non contiene il link rigido. Le dimensioni complessive documentate passano da 11,6 a 4,9 MB; non è una nuova misura di questa revisione delle note.

## Correzioni completate nei passi 93–94

- **Pacchetto:** il plug-in contiene tutte le 11 DLL GPC che usa, comprese GPC.Converter, GPC.Model.Persistence, GPC.Archive e GPCModelData, descritte da `gpc-release-set.json` accanto al plug-in.
- **Custom Material in PSI:** la dilatazione termica α si dà in 1/°F, come dice l'etichetta, ed è convertita in 1/°C (× 1,8). Prima, anche nella 0.3.0, passava senza conversione: i risultati termici dei modelli in PSI cambiano. kNm, SI e Nmm non cambiano.
- **Messaggi dei componenti:** sono in inglese e non parlano più di modello FEM (Bridge Action, Bridge Shrinkage Action, Decompose Model, Super Element Check, Composite Section Linear Tension Analysis, Bridge Detail Check, Beam Moment Dispatch, Bridge State Override, Bridge Report, i parametri Beam/Node/Plate/Section/Material, i risultati accanto al modello di Straus R3 Analysis e Moving Static Analysis, Node/Beam/Plate Results e gli strumenti sui risultati). Significato e limiti dichiarati sono gli stessi.
- **Dati salvati che non si leggono:** un dato salvato che non si legge (Bridge Detail, Bridge Segments, Bridge Diaphragms, carichi mobili, Composite Beam, Bridge Action/Phase di una versione precedente), o che non si salva, ora lo dice all'apertura o al salvataggio del file; prima restava vuoto senza messaggio.
- **Plate Pressure:** un Value non valido dice quali tipi sono ammessi («Value must be a number, numeric text or a vector. Connect one of these types.»); un testo che non è un numero lo dice («Value '…' is not a valid number in the current number format.») invece dell'eccezione generica.

## Come installare

Chiudi Rhino e conserva una copia della precedente installazione. Da `X:\ITEC-MODELLI\5-RHINO\SGBIM-GR_PLUGIN\BridGHe` copia **tutta la cartella**, comprese DLL e manifest, nella destinazione locale prevista. Fai puntare Grasshopper a una sola copia del plug-in, disattivando il vecchio percorso; riavvia Rhino. Se usi anche Arachne e ReBear, installa i pacchetti dello stesso rilascio F8. Non mescolare singole DLL. Trasferisci gli esempi con workbook, template e file esterni associati.

## Cosa fare se qualcosa non va

Conserva il file originale e segnala versione, revisione dell'esempio, componente, unità, messaggio completo e screenshot. Un avviso di bin o librerie incoerenti richiede di sistemare l'installazione prima di confrontare i risultati. Se un dato è obsoleto, ricostruiscilo e ricalcola; non sostituire con zero un risultato mancante. Per tornare indietro usa il file originale e l'intera precedente installazione.

## Provenienza delle note

Decisioni e changelog: correzioni-P2B, voci 0.4.0 e passi 89–94. Misure sulle inerzie: rapporto INR del 12 novembre; confronti limitati ai file indicati. Il manifest del pacchetto identifica le versioni effettive. I test e gli esiti storici non sostituiscono il collaudo F8.
