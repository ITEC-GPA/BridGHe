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

## Passaggio alla 0.4.0

Conservare gli originali 0.3.0: la vecchia versione non legge i nuovi dati internalizzati. Gli ingressi storici supportati vengono letti per valore; risultati/cache precedenti non recuperabili richiedono ricostruzione e un nuovo Run. Per trasferire i risultati conservare il relativo `.bghr` e aggiornare Output Path; il file `.st7` serve per riaprire il modello nel solutore, non per rileggere il sidecar. I dati di schema 2 sono rifiutati, non convertiti cambiando il numero nel file.

Le cutoff bar e il doppio angolare sono ora esportabili; le funi Cable vengono fermate prima di Straus7. Restano non supportati spostamenti imposti, piastre con più di quattro vertici e Brep con regioni separate. Composite Beam Strain resta nei documenti ma il valore del suo ingresso `Strain` non è più usato: per il ritiro impiegare Bridge Shrinkage Action.

Nelle proprietà composte omogeneizzate I11 si riferisce all'asse orizzontale e I22 a quello verticale, con angolo zero. La correzione delle solette larghe e i nuovi valori di torsione/taglio richiedono di ricalcolare i progetti interessati. [Uso e limiti](docs/USO.md) · [Fasi e azioni](docs/FASI-E-AZIONI.md).

## Funzioni

- Nodi, travi, plate, materiali, proprietà, vincoli, carichi, combinazioni e costruzione per fasi; esportazione e analisi Straus7 R3.
- 23 componenti di sezione Straus: 21 famiglie parametriche, BXS e librerie BSL/BGL; restano le sezioni generiche e da geometria.
- Carichi mobili: forze, distribuzioni, sorgenti termiche, veicoli, corsie, centrifuga, template, percorsi e analisi dei casi statici generati.
- Geometrie gPC H, H inclinata e cassone; fasi, armatura, opzioni di verifica, pioli, irrigiditori, armatura trasversale, fatica e dettagli del cassone.
- Estrazione di tensioni, taglio, flusso di connessione, domanda/resistenza dei pioli ed esiti gPC. Grafici SVG e PNG senza interpolare i campioni.
- Anteprima del modello, delle sezioni supportate e delle armature schematiche; icone SVG/PNG dei componenti. Le sezioni native senza contorno GPC restano rappresentate dall’asse: la preview non è la proprietà meccanica esportata.

## Compilare e installare

Per installare usare il pacchetto completo del rilascio, con `.gha`, DLL e manifest. La compilazione è un'attività di sviluppo; richiede SDK .NET 8 o successivo e Python 3. Con il set ufficiale in `lib/GPC` basta la compilazione ordinaria, senza opzioni:

```powershell
dotnet build BridGHe.sln -c Release
```

Le opzioni `-Native` ed `-Examples` di `tools/build.ps1` (sezione «Riprodurre le verifiche») richiedono Straus7 e la licenza API; `-Examples` può anche sovrascrivere gli esempi. Non eseguirle come procedura di installazione.

BridGHe **0.4.0** usa le undici DLL GPC fissate in **`lib/GPC`** (GPCUtilities, GPCGeometry, DelaunayMesh, GPCModel, GPCModelData, GPC.Converter, GPC.Model.Persistence, GPC.Archive, GPCChecker.Concrete, GPCChecker.Steel e GPCChecker.CompositeBridge), con manifest, versioni e SHA-256. Non occorre compilare i repository fratelli per compilare il plugin. [GpcReferences.props](build/GpcReferences.props) verifica l'integrità del set prima di risolvere i riferimenti; il manifest viene distribuito come `bin/Release/gpc-release-set.json`. Le DLL comuni a BridGHe e ReBear provengono dallo stesso set.

Al caricamento in Grasshopper il plugin controlla le librerie GPC con cui calcola. Se gira dal `bin/Release` di un checkout, confronta le DLL accanto al plugin con `lib/GPC`: un `bin` non ricompilato dopo un aggiornamento del set viene segnalato. Confronta inoltre le DLL GPC caricate in Rhino con `gpc-release-set.json`: un altro plugin, per esempio ReBear, può aver caricato prima un set diverso. Le differenze compaiono nella riga di comando di Rhino e come avviso su Super Element Check, Bridge Detail Check, Bridge Section Check e sugli altri componenti che calcolano con GPC; un set non verificabile è segnalato solo nella riga di comando. Il controllo è provato dalla regressione `gpc-release-set` del progetto Smoke.

Il set in `lib/GPC` è il **rilascio ufficiale** (`candidate=false`) del 14 novembre 2026, `gpc-release-20261114-aw` (AW), prodotto da `Checker/build/Release-Set.ps1` senza `-Candidate` sui commit di origin/master Model `c2abf222` (GPCModel **5.51.0.1**, GPCModelData **0.3.0.31**, GPC.Model.Persistence **3.15.0.3**, GPC.Archive **1.2.0.4**, GPC.Converter **2.22.0.3**) e Checker `9c71ca65` (Concrete **0.37.0.2**, Steel **2.134.0.1**, CompositeBridge **1.31.0.2**), con Utilities `e57b440b` (GPCUtilities **3.0.0.0**) e Geometry `6734eee1` (GPCGeometry **2.3.0.0**, DelaunayMesh **2.0.0.14**). La compilazione ordinaria non richiede opzioni. Un set candidato in `lib/GPC` viene rifiutato; per collaudarlo in modo esplicito usare `-p:GpcAllowCandidate=true`. `-p:GpcPilot=true` resta un'opzione di sviluppo: compila contro i progetti Model e Checker fratelli. Il set ufficiale precedente è quello del [2 novembre](docs/GPC-RILASCIO-2026-11-02.md); i set ancora precedenti sono descritti nei resoconti del [1° novembre (sera)](docs/GPC-RILASCIO-2026-11-01-sera.md), del [1° novembre (mattina)](docs/GPC-RILASCIO-2026-11-01.md), del [30 ottobre](docs/GPC-RILASCIO-2026-10-30.md) e del [27 ottobre](docs/GPC-RILASCIO-2026-10-27.md).

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

```powershell
.\tools\build.ps1 -Native -Examples -BridgeGrillage -BridgeSuite -BridgeSpine -Package
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --beam-support
```

Gli script rigenerano e sovrascrivono gli esempi: salvare separatamente le modifiche manuali. Con il set ufficiale `tools/build.ps1` non richiede impostazioni. Solo per collaudare un set candidato in `lib/GPC` impostare prima `$env:GpcAllowCandidate = 'true'` nella stessa sessione di PowerShell; altrimenti la compilazione si ferma al controllo del set. I test Grasshopper usano un server componenti isolato per non caricare i plugin estranei installati nel profilo utente.

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

Le verifiche eseguite sono documentate nei resoconti con data, commit, ambiente e casi confrontati. I conteggi storici non rappresentano il gate della 0.4.0: per il pacchetto installato usare la sua ricevuta di rilascio. La riapertura di un grafo, la soluzione Straus7 e la verifica resistente sono controlli distinti; nessuno sostituisce automaticamente gli altri.

## Pacchetto e risultati esterni nella 0.4.0

Il pacchetto conserva soltanto i file `.bghr` referenziati dai GHX, dopo il controllo SHA-256, ed esclude gli output nativi Straus7. Un riferimento mancante o alterato interrompe il confezionamento. Vedere [Risultati esterni](docs/USO.md#risultati-esterni-nella-040). I risultati BGHR1 della 0.3.0 richiedono una nuova analisi GPC.
