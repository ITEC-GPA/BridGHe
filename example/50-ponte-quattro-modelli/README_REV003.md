# 50 REV003 — carichi mobili sul modello n0

Aprire **[50-ponte-quattro-modelli_REV003.ghx](50-ponte-quattro-modelli_REV003.ghx)** con la Release aggiornata. Geometria e sezioni provengono dal REV002, che resta invariato. **[Sezioni_conci_REV003.xlsx](Sezioni_conci_REV003.xlsx)** contiene gli stessi dati iniziali, con una riga per concio fisico.

## Flusso

Le quattro rigidezze e le fasi 10, 20, 30 e 40 rimangono quelle del REV002. Nel blocco orizzontale **M03 n0** è aggiunta una quinta analisi, dedicata ai carichi mobili:

1. Quattro `Moving Point Force` → `Moving Vehicle` → `Moving Load Template`.
2. `Bridge Grillage Mesh / Grillage Geometry` + template → **Bridge Straight Moving Path**. Il percorso segue la quota delle aste trasversali di soletta e controlla che le ruote restino nella larghezza dell'impalcato.
3. Lo stesso `Build Model` n0 → `Attach Moving Paths` → **Moving Static Analysis**. Quest'analisi restituisce soltanto le posizioni generate, senza riapplicare peso proprio o carichi meccanici delle altre analisi.
4. `Cases` → **Bridge Moving Combinations** → `Action IDs` e `Cases` → `Bridge Action`, con comportamento **2, ShortTerm/n0**. Le azioni entrano nella **fase 30** già presente.
5. I quattro modelli calcolati più quello mobile → `Build BridGHe Model`; la tabella estesa alimenta `Combinations`. Seguono verifiche di sezione, pioli, irrigiditori, grafici e report già collegati.

Il modello mobile mantiene geometria, vincoli e proprietà n0. La mappa fessurato/non fessurato resta quella determinata con `Dead` in M01: non viene ricalcolata a ogni posizione del veicolo. Il ritiro resta non fessurato.

## Input del traffico

Tutti i nuovi input sono collegati a slider, toggle o parametri nominati sul canvas.

| Input | Valore iniziale | Significato |
|---|---:|---|
| Forza per ruota | 150.000 N | Quattro forze globali −Z: 600 kN quando il veicolo è interamente sul ponte |
| Interasse assi | 1.200 mm | Asse anteriore X = 0; posteriore X = −interasse |
| Semicarreggiata | 1.000 mm | Ruote a Y = ±1.000 mm rispetto al percorso |
| Eccentricità corsia | +1.500 mm | Verso la sinistra del tracciato |
| Divisioni percorso | 30 | Sul tracciato di 60.000 mm: passo di 2.000 mm |
| Coefficiente traffico | 1 | Fattore esplicito applicato alle combinazioni |
| Combinazione base | `DEMO_CUMULATIVA` | Riga esistente cui aggiungere una sola posizione |
| Posizioni isolate | False | Se True, aggiunge una combinazione di solo traffico per ogni posizione |
| Analisi automatica | False | Usare **Run** per evitare un'analisi a ogni modifica |

Il veicolo è **di prova**, senza attribuzione automatica a un modello normativo, coefficienti dinamici, γ o ψ. L'helper geometrico gestisce tracciati rettilinei con pendenza trasversale costante e un template esplicito a una corsia. Curve o geometrie non complanari richiedono un percorso mobile definito appositamente e vengono rifiutate dall'helper.

## Combinazioni e concomitanza

Le cinque combinazioni precedenti sono conservate con coefficienti mobili nulli. Per ogni posizione vengono aggiunti un Action ID (`Q_MOV_0001`, ecc.) e una combinazione: `DEMO_CUMULATIVA + Q_MOV_0001`, `DEMO_CUMULATIVA + Q_MOV_0002`, …

Ogni riga contiene **una sola posizione**. Non si sommano posizioni diverse e non si mescolano massimi di sollecitazioni non concomitanti. Il numero delle righe si aggiorna dai casi restituiti da Straus; il pannello `ACTION ID — POSIZIONE` rende consultabile l'associazione. L'opzione delle combinazioni isolate serve per leggere il contributo del solo traffico. La tabella di base e il fattore restano scelte esplicite: il componente non genera automaticamente combinazioni normative.

## Esecuzione

1. **Run** su `Bridge Read Section Excel` per leggere il foglio REV003.
2. Eseguire **M01**: `Dead` costruisce la mappa delle rigidezze. Prima di questo passo i selettori M02/M03 possono segnalare i casi delle travi mancanti, perché le travi composte attendono la mappa. Non eseguire i modelli parziali.
3. Eseguire **M02, M03, M04**, poi **Moving Static Analysis** nel blocco M03. Controllare percorso, passo, casi generati e associazioni.
4. **Run** su `Build BridGHe Model`, quindi su `Super Element Check`.
5. Consultare grafici, pioli e irrigiditori. Generare il report con **Generate**, destinazione `Report_50_REV003.docx`.

I file Straus sono in `calcoli/REV003`. Il GHX distribuito non contiene risultati FEM e non lancia analisi all'apertura. `Moving Static Analysis` conserva i risultati con Run=False finché modello, veicolo e percorso sono invariati; una modifica li invalida e richiede una nuova esecuzione. Dopo il ricalcolo ripetere Build e verificatore.

## Ambito e ripetibilità

Il collaudo nativo del **26 ottobre 2026** ha completato cinque analisi Straus, **31 posizioni mobili**, **36 combinazioni** e **32.832 risultati di sezione**, con tre rami di dettaglio e sei grafici SVG. Sono verificati equilibrio globale nelle sei componenti per ogni posizione, 120 assegnazioni delle forze alle aste di soletta, conservazione delle proprietà n0, associazione alla fase 30, cache dei risultati e invalidazione al cambio del percorso. Nessun errore nei componenti GH; gli ingressi del report coprono i risultati dei dettagli. Il test non genera un report Word del ponte.

Il [resoconto numerico](REV003-native-validation.json) conserva valori, tolleranze e hash del GHX e delle librerie effettivamente usate. Questa esecuzione precede l'integrazione finale del set GPC: Concrete 0.24.0.4 e CompositeBridge 1.19.0.4. I collaudi del set integrato sono documentati separatamente. Le cinque analisi Straus hanno zero errori e zero avvisi; il tempo maggiore è nella rielaborazione GPC di tutte le sezioni per pioli e irrigiditori.

Il checker segue l'ipotesi concordata **N–Mx–Vy**: T, M2 e V1 sono esclusi. L'eccentricità del traffico produce anche effetti esclusi, che richiedono controlli dedicati. Questa prova del flusso non costituisce verifica completa del ponte; mancano inoltre convergenza di mesh/passo mobile, combinazioni normative e verifiche specifiche di fatica e dinamica del traffico.

```powershell
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --example50-rev003 --check
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --regression bridge-moving-workflow
dotnet run --project tests/BridGHe.Smoke -c Release --no-build -- --example50-rev003 --native
```

L'ultimo comando richiede la licenza Straus, ricalcola cinque analisi in `artifacts/rev003-native` e non modifica il GHX. Il generatore `--example50-rev003` rifiuta di sovrascrivere una revisione già esistente.
