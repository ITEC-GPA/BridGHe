# BridGHe

Plugin Grasshopper per Rhino 8: modello parametrico → Straus7 R3 → risultati negli elementi → MeCheck2 con librerie gPC.

La versione 0.1.0 è una trasposizione compilabile e collaudata nel runtime Rhino 8. La catena nativa di analisi Straus **non è ancora validata**: nella sessione di sviluppo `St7Init` ha restituito `-8`, hardware lock API non disponibile. I dettagli e i limiti del verificatore sono in [VERIFICHE](docs/VERIFICHE.md).

## Contenuto

- Tutti i **16 componenti MeCheck2**, con porte, ordine, accessi e valori predefiniti mantenuti.
- Componenti FeMM conservati per materiali, sezioni, elementi, attributi, carichi, combinazioni, modello, risultati ed esportazione R3.
- **23 nuovi componenti di sezione Straus**: 15 forme standard, 6 forme BGL parametriche, BXS e librerie BSL/BGL. Restano le sezioni generiche e da geometria già presenti nella base.
- **10 componenti per carichi mobili**, dall'oggetto di carico al template, percorso, associazione al modello e analisi dei casi statici generati.
- GUID nuovi per plugin e tutti i componenti/parametri; nessuna dipendenza da SAP, ETABS, form finding o plugin R2.
- Icone originali per il plugin, tutti i componenti e i parametri, con sorgenti SVG e PNG incorporati.

Le diciture storiche di alcune porte, comprese quelle contenenti “FeMM”, sono intenzionalmente conservate per rispettare il contratto richiesto. I vecchi file Grasshopper non vengono riconnessi automaticamente: i GUID sono nuovi.

## Aprire e compilare

Aprire `BridGHe.sln` con un ambiente .NET che supporti `net8.0-windows`, oppure dalla cartella del progetto:

```powershell
.\tools\build.ps1 -Smoke -Package
```

Requisiti: Windows x64, SDK .NET 8 o successivo, Rhino 8 eseguito con runtime .NET 8, Python 3 per l'audit. Le prove sono state eseguite con SDK 9.0.319 e riferimenti Rhino/Grasshopper `8.27.26019.16021`.

Le DLL gPC devono essere già compilate nei repository fratelli `Utilities`, `Geometry`, `Model` e `Checker`. I sette riferimenti sono espliciti in [GpcReferences.props](build/GpcReferences.props); i sorgenti di quei repository non sono modificati. Per una diversa disposizione delle cartelle si può impostare la proprietà MSBuild `RepositoryRoot`.

Output principale: `src/BridGHe.Grasshopper/bin/Release/net8.0-windows/BridGHe.Grasshopper.gha`. Il pacchetto viene creato in `artifacts/packages` con dipendenze gestite e manifest SHA-256.

## Installazione

1. Chiudere Rhino. Estrarre il pacchetto in una sottocartella della cartella Libraries di Grasshopper, accessibile dal menu **File → Special Folders → Components Folder**.
2. Tenere `.gha` e DLL del pacchetto insieme. Se Windows ha bloccato i file scaricati, rimuovere il blocco dalle proprietà del file ZIP prima di estrarlo.
3. Avviare Rhino 8 con .NET 8 e aprire Grasshopper. Le categorie sono `BridGHe` e `BridGHe-Checks`.
4. Per le funzioni native occorre Straus7 R3 x64 con licenza API disponibile. Il percorso predefinito è `C:\Program Files\Straus7 R31`; per altre installazioni impostare `BRIDGHE_STRAUS_HOME` prima di avviare Rhino. La DLL cercata è `Bin64\St7API.dll`.

Il pacchetto non include le DLL native Straus né le DLL Rhino/Grasshopper. Non è stato installato automaticamente nel profilo utente.

## Consultazione

- [Passaggi eseguiti e metodi cambiati](docs/MIGRAZIONE.md).
- [Uso, sezioni, carichi mobili e convenzioni](docs/USO.md).
- [Controlli eseguiti, limiti e validazione nativa da completare](docs/VERIFICHE.md).
- [Inventario dei componenti](docs/COMPONENTI.md).
- [Icone, catalogo e rigenerazione](docs/ICONE.md).
- `migration/component-map.json`: GUID originali e nuovi; `cleanup-removed.json`: eliminazioni; `port-contract-baseline.json`: firme delle porte per l'audit senza il repository originale.
