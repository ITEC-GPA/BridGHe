# BridGHe

Plugin Grasshopper per **Rhino 8 / .NET 8 / Windows x64**: modello parametrico → Straus7 R3 → risultati negli elementi → verifiche e dettagli di ponti misti con gPC.

L'inventario corrente comprende **220 componenti e 8 parametri**, con 229 GUID distinti incluso il plugin; comprende anche gli adattatori storici nascosti dalla barra. I contratti aggiornati sono in [current-components.json](migration/current-components.json). Il [flusso geometrico modulare](docs/FLUSSO-MODULARE.md) separa appoggi, travi, soletta, conci, diaframmi e mesh. Gli strumenti operativi comprendono audit del modello, copie di fase, combinazioni per Action ID, inviluppi concomitanti, riepilogo GPC, confronti numerici, incrementi di ritiro e corsie mobili.

## Esempi

[Indice unico degli esempi numerati](example/README.md). Ogni caso raccoglie definizione, istruzioni e calcoli nella propria cartella.

I nuovi esempi **23–28** mostrano i 12 strumenti operativi; la [guida Word](docs/BridGHe_Guida_operativa.docx) li spiega nei capitoli 17–21. Le prove di inviluppo usano campioni controllati e il riepilogo esegue il calcolo di sezione GPC, senza nuove analisi Straus.

- [01–10: casi analitici, carichi mobili e cinque esempi di costruzione per fasi](example/00-documentazione/benchmark-01-10.md).
- [11: ponte misto a graticcio, quattro travi, veicolo mobile e tre rigidezze](example/11-ponte-graticcio/README.md).
- [12–15: ponti continui e curvi, tre contributi meccanici più ritiro](example/00-documentazione/ponti-12-18.md).
- [16–18: spina centrale H2000 e due laterali H1600, una/tre campate o due campate curve](example/00-documentazione/ponti-12-18.md#esempi-aggiuntivi-16-18-spina-centrale-e-due-travi-laterali). Tre fasi più ritiro differenziale, 212 controlli superati.

I modelli sono esempi riproducibili, con ipotesi, unità, confronti analitici e tolleranze dichiarati. Il percorso automatico dei superelementi H adotta la verifica N–Mx–Vy, escludendo torsione, M2 e V1 con ipotesi esplicita; il componente nativo gPC gestisce anche geometrie a cassone e anima inclinata. Non equivale a una verifica completa di ogni ponte. Il [collaudo del 26 ottobre](docs/VALIDAZIONE-MODULARE-2026-10-26.md) distingue prove numeriche, analisi Straus e verifica dei file GHX.

## Funzioni

- Nodi, travi, plate, materiali, proprietà, vincoli, carichi, combinazioni e costruzione per fasi; esportazione e analisi Straus7 R3.
- 23 componenti di sezione Straus: 21 famiglie parametriche, BXS e librerie BSL/BGL; restano le sezioni generiche e da geometria.
- Carichi mobili: forze, distribuzioni, sorgenti termiche, veicoli, corsie, centrifuga, template, percorsi e analisi dei casi statici generati.
- Geometrie gPC H, H inclinata e cassone; fasi, armatura, opzioni di verifica, pioli, irrigiditori, armatura trasversale, fatica e dettagli del cassone.
- Estrazione di tensioni, taglio, flusso di connessione, domanda/resistenza dei pioli ed esiti gPC. Grafici SVG e PNG senza interpolare i campioni.
- Preview del modello assemblato e delle sezioni native; icone SVG/PNG originali per tutti i componenti.

## Compilare e installare

Dalla root, con SDK .NET 8 o successivo e Python 3:

```powershell
.\tools\build.ps1 -Native -Package
```

BridGHe **0.3.0** usa le DLL GPC fissate in **`lib/GPC`**, con manifest, versioni e SHA-256. Non occorre compilare i repository fratelli per compilare il plugin. [GpcReferences.props](build/GpcReferences.props) verifica l'integrità del set prima di risolvere i riferimenti; il manifest viene distribuito come `bin/Release/gpc-release-set.json`. Le DLL comuni a BridGHe e ReBear provengono dallo stesso set.

Al caricamento in Grasshopper il plugin controlla le librerie GPC con cui calcola. Se gira dal `bin/Release` di un checkout, confronta le DLL accanto al plugin con `lib/GPC`: un `bin` non ricompilato dopo un aggiornamento del set viene segnalato. Confronta inoltre le DLL GPC caricate in Rhino con `gpc-release-set.json`: un altro plugin, per esempio ReBear, può aver caricato prima un set diverso. Le differenze compaiono nella riga di comando di Rhino e come avviso su Super Element Check, Bridge Detail Check, Bridge Section Check e sugli altri componenti che calcolano con GPC; un set non verificabile è segnalato solo nella riga di comando. Il controllo è provato dalla regressione `gpc-release-set` del progetto Smoke.

Il **set ufficiale dell'8 novembre 2026**, con `candidate=false`, comprende Model **5.50.0.2**, ModelData **0.3.0.30**, Concrete **0.37.0.1**, Steel **2.134.0.0** e CompositeBridge **1.31.0.1**, dai commit Model `ff318f87` e Checker `9e513417`. È stato adottato il 10 novembre per allineare le DLL comuni a ReBear; Utilities, Geometry e DelaunayMesh mantengono i binari precedenti. La build aggiunge System.Text.Json **10.0.13** e le relative dipendenze .NET 8. Non occorrono opzioni per candidati. Provenienza e collaudo corrente sono nel [resoconto di stabilizzazione](docs/STABILIZZAZIONE-2026-11-10.md); il [rilascio del 2 novembre](docs/GPC-RILASCIO-2026-11-02.md) documenta il set precedente e i suoi confronti prestazionali.

Output da installare: **`bin/Release/BridGHe.Grasshopper.gha`**, insieme alle DLL nella stessa cartella. L'evento MSBuild `BridGHePostBuild` in `Directory.Build.targets` parte **dopo ogni build riuscita**, sia da Visual Studio sia da `dotnet build`: per il plugin Release copia l'intero output in `bin/Release`, rimuove i file non più prodotti ed elimina le vecchie cartelle di compilazione sparse, comprese le precedenti sottocartelle di `bin`. Funziona anche con build incrementali e compilando direttamente il `.csproj`.

`bin` contiene soltanto `Release`. Cache MSBuild/NuGet, output di lavoro e test sono raccolti nell'unica `obj` della root, necessaria per build incrementali e riferimenti fra progetti. Debug resta in `obj` e non sovrascrive la Release. Una compilazione fallita non pubblica il plugin. I pacchetti ZIP sono in `artifacts/releases`; risultati Straus, esempi e librerie dei repository fratelli sono conservati. La pulizia controlla confini e junction; per simularla: `tools/clean-builds.ps1 -WhatIf`.

Prima di compilare, chiudere Rhino se sta usando gli output da aggiornare o eliminare. Se una vecchia DLL è in uso, il post-build segnala l'errore prima di eliminare le cartelle precedenti; ripetere la build dopo aver chiuso il processo.

Test del sistema di build (21 controlli su copia/pulizia e 9 su build MSBuild reali in un progetto isolato):

```powershell
.\tools\test-build-cleanup.ps1
.\tools\test-post-build.ps1
```

Chiudere Rhino, estrarre il pacchetto sotto la cartella Libraries di Grasshopper e tenere insieme `.gha` e DLL. Avviare Rhino 8 con .NET 8; unica tab `BridGHe`. Le DLL native Straus e Rhino non sono distribuite. La licenza API Straus è necessaria anche per calcolare le sezioni native. Percorso predefinito: `C:\Program Files\Straus7 R31`; in alternativa impostare `BRIDGHE_STRAUS_HOME` prima di avviare Rhino.

## Riprodurre le verifiche

Il [resoconto di stabilizzazione del 10 novembre 2026](docs/STABILIZZAZIONE-2026-11-10.md) distingue i controlli rieseguiti dagli esiti storici e descrive il recupero dei risultati esterni. I conteggi nei paragrafi storici non certificano automaticamente il checkout corrente.

Gli esempi GHX con risultati esterni richiedono anche i file `.bghr`: il pacchetto include quelli referenziati, dopo verifica SHA-256, e rifiuta riferimenti mancanti o alterati. I file nativi Straus restano esclusi. Dopo il trasferimento su un altro computer, aggiornare gli input `Output Path` dei componenti di analisi al percorso `.st7` nella nuova cartella `calcoli`, accanto al `.bghr` corrispondente, quindi ricalcolare Grasshopper con `Run` disattivato. Il percorso `.st7` serve a individuare il `.bghr`; non occorre il file `.st7` per rileggere i risultati archiviati. Il resto del grafo può comunque richiedere Straus, ad esempio per calcolare sezioni native.

Se un `.bghr` manca o non coincide con l'hash salvato, il componente non restituisce quei risultati e conserva il riferimento anche nei successivi salvataggi. Ripristinare il file corretto, oppure aggiornare `Output Path`, e ricalcolare; `Run` esegue invece una nuova analisi. Un nuovo modello o un reset esplicito possono invalidare i risultati precedenti.

```powershell
.\tools\build.ps1 -Native -Examples -BridgeGrillage -BridgeSuite -BridgeSpine -Package
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --beam-support
```

Gli script rigenerano e sovrascrivono gli esempi: salvare separatamente le modifiche manuali. I test Grasshopper usano un server componenti isolato per non caricare i plugin estranei installati nel profilo utente.

Per le correzioni di salvataggio e packaging, dopo la compilazione dei test:

```powershell
dotnet build tests/BridGHe.Smoke -c Release
.\tools\test_regressions.ps1 -Groups analysis-results-file
py -m unittest discover -s tools -p test_package_results.py
```

## Documentazione

- [Guida operativa Word: fasi, verifiche, dettagli e nuovo flusso geometrico modulare](docs/BridGHe_Guida_operativa.docx).
- [Fasi numerate, azioni incrementali e ritiro](docs/FASI-E-AZIONI.md).
- [Metodi cambiati, rimozioni e compatibilità](docs/MIGRAZIONE.md).
- [Uso e convenzioni](docs/USO.md).
- [Verifiche eseguite e limiti](docs/VERIFICHE.md).
- [Catalogo dei componenti e porte attuali](docs/COMPONENTI.md).
- [Dal tracciato al modello: dodici strumenti e tredici esempi](docs/BRIDGE-DESIGN.md).
- [Icone e rigenerazione](docs/ICONE.md).
- [Correzione degli appoggi distribuiti e differenze numeriche](docs/beam-support-fix.json).
- [Misura dell'ottimizzazione delle letture di taglio](docs/shear-performance.json).

Organizzazione delle palette e componenti non presenti negli esempi: [audit di utilizzo](docs/USO-COMPONENTI.md). Sezioni in `03-Sections`, appoggi in `07-Attributes`, tutti i carichi in `09-Loads`.

## Soletta con due file di armatura

`Concrete Slab Section` conserva i primi quattro input e aggiunge `Bottom rebar area` e `Bottom concrete cover`. Le aree sono totali sulla larghezza della soletta, in mm²; le distanze sono misurate dalla faccia della soletta all'asse delle barre, in mm. Area zero disattiva la fila. Gli archivi precedenti mantengono la sola fila superiore. Le due file entrano nelle proprietà omogeneizzate e nel verificatore gPC, con interpolazione di area e posizione lungo le travi a sezione variabile.

## Visualizzazione

Il menu **BridGHe → Visualizzazione…** e il componente **BridGHe Panel** aprono le preferenze persistenti. Sono disponibili geometria FEM, sezioni fisiche, superfici, armature schematiche, carichi, appoggi, svincoli, assi locali, etichette, scala dei simboli e filtro per nome esatto del caso. **Display Settings → Preview BridGHe Model**, nel gruppo **15-Preview**, applica opzioni locali; disattivare la preview automatica globale per evitare sovrapposizioni con gli output a monte. L'interruttore generale continua a controllare tutte le preview.

Le proprietà omogeneizzate conservano acciaio e soletta nella preview delle beam dopo l'override. Le armature sono rappresentate con tre linee schematiche per fila attiva: gli input definiscono aree totali, non una distinta ferri. I test verificano i solidi Rhino, gli archivi, le opzioni locali e l'assenza di modifiche ai dati FEM. La cattura della viewport GPU nel processo Rhino nascosto restituisce un errore nativo su questa macchina; la prova visuale interattiva rimane da eseguire in Rhino. Il comando diagnostico `--regression display-settings --preview-capture` è separato dai test geometrici.

## Esempio del flusso acciaio → misto

[Guida all'esempio 19](example/19-flusso-acciaio-misto/README.md): REV003 contiene un ponte a due campate da 30 m e tre travi, con modello acciaio, override n∞, n0 e n ritiro, quattro analisi Straus e verifica gPC della spina centrale. Il collaudo nativo del 20 ottobre 2026 supera quattro nuove analisi, 20 controlli e 12.192 confronti di stazioni; il checker produce 378 risultati di sezione senza errori GH. `Bridge Action` e `Bridge Shrinkage Action` associano casi e strain alle fasi numerate; le combinazioni usano gli Action ID. Gli ingressi correnti di `Build BridGHe Model` sono `Models`, `Phases`, `Combinations`, `Calculation`. Dopo le analisi premere Run sul Build e sul verificatore. Le vecchie connessioni di REV001/REV002 non sono compatibili con questa interfaccia. Le due file di armatura e il rapporto `n ritiro` sono inclusi nelle proprietà e nella verifica.

## Dal tracciato al modello di ponte

Gli [esempi 33–45](example/README.md#progettazione-del-modello-3345) coprono consigliatore delle campate con zone vietate, sezioni per progressiva, assegnazione, appoggi, connessioni, controventi, soletta, fasi, dettagli, frecce, storie per fatica e mappa delle verifiche. L'[esempio 45](example/45-ponte-completo-fasi/README.md) assembla un ponte curvo a tre travi, con campate 30+40+30 m, conci, shell e tre snapshot costruttivi. Non contiene carichi o risultati FEM inventati.

I tredici grafi sono risolti, salvati e riaperti dai test. La Release passa 218 controlli, 54 gruppi di regressione e due gruppi grafici; tutti i 238 valori della baseline numerica restano identici. Le prove native di UCS, vincoli, molle e link rigidi sono superate. Passano anche i cinque benchmark mobili (5.377 confronti su 80 posizioni) e i 30 confronti dei cinque contributi di fase; esiti in `docs/VERIFICHE.md`.
