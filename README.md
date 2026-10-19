# BridGHe

Plugin Grasshopper per **Rhino 8 / .NET 8 / Windows x64**: modello parametrico → Straus7 R3 → risultati negli elementi → verifiche e dettagli di ponti misti con gPC.

La versione corrente comprende **136 componenti e 8 parametri**, con 142 GUID distinti incluso il plugin. Il refactoring del 19 ottobre 2026 semplifica gli input e rimuove i contenitori non utilizzati. I contratti aggiornati sono in [COMPONENTI](docs/COMPONENTI.md); le definizioni Grasshopper degli esempi sono state rigenerate.

## Esempi

- [01–10: casi analitici, carichi mobili e cinque esempi di costruzione per fasi](examples/README.md).
- [11: ponte misto a graticcio, quattro travi, veicolo mobile e tre rigidezze](examples/bridge-grillage/README.md).
- [12–15: ponti continui e curvi, tre contributi meccanici più ritiro](examples/bridge-suite/README.md).
- [16–18: spina centrale H2000 e due laterali H1600, una/tre campate o due campate curve](examples/bridge-suite/README.md#esempi-aggiuntivi-16-18-spina-centrale-e-due-travi-laterali). Tre fasi più ritiro differenziale, 212 controlli superati.

I modelli sono esempi riproducibili, con ipotesi, unità, confronti analitici e tolleranze dichiarati. Il percorso automatico dei superelementi H rifiuta torsione e flessione minore fuori campo; il nuovo componente nativo gPC gestisce anche geometrie a cassone e anima inclinata. Non equivale a una verifica completa di ogni ponte.

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

Le DLL gPC devono essere compilate nei repository fratelli `Utilities`, `Geometry`, `Model` e `Checker`. I riferimenti sono espliciti in [GpcReferences.props](build/GpcReferences.props); `RepositoryRoot` permette una disposizione differente. I sorgenti delle librerie non sono modificati.

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

Gli script rigenerano e sovrascrivono gli esempi: salvare separatamente le modifiche manuali. I test Grasshopper usano un server componenti isolato per non caricare i plugin estranei installati nel profilo utente.

## Documentazione

- [Metodi cambiati, rimozioni e compatibilità](docs/MIGRAZIONE.md).
- [Uso e convenzioni](docs/USO.md).
- [Verifiche eseguite e limiti](docs/VERIFICHE.md).
- [Catalogo dei componenti e porte attuali](docs/COMPONENTI.md).
- [Icone e rigenerazione](docs/ICONE.md).
- [Correzione degli appoggi distribuiti e differenze numeriche](docs/beam-support-fix.json).
- [Misura dell'ottimizzazione delle letture di taglio](docs/shear-performance.json).

Organizzazione delle palette e componenti non presenti negli esempi: [audit di utilizzo](docs/USO-COMPONENTI.md). Sezioni in `03-Sections`, appoggi in `07-Attributes`, tutti i carichi in `09-Loads`.
# Soletta con due file di armatura

`Concrete Slab Section` conserva i primi quattro input e aggiunge `Bottom rebar area` e `Bottom concrete cover`. Le aree sono totali sulla larghezza della soletta, in mm²; le distanze sono misurate dalla faccia della soletta all'asse delle barre, in mm. Area zero disattiva la fila. Gli archivi precedenti mantengono la sola fila superiore. Le due file entrano nelle proprietà omogeneizzate e nel verificatore gPC, con interpolazione di area e posizione lungo le travi a sezione variabile.


## Visualizzazione

Il menu **BridGHe → Visualizzazione…** e il componente **BridGHe Panel** aprono le preferenze persistenti. Sono disponibili geometria FEM, sezioni fisiche, superfici, armature schematiche, carichi, appoggi, svincoli, assi locali, etichette, scala dei simboli e filtro per nome esatto del caso. **Display Settings → Preview BridGHe Model**, nel gruppo **15-Preview**, applica opzioni locali; disattivare la preview automatica globale per evitare sovrapposizioni con gli output a monte. L'interruttore generale continua a controllare tutte le preview.

Le proprietà omogeneizzate conservano acciaio e soletta nella preview delle beam dopo l'override. Le armature sono rappresentate con tre linee schematiche per fila attiva: gli input definiscono aree totali, non una distinta ferri. I test verificano i solidi Rhino, gli archivi, le opzioni locali e l'assenza di modifiche ai dati FEM. La cattura della viewport GPU nel processo Rhino nascosto restituisce un errore nativo su questa macchina; la prova visuale interattiva rimane da eseguire in Rhino. Il comando diagnostico `--regression display-settings --preview-capture` è separato dai test geometrici.
