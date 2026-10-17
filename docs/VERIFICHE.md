# Verifiche della versione 0.1.0

## Ponte misto a graticcio — esempio 11

Esecuzione del 17 ottobre 2026: **357 misure superate**, nessun fallimento, tramite `--bridge-grillage --gh`. La definizione GHX è stata riaperta e risolta con i componenti di costruzione, analisi, merge e MeCheck2; 240 sezioni hanno attraversato cinque contributi di fase. Riferimenti, unità e tolleranze: [guida del ponte](../examples/bridge-grillage/README.md); dati grezzi: [validation.json](../examples/bridge-grillage/validation.json).

Copertura aggiuntiva: tre rigidezze del graticcio, equilibrio e risposta analitica simmetrica, reciprocità, veicolo eccentrico e speculare, 23 posizioni mobili, cinque incrementi nativi di getto, mesh 12/24/48, sensibilità GJ. Il traffico eccentrico produce torsione e viene correttamente rifiutato dal verificatore N–M–V. La combinazione simmetrica è controllata elasticamente a sezione lorda, senza instabilità locale o certificazione normativa.

Dopo i fix è stata rieseguita la suite generale: **202 controlli smoke/nativi e 21 gruppi di regressione** superati. Il successivo controllo isolato `bridge-example-parameters` verifica i limiti della ricetta prima dell'analisi; non altera il modello di default. Porte e GUID: audit invariato, 16 componenti MeCheck2 e 137 GUID complessivi.

## Risultati effettivamente ottenuti

Ambiente: Windows x64, Rhino 8 avviato senza finestra mediante Rhino.Inside, runtime .NET 8, compilazione con SDK 9.0.319. Ultimo collaudo nativo: 17 ottobre 2026. Grasshopper installato 8.35.26251.13001; riferimenti di compilazione 8.27.26019.16021. Straus7 R3.1.5 x64 con licenza API disponibile.

- Build Release: **0 errori, 0 avvisi**.
- Audit delle porte: **103 componenti/parametri mantenuti**, inclusi **16 MeCheck2**, nessuna differenza non ammessa.
- **137 GUID distinti** contando plugin e oggetti Grasshopper; nessuna collisione con i GUID della base.
- Smoke test e sezioni native: **202 controlli superati e 19 gruppi di regressione superati**, inclusa l'istanziazione di tutti i **136 componenti/parametri registrabili** nel runtime Rhino 8. I 202 comprendono i precedenti 177 controlli, 21 sezioni native, area analitica BGL rettangolare e tre controlli sulla mensola nativa.
- Icone: tutte le 136 risorse presenti, distinte e associate al proprio oggetto a 24×24 px; logo plugin a 24 px; registrazione delle due categorie con icone a 16 px verificata nella sessione precedente; il controllo corrente sul profilo completo è rimasto bloccato durante il caricamento dei plugin installati. Confronto dei pixel opachi e alpha esatto, tolleranza di due livelli su 255 sui canali premoltiplicati dei bordi semitrasparenti per gli arrotondamenti GDI+.
- Prove analitiche: area/inerzia del cerchio, sezione omogeneizzata di solo acciaio, tensione N/A, flessione con segno dei lembi, taglio VQ/(It).
- Fasi: azioni istantanee e a lungo termine distinte, accumulo delle deformazioni imposte, errore esplicito per risultati mancanti alla stazione.
- Persistenza: archivio del modello con nodi, beam, attributi e risultati; identità condivisa dei casi di carico; archivio Grasshopper di sezioni, proprietà e proprietà composte; dati e copia profonda dei carichi mobili.
- Input non supportati: rifiuto di torsione nel percorso MeCheck2 adottato, offset verticale non reinterpretato, mobilità dei carichi non valida.

Il log corrente è `artifacts/bugfix-validation.log`; audit e inventario sono in `artifacts/contract-audit.json` e `artifacts/component-runtime.json`. Gli artefatti sono esclusi da Git. La suite isolata termina con `PASS TOTAL 177`, `REGRESSION TOTAL 19` e codice zero.

Il precedente totale 179 includeva due controlli di registrazione delle categorie. Ora sono opzionali perché `Grasshopper.Instances.ComponentServer` carica anche i plugin del profilo: il tentativo corrente è rimasto bloccato dopo il controllo delle icone del plugin ed è stato interrotto. Il precedente log riportava inoltre errori di plugin Tekla estranei a BridGHe. Per eseguire quei due controlli d'integrazione: `dotnet run --project tests/BridGHe.Smoke -c Release -- --installed-plugins`. Non sono conteggiati come superati nella validazione corrente.

## Regressioni della revisione correttiva

Sono coperti i difetti R1–R9 della revisione e i problemi aggiuntivi emersi durante le correzioni. Ogni fix ha un commit distinto; elenco in [MIGRAZIONE](MIGRAZIONE.md#revisione-correttiva).

- Somma di forze e momenti nodali, identità dei casi omonimi, rifiuto delle definizioni incompatibili.
- Merge dei dati nodali, topologia delle mesh multifaccia, connettività FEM delle piastre e indipendenza degli input.
- GUID degli elementi attraverso output, duplicazione e archivio GH; copie indipendenti con riferimenti condivisi conservati all'interno della copia.
- Ripristino delle travi composte, sostituzione dei solidi obsoleti, validità di sezioni native e proprietà cutoff.
- Casi esclusivi delle fasi, selezione qualificata `Combinazione [Fase]`, conservazione degli indici di incremento e rifiuto dei casi incompatibili con il solutore.
- Indici dei risultati e dei nodi confrontati con somme analitiche e ricerca esaustiva; coefficienti negativi, conversione dei momenti, stazioni normalizzate, soglie di tolleranza, risultati mancanti/duplicati.
- Aggiornamento degli attributi dopo archivio, sostituzione/svuotamento dei punti delle piastre, travi incomplete e invalidazione della cache delle bounding box.

La separazione fra errori bloccanti e avvisi nell'esportatore e le descrizioni di validità dei Goo sono state controllate sul codice e compilate. Il comportamento delle chiamate native nei percorsi di errore non è attestato senza una prova API.

### Microbenchmark MeCheck2

Un elemento, 8 casi, 10 combinazioni, mediana di tre esecuzioni dopo riscaldamento. L'indice viene ricostruito una volta per esecuzione e il suo costo è incluso. Checksum analitico delle azioni verificato in ogni esecuzione.

| Stazioni | Revisione iniziale | Suite corrente | Allocazioni correnti |
|---|---:|---:|---:|
| 100 | 64,6 ms | 7,6 ms | 10,3 MiB |
| 400 | 482,3 ms | 19,9 ms | 41,2 MiB |
| 800 | 1059,5 ms | 40,1 ms | 82,4 MiB |

La regressione mirata precedente aveva misurato 37,6 ms a 800 stazioni. I tempi dipendono dalla sessione e non rappresentano il tempo di verifica di un ponte completo. L'allocazione resta rilevante per gli oggetti delle azioni gPC. La ricerca nodale usa un RTree riutilizzato e conserva la selezione per ordine delle coordinate e la soglia stretta `< tolerance`; è verificata contro ricerca esaustiva, senza attribuirle un'accelerazione numerica non misurata.

Restano da profilare copie su modelli completi, risultati e anteprime. Le copie profonde usano l'archivio già presente per garantire l'isolamento; non viene dichiarata una riduzione dei loro costi. L'esecuzione del solutore resta sincrona e serializzata dal gate API.

## Collaudo nativo e definizioni Grasshopper

Il 17 ottobre 2026 `St7Init` ha inizializzato correttamente l'API con la licenza disponibile. Il precedente errore di hardware lock del 16 ottobre è superato. Le prove native richieste ora falliscono esplicitamente se la licenza non è disponibile; non vengono conteggiate come passate tramite uno skip.

Sono stati eseguiti i **10 benchmark** descritti in [examples/README.md](../examples/README.md), con **5 casi di costruzione per fasi**. I report riportano valore atteso, valore ottenuto, tolleranza ed esito di ogni misura. Le dieci definizioni `.ghx` sono anche salvate, riaperte e risolte tramite i componenti Grasshopper reali, compresi `Node Results`, `Beam Results` e `Plate Results`. Il modello iniziale è internalizzato nelle definizioni; le ricette C# consentono di rigenerarlo con dati diversi.

Esito della sessione: **121 confronti superati nei benchmark nativi**, **111 confronti ripetuti attraverso le definizioni Grasshopper**, **30 controlli superati nella catena delle fasi MeCheck2**. Questi conteggi si aggiungono ai 202 controlli smoke/nativi e ai 19 gruppi di regressione; i 111 confronti GH sono intenzionalmente gli stessi riferimenti numerici, verificati attraverso un diverso punto di ingresso.

Copertura: equilibrio nodale, somma delle azioni, combinazioni lineari, carico distribuito, patch test shell, asse mobile in 13 posizioni, incrementi NLA, carico/scarico per fasi, nascita senza tensioni, rimozione con redistribuzione, selezione parziale degli incrementi e assenza di risultati per gli elementi inattivi. Per NLA si controllano convergenza dell'intera storia e fase nativa degli incrementi prima di associare i risultati agli elementi.

Un benchmark aggiuntivo collega i momenti nativi al verificatore MeCheck2 e confronta **cinque contributi di fase**: solo acciaio, lungo termine, istantanea e due ritiri imposti. Controlla quattro tensioni di lembo dopo ogni fase con integrali di sezione indipendenti. Include una prova fisica del segno del momento: assi nativi, deformata verso l'alto per coppia terminale che produce flessione positiva e compressione del lembo superiore. Il riferimento tiene conto dell'idealizzazione a parete sottile delle flange adottata dal motore gPC nelle fasi composte; dettagli e formule sono nel README degli esempi.

Le prove hanno individuato e corretto cinque difetti: ricalcolo improprio delle BGL, fasi NLA non abilitate, lettura di forze su elementi inattivi, accettazione senza controllo esplicito della convergenza e inversione dei segni nel riferimento MeCheck2. I commit e i metodi modificati sono elencati in [MIGRAZIONE](MIGRAZIONE.md). In particolare, le vecchie verifiche MeCheck2 devono essere rigenerate dopo la correzione dei segni.

Per ripetere:

```powershell
.\tools\build.ps1 -Native -Examples -Package
```

Le 21 famiglie parametriche sono state calcolate con l'API e hanno area e inerzie positive; per la BGL rettangolare cava è verificata anche l'area analitica. Ciò non costituisce una prova di tutte le combinazioni geometriche ammissibili. Restano da collaudare sistematicamente BXS e librerie BSL/BGL, tutte le modalità di carico mobile, stabilità/convergenza su ponti completi e la totalità dei componenti di verifica. I benchmark non attestano tutte le verifiche normative di un ponte.

## Limiti espliciti della trasposizione

| Ambito | Stato |
|---|---|
| MeCheck2 | Tutti i componenti e le porte ricreati. Il percorso adottato del motore gPC copre una singola H diritta con soletta rettangolare e fino a due file di barre a diametro uniforme per fila, sotto N–Mx–Vy. Flessione deviata, torsione, taglio trasversale e sezioni ruotate/asimmetriche sono rifiutati. |
| Altri checker della base | I componenti esterni a MeCheck2 sono stati trasposti e compilati; non sono stati eseguiti benchmark numerici completi per ciascuno. Le convenzioni storiche di questi percorsi richiedono una validazione dedicata prima dell’uso progettuale. |
| Metodo di verifica | La trasposizione usa il motore gPC disponibile, con opzioni Eurocode 4 / SLE rara per la risposta tensionale. Non attesta tutte le verifiche NTC/Eurocodici di un ponte, né equivalenza numerica completa al vecchio checker. |
| Ritiro calcolato dal tempo | `epsilonCS=NaN`; resta utilizzabile il ritiro **imposto** nei componenti di fase/self tension. |
| TauB | `NaN` e warning finché non è definita una corrispondenza verificata. |
| Effective Epsilon | `true` non supportato dal nuovo adattatore; usare `false` per il metodo corrente a larghezze efficaci. |
| Vertical Offset della sezione composta | Supportato solo zero: il parametro della nuova libreria ha significato differente. |
| Sezioni native nuove | Calcolo delle 21 famiglie parametriche verificato; librerie/BXS e spazio completo delle geometrie da collaudare. Niente anteprima solida estrusa; moduli resistenti non letti = `NaN`. |
| Mobile | Casi statici discreti; niente inviluppo da linee d'influenza. Target Entity Set/Brick non esportabili. Analisi termica da eseguire in Straus. |
| Link Property legacy nei supporti | Porta mantenuta, utilizzo esplicitamente rifiutato; disponibili le rigidezze dei supporti gestite da R3. |
| Cataloghi esterni | Selezione dai vecchi database rimossa; usare componenti materiali/sezioni. |
| Dati legacy e bake | Nuovi GUID e archivio BGHA1; nessuna importazione automatica dei vecchi documenti. Bake di geometria nativa senza metadati strutturali. |

## Validazione successiva, concreta

1. Eseguire il benchmark nativo predisposto e confrontare anche verso delle reazioni, SF1/SF2, BM1/BM2 e orientamento di beam orizzontali, verticali e ruotati.
2. Confrontare standard/BGL/BXS/librerie con il pannello proprietà Straus, comprese sezioni non simmetriche e unità m/mm.
3. Verificare un singolo asse mobile su trave appoggiata confrontando reazioni e momento con la soluzione statica per ogni posizione; poi multicorsia, carico distribuito, curva e centrifuga. Raffinare le divisioni per stabilizzare i massimi.
4. Verificare un caso composto per fase, con modello elastico indipendente, confermando baricentri, rapporti modulari, segni, ritiro e riduzioni di classe 4. Controllare separatamente i risultati con soletta esclusa.
5. Eseguire almeno un caso NLA e uno staged, confrontando nomi/indici dei casi salvati e risultati riassociati agli elementi.

Questi passaggi non sono sostituiti dalla sola compilazione. I controlli software già eseguiti rendono la trasposizione riproducibile e ispezionabile; la validazione del verificatore strutturale completo rimane distinta.

## Suite ponti del 18 ottobre 2026

Quattro esempi 12–15: **290 misure superate**, di cui 20 sui benchmark del ritiro libero/impedito e 270 nei quattro ponti (81, 85, 52, 52). Ogni GHX è stato riaperto e risolto con i componenti reali. Le verifiche numeriche comprendono tutti e quattro i contributi, equilibrio, convergenza della mesh, confronti analitici dei continui, tensioni primarie+secondarie, pioli e dettagli gPC.

| Esempio | Mesh base→raffinata per campata | Massimo scarto Uz / picco di riferimento |
|---|---|---|
| 12, 20+20 m | 12→24 | 0,2972% |
| 13, 20+30+20 m | 12→24 | 0,1516% |
| 14, curvo R80 | 48→96 | 0,5831% |
| 15, curvo R120 | 24→48 | 1,1128% |

La prima mesh dei curvi non rispettava il 2%; è stata raffinata senza allargare la tolleranza. I file distribuiti corrispondono alle mesh della tabella. Il caso R80 ha una freccia di circa 136 mm nella fase di solo acciaio: è un esempio numerico flessibile, non un dimensionamento approvato; warping e instabilità globale non sono inclusi.

Test generale della build aggiornata: **181 controlli smoke e 23 gruppi di regressione**, incluse le icone dei 140 oggetti Grasshopper e la persistenza dei dettagli gPC. Audit: 103 oggetti conservati, 16 MeCheck2, 141 GUID contando il plugin, nessun errore. Il bug nativo dei percorsi è verificato separatamente sul caso 01 a mensola (9 controlli).

Per i curvi viene verificato anche che MeCheck2 segnali la torsione H non supportata: **questo è un test del software, non una verifica resistente del ponte**. Limiti, fonti e file riproducibili sono nel [README della suite](../examples/bridge-suite/README.md).
