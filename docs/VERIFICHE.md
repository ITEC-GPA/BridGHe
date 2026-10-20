# Verifiche della Release

## Collaudo con licenza Straus — 20 ottobre 2026

Completate le prove rimaste sospese per la licenza. Il riepilogo versionato è [licensed-native-20261020.json](../example/00-documentazione/validazioni/licensed-native-20261020.json).

| Prova eseguita | Esito e campo verificato |
|---|---|
| Appoggi orientati | UCS, verso dell'asse X, vincoli e molle riletti dall'API R3; nessuna soluzione FEM in questo test |
| Collegamenti rigidi | Esportazione e rilettura del link spaziale XYZ; nessuna soluzione FEM in questo test |
| Carichi mobili, esempio 21 | 5 scenari, 80 posizioni, **5.377 confronti** superati; nessun warning del solutore |
| Ponte 19 REV003 | **4 nuove analisi**, 20 controlli, equilibrio G1/G2/Q/ritiro, merge e checker senza errori GH |
| Stazioni del ponte 19 | **12.192 valori** contro ricampionamento API su 98 aste; **378 risultati di sezione** nel grafo GH |
| Cinque contributi di sezione, esempio 22 | **30 confronti** Straus/gPC superati: acciaio, lungo termine, istantaneo e due incrementi di ritiro |

Carichi mobili: errori assoluti massimi 2.48×10⁻¹⁰ N sull'equilibrio, 7.05×10⁻¹¹ N sulla reazione destra, 1.06×10⁻¹⁴ mm sulla freccia e 3.87×10⁻⁷ N·mm sui momenti. Ponte 19: scarto massimo di ricampionamento 6.37×10⁻¹² N sulle forze e 9.54×10⁻⁷ N·mm sui momenti. Tolleranze originali mantenute.

Il collaudo ha corretto **due errori nei benchmark**, ciascuno in un commit: gravità erroneamente locale nel percorso inverso; numero e posizioni del tandem che omettevano ingresso/uscita. Ora il tandem usa tutta la luce e produce 16 casi per 13 divisioni. Non sono cambiate le chiamate native, le convenzioni o le formule del plugin. Dettagli e differenze in [esempio 21](../example/21-benchmark-carichi-mobili/README.md).

Nel test 22 il warning `Stages on load cases not supported` è atteso: gli attributi di fase dei casi sono metadata del checker gPC e non stage nativi Straus. Si confrontano tre casi meccanici risolti e due contributi di ritiro di sezione; questa prova non convalida una nuova sequenza di costruzione nativa. Gli esempi 33–45 restano grafi geometrici/offline; non sono stati presentati come nuove analisi di ponte.

Verifica finale: Release con 0 avvisi e 0 errori, sincronizzata in `bin/Release`; 218 controlli, 54 gruppi principali e 2 grafici superati, 238 valori di baseline esattamente uguali. Riaperti 43 GHX con 2.642 oggetti; catalogo di 45 esempi e collegamenti locali validato. Guida Word di 28 pagine renderizzata: le tre pagine modificate sono state ispezionate e le altre 25 sono identiche pixel per pixel alla versione già verificata.

## Strumenti del modello di ponte — 20 ottobre 2026

Release compilata senza warning/errori e sincronizzata in `bin/Release`. `tools/build.ps1 -Smoke` completato: **218 controlli**, **54 gruppi di regressione principali e 2 grafici**, **238 valori di baseline esattamente invariati**. Il runtime registra 169 componenti e 8 parametri, tutti con GUID e icone distinti.

Tredici esempi nuovi, **33–45**, risolti, salvati e riaperti con il motore Grasshopper: consigliatore delle campate, sezioni per progressiva, assegnazione, appoggi, connessioni, controventi, soletta, costruzione, dettagli, frecce, fatica, mappa e ponte integrato. Il modello dell'esempio 45 non ha errori o warning di audit e conserva tutti gli elementi delle sorgenti. I report per ciascun esempio indicano `solverInvoked=false`.

Le prove di servizio comprendono confronto esaustivo delle alternative del consigliatore, cambi di mesh e identità fisiche, regole sovrapposte, rastremazioni, X/V/Warren, topologia dei collegamenti, area della soletta, selezioni delle fasi, attivazione per gruppi, quantità e volume dei dettagli, parabola di freccia nota, rainflow contato a mano e dati mancanti nelle mappe. Gli esempi 42/43 usano dati sintetici dichiarati; il 44 ricalcola controlli locali con gPC su azioni assegnate, senza FEM globale.

Correzioni emerse dall'integrazione: costruttore vuoto per ripristinare `GH_PlateProperty` da un GHX; diagnostica delle fasi mancanti nell'audit; esclusione degli stage nativi per selezioni parziali dentro lo stesso gruppo o elementi senza gruppo. Nel grafo completo Elements è appiattito per formare un solo modello.

Il primo tentativo nativo aveva restituito `St7Init -8`, hardware lock non disponibile. Il successivo collaudo con licenza ha superato `--bridge-bearings-native` e `--bridge-connections-native`, oltre alle analisi riportate sopra. La guida Word è aggiornata con gli esiti del collaudo.

## Stato precedente — 19 ottobre 2026

Ambiente effettivo: Windows x64, Rhino 8.35 avviato con Rhino.Inside, runtime .NET 8, SDK 9.0.319, riferimenti Rhino/Grasshopper 8.27.26019.16021, Straus7 R3.1.5 x64 con licenza API disponibile. Gli esempi Grasshopper vengono salvati, riaperti e risolti con i componenti reali in un server isolato dai plugin estranei del profilo.

## Esiti eseguiti

| Prova | Esito | Evidenza riproducibile |
|---|---|---|
| Build Release | 0 errori, 0 avvisi | `tools/build.ps1` |
| Smoke Release attuale | 187 controlli superati | `tools/build.ps1 -Smoke` |
| Famiglie di sezioni native | 21 famiglie Straus superate | `artifacts/straus-license-check.log` |
| Regressioni | 36 gruppi superati | `--regression all` |
| Baseline numerica prima/dopo | 238 valori su 14 casi, tutti esattamente uguali | `bridge-numerical-baseline`, fixture versionata; tolleranza 1e-12 |
| Dieci esempi Straus | 121 misure, nessun fallimento | `example/00-documentazione/validazioni/native.json` |
| Dieci esempi Grasshopper | 111 misure, nessun fallimento | `example/00-documentazione/validazioni/grasshopper.json` |
| Cinque contributi di fase nativi/gPC | 30 misure superate | `example/00-documentazione/validazioni/bridgeChecks.json` |
| Appoggi elastici distribuiti | 29 misure native superate | `--beam-support`, `docs/beam-support-fix.json` |
| Esempio 11: graticcio completo | 357 misure superate | `example/11-ponte-graticcio/validation.json` |
| Esempi 12-15: continui e curvi | 290 misure, inclusi 20 benchmark di ritiro | cartelle `example`, `benchmarks.json` |
| Esempi 16-18: spina centrale | 212 misure superate | tre cartelle `example/1[678]-*` |
| Esempio 19: acciaio → override → merge → verifica | 162 controlli, 4 analisi Straus, 62 stazioni gPC; riapertura del GHX verificata | `example/19-flusso-acciaio-misto/archivio/REV001/validation.json` |
| Contratti e GUID | 104 file di contratto, 146 GUID unici, nessun errore | `tools/audit_contracts.py --baseline-only` |
| Inventario e icone | 137 componenti + 8 parametri istanziati, 145 icone di oggetti; 147 PNG includendo plugin e categoria | `migration/current-components.json`, `assets/icons/manifest.json` |

Una correzione finale è esclusivamente grafica: il raggio equivalente di una sezione generica ora usa le unità del modello, eliminando un fattore 0,001 improprio. Sono state rieseguite tutte le 30 regressioni, inclusi dimensioni della preview, confronto dei gruppi e baseline numerica invariata. Corretto anche il tipo usato da Equals(object) degli String Group, che poteva provocare ricorsione infinita confrontando un rilascio.

I controlli software non coincidono con verifiche resistenti di progetto. In particolare, il rifiuto esplicito di torsione fuori campo è un test corretto del comportamento del software, non un esito positivo per il ponte.

## Riesecuzione dopo il riordino delle palette e delle fasi

L'ultima build Release termina senza errori o avvisi: 186 controlli smoke e 35 gruppi di regressione superati. I 238 valori della baseline rimangono esattamente uguali. Sono stati rieseguiti anche il confronto nativo/gPC dei cinque contributi di fase (30 misure) e le 21 famiglie di sezioni Straus. I report delle altre suite nella tabella documentano le esecuzioni precedenti; non sono stati rigenerati da questo riordino.

L'esempio 19 usa quattro analisi incrementali reali: acciaio, misto a lungo termine, misto istantaneo e ritiro FEM. Il contributo di autotensione del ritiro viene aggiunto separatamente. Sono verificati le proprietà generiche esportate, gli equilibri, le soluzioni analitiche, il matching esplicito dei casi, entrambe le file di armatura e la conservazione esatta delle sollecitazioni nel merge. Il GHX contiene risultati salvati e viene riaperto con il solutore disattivato; il verificatore restituisce nuovamente 62 stazioni.

I controlli delle preview composte comprendono solidi validi, contatto soletta/flangia, sezioni asimmetriche, copia e archivio dopo Beam Override, ingombri e armature schematiche. Le opzioni globali persistenti e quelle locali non modificano il modello FEM. La cattura del viewport con Rhino nascosto ha restituito un errore nativo GPU (`SEHException`): la resa a schermo di queste nuove opzioni resta da ispezionare in Rhino interattivo. Il file `example/19-flusso-acciaio-misto/archivio/REV001/preview-sezioni-composte.3dm` contiene la geometria verificata per questa ispezione.

L'output distribuibile è solo `bin/Release`. Il pacchetto include l'esempio 19 e un manifest SHA-256 dei 13 file alla radice, verificato sullo ZIP generato; i dati intermedi della compilazione restano sotto `obj`.

## Copertura numerica

Le prove comprendono mensola e trave appoggiata con soluzione analitica, patch di plate, carico mobile, incrementi NLA, attivazione/rimozione di elementi, carico/scarico per fasi e selezione delle combinazioni di fase. Sono verificati spostamenti, reazioni, segni e azioni restituite agli elementi.

I cinque esempi di costruzione per fasi sono **06–10**. Il confronto aggiuntivo dei cinque contributi composti distingue acciaio, lungo termine, istantaneo e due incrementi di ritiro. Il graticcio dell'esempio 11 include anche cinque incrementi nativi di getto separati. Gli incrementi elastici con rigidezze diverse non sono confusi con i risultati totali cumulati della costruzione staged.

Il nuovo flusso gPC è eseguito per H, H inclinata e cassone, con tre fasi meccaniche più ritiro. Tensioni, tau e flusso dei pioli coincidono esattamente con le chiamate dirette alla libreria. Sono controllati anche fasi disattivate, opzioni, archiviazione dei record, input opzionali e restituzione degli stati/avvisi dei controlli.

La baseline comprende instabilità attiva/disattiva, azioni positive/nulle/negative, soletta inclusa/esclusa e ritiro. Non è una validazione esaustiva di ogni dominio normativo o di ogni geometria gPC.

## Convergenza dei graticci

La soglia è mantenuta al **2%**: massimo scarto di Uz sui nodi comuni, riferito al picco della soluzione raffinata. Tutti e quattro i contributi vengono risolti anche sulla mesh doppia.

| Esempio | Elementi per campata: base → raffinata | Massimo scarto relativo |
|---|---|---|
| 12, continuo 20+20 m | 12 → 24 | 0,2972% |
| 13, continuo 20+30+20 m | 12 → 24 | 0,1516% |
| 14, curvo R80 | 48 → 96 | 0,5831% |
| 15, curvo R120 | 24 → 48 | 1,1128% |
| 16, spina 30 m | 24 → 48 | 0,9696% |
| 17, spina 20+30+20 m | 48 → 96 | 1,6629% |
| 18, spina curva R120 | 96 → 192 | 1,1782% |

Le mesh iniziali che non rispettavano la soglia sono state raffinate, senza ampliare la tolleranza. Gli esempi con spina hanno geometrie e ritiro equivalente differenti fra trave centrale e laterali; nei rettilinei viene verificata la simmetria delle deformate laterali. I dettagli delle ipotesi sono nel [README della suite](../example/00-documentazione/ponti-12-18.md).

## Preview e grafici

Aggiornamento grafici del 19 ottobre 2026: build Release dei soli commit pubblicabili, audit dei contratti (105 file, 147 GUID) e suite completa superati, con **187 controlli smoke e 36 gruppi di regressione**. Baseline: 238 valori invariati. La copia di verifica è stata creata sotto `obj` per escludere le modifiche locali preesistenti ai componenti di sezione composta e merge; quei sorgenti non sono stati modificati da questo intervento.

I grafici ripristinano inviluppi, progressive in metri, separatori delle beam ed estremi annotati. Il nuovo `Bridge Plot Preview` è provato con SVG.NET sul canvas GH, cache, lista di grafici, ridimensionamento, errori, dati mancanti e rilascio delle immagini. L'esempio `example/20-visualizzatore-grafici/plot-preview.ghx` è stato salvato, riaperto e ricalcolato. Il renderer SVG.NET non usa il viewport GPU; il precedente limite di cattura della preview geometrica 3D non si applica a questi grafici 2D. Le chiamate di aggiornamento della finestra ingrandita sono testate automaticamente; il clic manuale e l'interazione con lo zoom restano da provare nella sessione interattiva dell'utente.

Sono verificate 21 famiglie parametriche di sezione Straus: contorni presenti, estrusione chiusa, copia/archivio fedele e area geometrica della preview entro il 3% dell'area esatta. Le proprietà meccaniche restano quelle native; la discretizzazione grafica non entra nel solutore. I 21 contorni sono stati ispezionati anche visivamente. Librerie esterne e ogni possibile file BXS non sono coperti dal test delle 21 famiglie.

Le regressioni della preview controllano geometrie aggiornate, elementi aggiunti/rimossi e cambiamenti ai percorsi mobili. Per i percorsi curvi/quadratici sono rappresentati solo i punti di definizione. Parametri numerici, materiali e opzioni non hanno una geometria autonoma; non viene dichiarata una preview 3D completa di ogni oggetto.

I test dei grafici coprono stazioni sparse, combinazioni, campioni singoli, salti, NaN, valori costanti, escaping XML e cultura numerica. Le tavole PDF degli esempi sono state renderizzate e ispezionate su tutte le pagine.

## Differenze autorizzate e campo ancora da completare

La correzione di `BeamSupport` cambia i modelli interessati dall'esportazione errata. [Confronto prima/dopo, casi lineari e contatto NLA](beam-support-fix.json). Per gli altri percorsi coperti dalla baseline non è stata rilevata variazione numerica.

Restano verifiche di progetto specifiche: torsione/warping e flessione minore delle H nel percorso automatico dei superelementi; soletta fessurata nelle zone negative; shear lag; storia reologica evolutiva; connessione deformabile e trasferimenti di estremità; fatica con spettro di traffico; instabilità globale; soletta locale e armature; dispositivi di appoggio; inviluppi normativi e azioni orizzontali. I nuovi input gPC per fatica, armatura trasversale e cassone espongono la capacità della libreria, ma non derivano automaticamente le azioni richieste dal modello globale.

I carichi mobili sono analizzati come posizioni statiche discrete; non è implementato un inviluppo completo mediante superfici d'influenza. Le sorgenti termiche vengono esportate, ma non sono oggetto del collaudo strutturale. Il traffico uniforme degli esempi 12–18 è dimostrativo, non un modello normativo LM1.
## Asse dei grafici e matching del ritiro — 19 ottobre 2026

Verifica sulla copia pulita del commit `8b2effc`: compilazione Release senza avvisi/errori, audit dei contratti superato, **187 smoke check e 36 gruppi di regressione** superati; **238 valori di baseline esattamente invariati**. Inclusi tacche ancorate allo zero con passi 1–2–5, SVG/PNG e preview GH, Value List dell'ultima fase alimentata dai casi FEM, distinzione fra forze e deformazioni imposte, serializzazione della sezione mista con i due ordini degli input e conservazione dei GUID delle porte.

Sul GHX utente corretto in `19-flusso-acciaio-misto_REV002.ghx`: **12 beam, 6 casi, 5 combinazioni, 275 risultati**; **223 campioni FEM** conservati dal merge con differenza massima zero sulle sei componenti. Il modello Straus del ritiro è stato ricalcolato con n = 12 e la cache aggiornata, poi il file finale è stato riaperto e verificato fino ai grafici. Le combinazioni XLSX sono state internalizzate nel solo processo di test, senza automazione Excel/TT Toolbox. Il foglio consegnato resta identico all'originale e non contiene il caso `4-RITIRO_FEM`: questo contributo non entra nelle cinque combinazioni.

Dettagli e differenze numeriche: [README REV002](../example/19-flusso-acciaio-misto/archivio/REV002/README_REV002.md) e [report](../example/19-flusso-acciaio-misto/archivio/REV002/validation_REV002.json). Gli altri cambiamenti locali preesistenti ai due componenti non sono inclusi nei commit di questa verifica.
