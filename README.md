# BridGHe

Plugin Grasshopper per **Rhino 8 / .NET 8 / Windows x64**: modello parametrico → Straus7 R3 → risultati negli elementi → verifiche e dettagli di ponti misti con gPC.

La versione corrente comprende **136 componenti e 8 parametri**, con 145 GUID distinti incluso il plugin. Il refactoring del 19 ottobre 2026 semplifica gli input e rimuove i contenitori non utilizzati. I contratti aggiornati sono in [COMPONENTI](docs/COMPONENTI.md); le definizioni Grasshopper degli esempi sono state rigenerate.

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

Output plugin: `bin/BridGHe.Grasshopper/Release/net8.0-windows/BridGHe.Grasshopper.gha`. Libreria, test, file intermedi e pacchetti sono tutti sotto la `bin` della root. Ogni compilazione elimina automaticamente le vecchie cartelle `bin`/`obj` sparse nel progetto, con controllo dei confini e delle junction. Sono conservati risultati Straus, esempi e dipendenze esterne. Per simulare la pulizia: `tools/clean-builds.ps1 -WhatIf`.

Chiudere Rhino, estrarre il pacchetto sotto la cartella Libraries di Grasshopper e tenere insieme `.gha` e DLL. Avviare Rhino 8 con .NET 8; categorie `BridGHe` e `BridGHe-Checks`. Le DLL native Straus e Rhino non sono distribuite. La licenza API Straus è necessaria anche per calcolare le sezioni native. Percorso predefinito: `C:\Program Files\Straus7 R31`; in alternativa impostare `BRIDGHE_STRAUS_HOME` prima di avviare Rhino.

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
